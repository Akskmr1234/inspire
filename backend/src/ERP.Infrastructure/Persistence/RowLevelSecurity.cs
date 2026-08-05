using System.Globalization;
using System.Text;
using ERP.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Generates the PostgreSQL row-level-security statements that form the second
/// tenant-isolation layer.
/// </summary>
/// <remarks>
/// <para>
/// The EF Core global query filter already restricts every LINQ query to the
/// current tenant. This exists because that filter has gaps that are invisible at
/// the call site:
/// </para>
/// <list type="bullet">
/// <item><description>raw SQL from the report builder and the dynamic dashboard widgets;</description></item>
/// <item><description><c>ExecuteUpdate</c> and <c>ExecuteDelete</c>, which bypass the change tracker;</description></item>
/// <item><description>a deliberate <c>IgnoreQueryFilters()</c> added for one legitimate reason and later copied;</description></item>
/// <item><description>a Hangfire job or migration running outside a request;</description></item>
/// <item><description>anyone connecting with psql.</description></item>
/// </list>
/// <para>
/// Under RLS the database refuses to return another tenant's rows regardless of
/// the query that asks for them. Application code cannot opt out - only a
/// superuser or the table owner can, and the application does not connect as
/// either.
/// </para>
/// </remarks>
public static class RowLevelSecurity
{
    /// <summary>
    /// The session variable carrying the current tenant.
    /// </summary>
    /// <remarks>
    /// A custom GUC rather than a PostgreSQL role per tenant: roles do not scale
    /// to thousands of tenants and cannot be switched cheaply on a pooled
    /// connection. The variable is set at the start of every connection's use and
    /// read by the policy predicate.
    /// </remarks>
    public const string TenantSetting = "app.current_tenant";

    /// <summary>
    /// Builds the SQL enabling row-level security on every tenant-scoped table.
    /// </summary>
    /// <param name="model">The EF Core model.</param>
    /// <returns>The DDL statements, in application order.</returns>
    public static IReadOnlyList<string> BuildPolicyStatements(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<string> statements = [];

        foreach (IEntityType entityType in model.GetEntityTypes())
        {
            if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            if (entityType.GetTableName() is not { } table)
            {
                continue;
            }

            string schema = entityType.GetSchema() ?? "public";
            string tenantColumn = ResolveTenantColumn(entityType);
            string qualified = $"\"{schema}\".\"{table}\"";
            string policy = $"{table}_tenant_isolation";

            StringBuilder sql = new();

            sql.AppendLine(CultureInfo.InvariantCulture, $"ALTER TABLE {qualified} ENABLE ROW LEVEL SECURITY;");

            // FORCE makes the policy apply to the table's owner too. Without it a
            // migration or maintenance script connecting as the owner silently
            // sees everything, which is exactly the situation this layer exists
            // to cover.
            sql.AppendLine(CultureInfo.InvariantCulture, $"ALTER TABLE {qualified} FORCE ROW LEVEL SECURITY;");

            sql.AppendLine(CultureInfo.InvariantCulture, $"DROP POLICY IF EXISTS \"{policy}\" ON {qualified};");

            // current_setting(..., true) returns NULL rather than raising when the
            // variable is unset, and NULL fails the comparison - so an unset
            // tenant yields no rows. Failing closed is the only acceptable
            // default here.
            sql.AppendLine(CultureInfo.InvariantCulture, $"""
                CREATE POLICY "{policy}" ON {qualified}
                    USING ("{tenantColumn}" = NULLIF(current_setting('{TenantSetting}', true), '')::uuid)
                    WITH CHECK ("{tenantColumn}" = NULLIF(current_setting('{TenantSetting}', true), '')::uuid);
                """);

            statements.Add(sql.ToString());
        }

        return statements;
    }

    /// <summary>
    /// Builds the SQL removing the policies, for a migration rollback.
    /// </summary>
    /// <param name="model">The EF Core model.</param>
    /// <returns>The DDL statements.</returns>
    public static IReadOnlyList<string> BuildDropStatements(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<string> statements = [];

        foreach (IEntityType entityType in model.GetEntityTypes())
        {
            if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType)
                || entityType.GetTableName() is not { } table)
            {
                continue;
            }

            string schema = entityType.GetSchema() ?? "public";
            string qualified = $"\"{schema}\".\"{table}\"";

            statements.Add($"""
                DROP POLICY IF EXISTS "{table}_tenant_isolation" ON {qualified};
                ALTER TABLE {qualified} NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE {qualified} DISABLE ROW LEVEL SECURITY;
                """);
        }

        return statements;
    }

    /// <summary>Finds the database column holding the tenant.</summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The column name.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the property cannot be located, which would otherwise produce a
    /// policy referencing a column that does not exist and fail at migration time
    /// with a far less obvious message.
    /// </exception>
    private static string ResolveTenantColumn(IEntityType entityType)
    {
        IProperty property = entityType.FindProperty(nameof(ITenantScoped.TenantId))
            ?? throw new InvalidOperationException(
                $"{entityType.ClrType.Name} implements {nameof(ITenantScoped)} but no " +
                $"{nameof(ITenantScoped.TenantId)} property is mapped.");

        StoreObjectIdentifier store = StoreObjectIdentifier.Table(
            entityType.GetTableName()!, entityType.GetSchema());

        return property.GetColumnName(store) ?? "tenant_id";
    }
}
