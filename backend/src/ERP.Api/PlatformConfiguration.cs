using System.Globalization;
using Npgsql;

namespace ERP.Api;

/// <summary>
/// Adapts the environment a platform-as-a-service hands the container into the
/// configuration this application actually reads.
/// </summary>
/// <remarks>
/// <para>
/// Under <c>docker compose</c> the compose file is a translation layer: it maps
/// <c>POSTGRES_PASSWORD</c> and friends onto <c>ConnectionStrings__Postgres</c>, which
/// is the name the code reads. Deployed directly - OneDeploy, Railway, Fly, App
/// Runner - there is no compose file and nothing performs that mapping. The platform
/// injects its own names, and an application that only understands the compose names
/// starts up pointing at nothing.
/// </para>
/// <para>
/// This class closes that gap in the application rather than in a deployment
/// checklist, because a checklist is followed once and this is needed on every deploy.
/// Everything here is a fallback: an explicitly supplied setting always wins, so a
/// local <c>appsettings.Development.json</c>, a compose file, and a managed platform
/// all keep working without knowing about each other.
/// </para>
/// </remarks>
internal static class PlatformConfiguration
{
    /// <summary>The connection string the rest of the application reads.</summary>
    private const string PostgresConnectionStringKey = "ConnectionStrings:Postgres";

    /// <summary>The same key in the environment-variable spelling.</summary>
    private const string PostgresConnectionStringVariable = "ConnectionStrings__Postgres";

    /// <summary>The configuration key holding the origins CORS will allow.</summary>
    private const string CorsOriginsKey = "Cors:AllowedOrigins";

    /// <summary>The same key in the environment-variable spelling.</summary>
    private const string CorsOriginsVariable = "Cors__AllowedOrigins";

    /// <summary>
    /// Derives configuration the hosting platform supplies under its own names.
    /// </summary>
    /// <param name="builder">The application builder being configured.</param>
    /// <remarks>
    /// Added as the last configuration source so it takes precedence over
    /// <c>appsettings.json</c>, which ships a localhost placeholder that would
    /// otherwise mask a perfectly good set of platform variables. Each individual
    /// derivation still stands aside when the operator has set the real key
    /// explicitly.
    /// </remarks>
    internal static void AddPlatformConfiguration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Dictionary<string, string?> derived = [];

        AddDerivedConnectionString(derived);
        AddDerivedCorsOrigins(derived);

