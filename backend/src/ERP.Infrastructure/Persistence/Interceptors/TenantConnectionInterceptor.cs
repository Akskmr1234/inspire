using System.Data.Common;
using System.Globalization;
using ERP.Application.Abstractions.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Sets the PostgreSQL session variable that row-level-security policies read,
/// every time a connection is opened.
/// </summary>
/// <remarks>
/// <para>
/// It must happen on every open, not once at startup. Npgsql pools physical
/// connections, so the connection handed to this request may be the same one that
/// served a different tenant moments ago, still carrying that tenant's value. Any
/// gap between opening the connection and setting the variable is a window in
/// which a query could run under the wrong tenant.
/// </para>
/// <para>
/// When no tenant is resolved the variable is set to an empty string, which the
/// policy's <c>NULLIF(...)::uuid</c> turns into <c>NULL</c>, and a comparison with
/// <c>NULL</c> matches nothing. Unresolved therefore means "see nothing" rather
/// than "see everything".
/// </para>
/// </remarks>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initialises a new instance of the <see cref="TenantConnectionInterceptor"/> class.
    /// </summary>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public TenantConnectionInterceptor(ITenantContext tenantContext) =>
        _tenantContext = tenantContext;

    /// <inheritdoc />
    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);

        ApplyTenantSetting(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await ApplyTenantSettingAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void ApplyTenantSetting(DbConnection connection)
    {
        using DbCommand command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    private async Task ApplyTenantSettingAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        string tenant = _tenantContext.IsResolved
            ? _tenantContext.TenantId.Value.ToString()
            : string.Empty;

        DbCommand command = connection.CreateCommand();

        // set_config is used in preference to SET because it accepts the value as
        // a bound parameter. Interpolating a tenant identifier straight into DDL
        // would be an injection point, however well-formed the value looks today.
        command.CommandText = string.Create(
            CultureInfo.InvariantCulture,
            $"SELECT set_config('{RowLevelSecurity.TenantSetting}', @tenant, false)");

        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@tenant";
        parameter.Value = tenant;
        command.Parameters.Add(parameter);

        return command;
    }
}
