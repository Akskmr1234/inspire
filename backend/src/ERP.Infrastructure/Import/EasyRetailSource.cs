using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ERP.Application.Platform.Import;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using Microsoft.Data.SqlClient;

namespace ERP.Infrastructure.Import;

/// <summary>Reads an EasyRetail database, or writes the script that does it on the shop's computer.</summary>
/// <remarks>
/// <para>
/// EasyRetail keeps its books in SQL Server on the shop's own computer, which a server in
/// the cloud usually cannot reach. So there are two ways in: the server reads the database
/// itself when it can, and otherwise the shop runs a script that reads it and saves the
/// same document to a file, which is uploaded. Both run <see cref="EasyRetailQueries"/>.
/// </para>
/// <para>
/// Read only, and nothing else: the connection asks for read intent and the queries are
/// all <c>SELECT</c>s.
/// </para>
/// </remarks>
public sealed class EasyRetailSource : IEasyRetailSource
{
    /// <summary>The format name written into every document.</summary>
    public const string Format = "easyretail";

    /// <summary>The version of the document's shape.</summary>
    public const int Version = 1;

    private const string ScriptHeader = """
        <#
          EasyRetail export for Aider ERP
          ===============================

          Reads an EasyRetail database and saves everything Aider ERP imports - accounts,
          customers, suppliers, products, balances and stock - into one file.
          It only reads. Nothing in EasyRetail is changed.

          How to use it
            1. Copy this file to the computer that runs EasyRetail.
            2. Right-click it and choose "Run with PowerShell".
               If Windows will not run it, open PowerShell in this folder and type:
                 powershell -ExecutionPolicy Bypass -File .\easyretail-export.ps1
            3. Answer the questions. Press Enter to accept the suggestion in [brackets].
            4. The file easyretail-export.json is saved on the Desktop.
               In Aider ERP open Settings > Import data and choose that file.

          The questions can also be answered on the command line:
            -Server ".\SQLEXPRESS" -Database "EasyRetail2025" -AsAt "2026-10-31"
            -User sa -Password "..."   (leave out to sign in with Windows)
            -Out "C:\export\easyretail-export.json"
        #>
        param(
            [string]$Server,
            [string]$Database,
            [string]$User,
            [string]$Password,
            [string]$AsAt,
            [string]$Out
        )

        $ErrorActionPreference = 'Stop'

        if (-not $Server) {
            $Server = Read-Host 'SQL Server name [.\SQLEXPRESS]'
            if (-not $Server) { $Server = '.\SQLEXPRESS' }
        }
        if (-not $Database) {
            $Database = Read-Host 'EasyRetail database name, for example EasyRetail2025'
        }
        if (-not $AsAt) {
            $AsAt = Read-Host 'Balances as at, yyyy-mm-dd [today]'
            if (-not $AsAt) { $AsAt = (Get-Date).ToString('yyyy-MM-dd') }
        }
        if (-not $Out) {
            $Out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'easyretail-export.json'
        }

        $asAtDate = [datetime]::ParseExact($AsAt, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)

        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
        $builder['Data Source'] = $Server
        $builder['Initial Catalog'] = $Database
        $builder['Application Name'] = 'Aider ERP export'
        if ($User) {
            $builder['User ID'] = $User
            $builder['Password'] = $Password
        } else {
            $builder['Integrated Security'] = $true
        }

        $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
        $connection.Open()

        function Read-Section([string]$sql) {
            $command = $connection.CreateCommand()
            $command.CommandText = $sql
            $command.CommandTimeout = 600
            $parameter = $command.Parameters.Add('@AsAt', [System.Data.SqlDbType]::Date)
            $parameter.Value = $asAtDate
            $reader = $command.ExecuteReader()
            $rows = New-Object System.Collections.Generic.List[object]
            try {
                while ($reader.Read()) {
                    $row = [ordered]@{}
                    for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                        $row[$reader.GetName($i)] = $reader.GetValue($i)
                    }
                    $rows.Add([pscustomobject]$row)
                }
            } finally {
                $reader.Close()
            }
            return ,$rows.ToArray()
        }

        $export = [ordered]@{
            format = 'easyretail'
            version = 1
            database = $Database
            asAt = $AsAt
            exportedAt = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss')
        }


        """;

    private const string ScriptFooter = """

        $connection.Close()

        Write-Host 'Writing the file...'
        $json = $export | ConvertTo-Json -Depth 5 -Compress
        [System.IO.File]::WriteAllText($Out, $json, (New-Object System.Text.UTF8Encoding $false))

        Write-Host ''
        Write-Host "Done. Saved to $Out"
        Write-Host 'In Aider ERP open Settings > Import data and choose this file.'
        Read-Host 'Press Enter to close'

        """;

    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="EasyRetailSource"/> class.</summary>
    /// <param name="clock">The clock, for today's date.</param>
    public EasyRetailSource(IClock clock) => _clock = clock;