        if (derived.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(derived);
        }
    }

    /// <summary>
    /// Resolves the URL the server should listen on from the platform's port.
    /// </summary>
    /// <returns>The listen URL, or <see langword="null"/> to leave the host's default.</returns>
    /// <remarks>
    /// Two things matter and both are easy to get wrong. The port must come from
    /// <c>PORT</c>, which the platform chooses and injects; and the binding must be
    /// <c>0.0.0.0</c> rather than <c>localhost</c>, because the health probe reaches
    /// the container from outside its network namespace and a loopback binding is
    /// unreachable from there - the container looks healthy from inside and dead from
    /// without.
    /// </remarks>
    internal static string? ResolveListenUrl()
    {
        string? port = Environment.GetEnvironmentVariable("PORT");

        if (string.IsNullOrWhiteSpace(port)
            || !int.TryParse(port, CultureInfo.InvariantCulture, out int parsed)
            || parsed is < 1 or > 65535)
        {
            return null;
        }

        // Plain HTTP on purpose: this is the address inside the container. TLS is
        // terminated by the platform's proxy in front of it, and a certificate the
        // container cannot obtain would make it unreachable rather than secure.
#pragma warning disable S5332 // Using http protocol is insecure
        return $"http://0.0.0.0:{parsed.ToString(CultureInfo.InvariantCulture)}";
#pragma warning restore S5332
    }

    /// <summary>
    /// Builds an Npgsql connection string from the parts a platform provides.
    /// </summary>
    /// <param name="derived">The collection to add the derived setting to.</param>
    /// <remarks>
    /// <para>
    /// Managed PostgreSQL is advertised two ways: as discrete <c>PGHOST</c> /
    /// <c>PGDATABASE</c> / <c>PGUSER</c> / <c>PGPASSWORD</c> variables, and as a single
    /// <c>DATABASE_URL</c> URI. Npgsql accepts neither. It wants keyword form -
    /// <c>Host=…;Port=…;Database=…;Username=…;Password=…</c> - so a <c>DATABASE_URL</c>
    /// that Node or Rails would consume directly is rejected here with an error that
    /// names neither the variable nor the format.
    /// </para>
    /// <para>
    /// The discrete variables are preferred over the URI: they need no parsing, and a
    /// password containing a slash or an at-sign survives them intact, which is exactly
    /// where hand-rolled URI parsing tends to fail.
    /// </para>
    /// </remarks>
    private static void AddDerivedConnectionString(Dictionary<string, string?> derived)
    {
        // An explicit connection string is the operator saying precisely what they
        // want, and it outranks anything inferred from the environment.
        if (!string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(PostgresConnectionStringVariable)))
        {
            return;
        }

        NpgsqlConnectionStringBuilder? connection =
            FromDiscreteVariables() ?? FromDatabaseUrl();

        if (connection is null)
        {
            return;
        }

        // The managed database sits on the project's private network and is not
        // published to the internet. It commonly presents a self-signed certificate,
        // so a strict verification default turns a working database into a startup
        // failure; Prefer encrypts where the server offers it without demanding a
        // chain the container has no way to validate.
        connection.SslMode = SslMode.Prefer;

        derived[PostgresConnectionStringKey] = connection.ConnectionString;
    }

    /// <summary>Reads the discrete <c>PG*</c> variables, if the platform set them.</summary>
    /// <returns>The connection, or <see langword="null"/> when they are absent.</returns>
    private static NpgsqlConnectionStringBuilder? FromDiscreteVariables()
    {
        string? host = Environment.GetEnvironmentVariable("PGHOST");
        string? database = Environment.GetEnvironmentVariable("PGDATABASE");
        string? user = Environment.GetEnvironmentVariable("PGUSER");

        // Host, database and user are the irreducible set. A password may legitimately
        // be empty for trust authentication, so its absence is not disqualifying.
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(user))
        {
            return null;
        }

        NpgsqlConnectionStringBuilder connection = new()
        {
            Host = host,
            Database = database,
            Username = user,
            Password = Environment.GetEnvironmentVariable("PGPASSWORD"),
        };

        string? port = Environment.GetEnvironmentVariable("PGPORT");

        if (!string.IsNullOrWhiteSpace(port)
            && int.TryParse(port, CultureInfo.InvariantCulture, out int parsed))
        {
            connection.Port = parsed;
        }

        return connection;
    }

    /// <summary>Parses a <c>DATABASE_URL</c> URI into Npgsql's keyword form.</summary>
    /// <returns>The connection, or <see langword="null"/> when it is absent or unusable.</returns>
    /// <remarks>
    /// A malformed URI returns null rather than throwing. The application will then
    /// fail on its configured connection string with a message about the database,
    /// which is a better diagnostic than a parse exception thrown before logging is
    /// even running.
    /// </remarks>
    private static NpgsqlConnectionStringBuilder? FromDatabaseUrl()
    {
        string? databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrWhiteSpace(databaseUrl)
            || !Uri.TryCreate(databaseUrl, UriKind.Absolute, out Uri? uri)
            || (!uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string[] credentials = uri.UserInfo.Split(':', 2);

        NpgsqlConnectionStringBuilder connection = new()
        {
            Host = uri.Host,
            // A URI carries its database as a leading-slash path segment.
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        if (uri.Port > 0)
        {
            connection.Port = uri.Port;
        }

        // Credentials arrive percent-encoded, because a password containing an
        // at-sign would otherwise terminate the authority component early.
        if (credentials.Length > 0 && !string.IsNullOrEmpty(credentials[0]))
        {
            connection.Username = Uri.UnescapeDataString(credentials[0]);
        }

        if (credentials.Length > 1 && !string.IsNullOrEmpty(credentials[1]))
        {
            connection.Password = Uri.UnescapeDataString(credentials[1]);
        }

        return string.IsNullOrWhiteSpace(connection.Database) ? null : connection;
    }

    /// <summary>
    /// Expands a delimited list of CORS origins into the indexed keys the binder wants.
    /// </summary>
    /// <param name="derived">The collection to add the derived settings to.</param>
    /// <remarks>
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> represents an
    /// array as <c>Cors__AllowedOrigins__0</c>, <c>__1</c> and so on, which is an
    /// unreasonable thing to expect somebody to type into a deployment console - and
    /// setting the obvious <c>Cors__AllowedOrigins</c> instead binds a scalar where the
    /// code asks for an array, yielding an empty origin list and a frontend blocked by
    /// the browser while the API reports itself perfectly healthy. Accepting the
    /// obvious spelling and expanding it here removes that trap.
    /// </remarks>
    private static void AddDerivedCorsOrigins(Dictionary<string, string?> derived)
    {
        string? origins = Environment.GetEnvironmentVariable(CorsOriginsVariable);

        if (string.IsNullOrWhiteSpace(origins))
        {
            return;
        }

        string[] parsed = origins
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // A trailing slash makes an origin fail to match: the browser sends
            // "https://app.example.com", never "https://app.example.com/".
            .Select(origin => origin.TrimEnd('/'))
            .Where(origin => origin.Length > 0)
            .ToArray();

        for (int index = 0; index < parsed.Length; index++)
        {
            derived[$"{CorsOriginsKey}:{index.ToString(CultureInfo.InvariantCulture)}"] =
                parsed[index];
        }
    }
}
