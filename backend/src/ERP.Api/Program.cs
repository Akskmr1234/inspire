using System.Diagnostics;
using System.Globalization;
using Asp.Versioning;
using ERP.Api.Middleware;
using ERP.Application;
using ERP.Identity;
using ERP.Infrastructure;
using ERP.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;

namespace ERP.Api;

/// <summary>
/// The API composition root.
/// </summary>
/// <remarks>
/// Registration is grouped into <c>Add*</c> extension methods per concern rather
/// than inlined here, so this file stays a readable table of contents for the
/// application's wiring as the platform grows.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1052:Static holder types should be Static or NotInheritable",
    Justification =
        "WebApplicationFactory<TEntryPoint> takes the entry-point type as a generic " +
        "argument, and a static class cannot be used as one. Making this static would " +
        "make the API untestable through the standard in-memory host. The private " +
        "constructor prevents instantiation, which is what the rule is actually for.")]
public partial class Program
{
    private Program()
    {
    }

    /// <summary>Application entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code: 0 on clean shutdown, 1 on startup failure.</returns>
    public static async Task<int> Main(string[] args)
    {
        // A bootstrap logger captures failures that happen before configuration
        // has been read. Without it, a bad connection string or a malformed
        // appsettings file produces a silent exit with no diagnostic at all.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .CreateBootstrapLogger();

        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            // Before anything reads configuration: turns the names a hosting platform
            // injects - PGHOST, DATABASE_URL, PORT - into the names this application
            // reads. Without it a deployed container starts up pointing at the
            // localhost placeholder in appsettings.json.
            builder.AddPlatformConfiguration();

            if (PlatformConfiguration.ResolveListenUrl() is { } listenUrl)
            {
                builder.WebHost.UseUrls(listenUrl);
            }

            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning));

            ConfigureServices(builder);

            WebApplication app = builder.Build();
            ConfigurePipeline(app);

            Log.Information(
                "Inspire ERP API starting in {Environment}",
                app.Environment.EnvironmentName);

            // Before the first request is served, so a request never arrives at a
            // schema that is mid-migration or a database with no permission
            // catalogue. Failure here aborts startup rather than surfacing later as
            // an inexplicable authorisation error.
            // Migrations run by default. A container deployed against a fresh managed
            // database has no schema at all, and an API that waits to be migrated by
            // hand serves nothing but 500s until somebody does it. Set
            // Erp__Database__ApplyMigrationsOnStartup to false where migrations are a
            // separate release step - and do so once they grow long enough to threaten
            // the platform's startup health-check budget, because a container still
            // migrating is a container not yet answering.
            await DatabaseInitializer.InitializeAsync(
                app.Services,
                applyMigrations: builder.Configuration.GetValue(
                    "Erp:Database:ApplyMigrationsOnStartup", true));

            await app.RunAsync();
            return 0;
        }
        catch (Exception ex) when (ex is not HostAbortedException)
        {
            Log.Fatal(ex, "Inspire ERP API terminated unexpectedly during startup");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;

        services.AddApplication();
        services.AddInfrastructure(builder.Configuration);
        services.AddErpIdentity(builder.Configuration);

        services.AddControllers();
        services.AddEndpointsApiExplorer();

        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // Correlating a user-reported error with its log entry is
                // otherwise guesswork across 1,000+ concurrent users.
                context.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;
            });

        // URL-segment versioning (/api/v1/...) keeps the version visible in logs,
        // browser history, and support tickets, which header-based versioning
        // does not.
        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        ConfigureSwagger(services);

        // Liveness is intentionally dependency-free: it answers "is the process
        // up", so an orchestrator does not restart a healthy pod merely because
        // the database is briefly unreachable. Readiness carries the dependency
        // checks and is what a load balancer should gate traffic on.
        services.AddHealthChecks();

        services.AddCors(options => options.AddPolicy(
            "erp-web",
            policy => policy
                .WithOrigins(
                    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? ["http://localhost:5173"])
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));
    }

    private static void ConfigureSwagger(IServiceCollection services) =>
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Inspire ERP API",
                Version = "v1",
                Description =
                    "Multi-tenant, multi-firm, multi-branch ERP platform: accounting, " +
                    "inventory, sales, manufacturing, and service management.",
            });

            // Surface the XML documentation written against the DTOs and
            // controllers so Swagger is genuinely useful rather than a bare
            // route list.
            string xmlPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{typeof(Program).Assembly.GetName().Name}.xml");

            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description =
                    "JWT from POST /api/v1/auth/login. Paste the access token only, " +
                    "without the 'Bearer ' prefix.",
            });

            // Microsoft.OpenApi v2, which Swashbuckle 10 targets, replaced the
            // inline Reference object with a dedicated reference type, and takes
            // the requirement as a factory over the document.
            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer")] = [],
            });
        });

    private static void ConfigurePipeline(WebApplication app)
    {
        // Deployed behind a reverse proxy that terminates TLS, the request arrives
        // over plain HTTP carrying X-Forwarded-*. Without this the application
        // believes every request is insecure and generates http:// links for an
        // https:// site.
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost,

            // The proxy is the platform's own, on an address the container has no way
            // to know. Restricting to known proxies here would reject its headers and
            // defeat the point; the container is not reachable except through it.
            KnownIPNetworks = { },
            KnownProxies = { },
        });

        app.UseSerilogRequestLogging(options => options.GetLevel = ResolveRequestLogLevel);

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                foreach (string groupName in app.DescribeApiVersions().Select(d => d.GroupName))
                {
                    options.SwaggerEndpoint(
                        $"/swagger/{groupName}/swagger.json",
                        groupName.ToUpperInvariant());
                }
            });
        }
        else
        {
            app.UseHsts();
        }

        // Off by default wherever the platform assigned the port, which is the reliable
        // signal that TLS is being terminated in front of this process. Redirecting
        // there sends the health probe - which speaks plain HTTP to the container - to
        // an https:// URL the container does not serve, and the deployment is rolled
        // back for a healthy application.
        if (app.Configuration.GetValue(
            "Erp:Hosting:HttpsRedirection", PlatformConfiguration.ResolveListenUrl() is null))
        {
            app.UseHttpsRedirection();
        }

        app.UseCors("erp-web");
        app.UseAuthentication();

        // Between authentication and authorisation, and before any endpoint can
        // reach the database: there must be no window in which a query could run
        // without a tenant established.
        app.UseTenantResolution();

        app.UseAuthorization();

        app.MapControllers();

        // The root path, because a deployment platform probes GET / unless told
        // otherwise, and an API that answers 404 there is relying on 404 counting as
        // "not a server error" - true today, and a poor thing to stake a rollback on.
        // Dependency-free like the liveness probe: it reports that the process is
        // serving, not that the database is reachable.
        app.MapGet("/", () => Results.Ok(new
        {
            name = "Inspire ERP API",
            status = "ok",
            documentation = "/swagger",
        })).AllowAnonymous();

        HealthCheckOptions livenessOptions = new()
        {
            Predicate = _ => false,
        };

        app.MapHealthChecks("/health", livenessOptions);
        app.MapHealthChecks("/health/live", livenessOptions);

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
        });
    }

    /// <summary>
    /// Decides the severity at which a completed HTTP request is logged.
    /// </summary>
    /// <param name="httpContext">The request being logged.</param>
    /// <param name="elapsedMilliseconds">How long the request took.</param>
    /// <param name="exception">The unhandled exception, if any.</param>
    /// <returns>The level to log at.</returns>
    private static LogEventLevel ResolveRequestLogLevel(
        HttpContext httpContext,
        double elapsedMilliseconds,
        Exception? exception)
    {
        if (exception is not null || httpContext.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        // Container health probes fire every few seconds; logging each at
        // Information buries everything of actual interest.
        if (httpContext.Request.Path.StartsWithSegments("/health"))
        {
            return LogEventLevel.Verbose;
        }

        return LogEventLevel.Information;
    }
}