    /// <inheritdoc />
    public async Task<Result<string>> ReadAsync(
        EasyRetailConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.Server) || string.IsNullOrWhiteSpace(connection.Database))
        {
            return Result.Failure<string>(Error.Validation(
                "Import.ConnectionIncomplete", "Enter the SQL Server and the database name."));
        }

        SqlConnectionStringBuilder builder = new()
        {
            DataSource = connection.Server.Trim(),
            InitialCatalog = connection.Database.Trim(),
            TrustServerCertificate = connection.TrustServerCertificate,
            Encrypt = SqlConnectionEncryptOption.Optional,
            ConnectTimeout = 15,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ApplicationName = "Aider ERP import",
        };

        if (string.IsNullOrWhiteSpace(connection.UserName))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = connection.UserName.Trim();
            builder.Password = connection.Password ?? string.Empty;
        }

        DateOnly asAt = connection.AsAt ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        try
        {
            await using SqlConnection sql = new(builder.ConnectionString);
            await sql.OpenAsync(cancellationToken);

            using MemoryStream buffer = new();

            await using (Utf8JsonWriter json = new(buffer))
            {
                json.WriteStartObject();
                json.WriteString("format", Format);
                json.WriteNumber("version", Version);
                json.WriteString("database", connection.Database.Trim());
                json.WriteString("asAt", asAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                json.WriteString(
                    "exportedAt",
                    _clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

                foreach ((string name, bool single, string query) in EasyRetailQueries.Sections)
                {
                    await WriteSectionAsync(json, sql, name, single, query, asAt, cancellationToken);
                }

                json.WriteEndObject();
            }

            return Result.Success(Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (SqlException exception)
        {
            return Result.Failure<string>(Error.BusinessRule(
                "Import.SourceUnreadable",
                $"The EasyRetail database could not be read: {exception.Message}"));
        }
        catch (PlatformNotSupportedException)
        {
            return Result.Failure<string>(Error.BusinessRule(
                "Import.WindowsSignInUnavailable",
                "This server cannot sign in to SQL Server with Windows authentication. "
                + "Enter a SQL Server login and password, or export with the script instead."));
        }
    }

    /// <inheritdoc />
    public string ExportScript()
    {
        StringBuilder script = new();

        script.Append(ScriptHeader);

        foreach ((string name, bool single, string query) in EasyRetailQueries.Sections)
        {
            script.Append(CultureInfo.InvariantCulture, $"Write-Host 'Reading {name}...'\n");
            script.Append(CultureInfo.InvariantCulture, $"$section = Read-Section @'\n{query}\n'@\n");
            script.Append(single
                ? $"$export['{name}'] = if ($section.Length -gt 0) {{ $section[0] }} else {{ $null }}\n"
                : $"$export['{name}'] = $section\n");
        }

        script.Append(ScriptFooter);

        // Windows line endings throughout, for Notepad and for Windows PowerShell 5.1.
        return script.ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static async Task WriteSectionAsync(
        Utf8JsonWriter json,
        SqlConnection sql,
        string name,
        bool single,
        string query,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = sql.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = 600;
        command.Parameters.Add("@AsAt", SqlDbType.Date).Value = asAt.ToDateTime(TimeOnly.MinValue);

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        json.WritePropertyName(name);

        if (!single)
        {
            json.WriteStartArray();
        }

        bool written = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (single && written)
            {
                break;
            }

            json.WriteStartObject();

            for (int column = 0; column < reader.FieldCount; column++)
            {
                json.WritePropertyName(reader.GetName(column));
                WriteValue(json, reader.GetValue(column));
            }

            json.WriteEndObject();
            written = true;
        }

        if (single && !written)
        {
            json.WriteNullValue();
        }

        if (!single)
        {
            json.WriteEndArray();
        }
    }

    private static void WriteValue(Utf8JsonWriter json, object value)
    {
        switch (value)
        {
            case DBNull:
                json.WriteNullValue();
                break;
            case bool flag:
                json.WriteBooleanValue(flag);
                break;
            case int number:
                json.WriteNumberValue(number);
                break;
            case long number:
                json.WriteNumberValue(number);
                break;
            case short number:
                json.WriteNumberValue(number);
                break;
            case byte number:
                json.WriteNumberValue(number);
                break;
            case decimal number:
                json.WriteNumberValue(number);
                break;
            case double number:
                json.WriteNumberValue(number);
                break;
            default:
                json.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
