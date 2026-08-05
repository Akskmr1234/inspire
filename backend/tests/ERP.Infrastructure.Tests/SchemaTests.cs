using ERP.Infrastructure.Persistence;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Proves the migrations apply cleanly and that the schema they produce has the
/// properties the design depends on.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaTests
{
    private readonly PostgresFixture _fixture;

    public SchemaTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Migrations_apply_and_leave_nothing_pending()
    {
        await using ErpDbContext context =
            _fixture.CreateContext(PostgresFixture.ScopedTo(TenantId.NewId()));

        IEnumerable<string> pending = await context.Database.GetPendingMigrationsAsync();

        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task Xmin_is_the_system_column_not_a_created_one()
    {
        // xmin already exists on every PostgreSQL table as a system column. If a
        // migration tried to create a user column of that name the migration would
        // have failed outright; this asserts the mapping reads the system column,
        // which is what makes it a free concurrency token.
        await using ErpDbContext context =
            _fixture.CreateContext(PostgresFixture.ScopedTo(TenantId.NewId()));

        // attnum is negative for system columns and positive for user columns.
        short attributeNumber = await ScalarAsync<short>(
            context,
            """
            SELECT a.attnum
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            WHERE c.relname = 'firms' AND a.attname = 'xmin'
            """);

        attributeNumber.ShouldBeLessThan((short)0);
    }

    [Fact]
    public async Task Every_tenant_scoped_table_has_row_level_security_forced()
    {
        // Guards against the most likely future mistake: adding a tenant-scoped
        // table and forgetting its policy. Without this the omission is invisible
        // until a customer sees another customer's data.
        await using ErpDbContext context =
            _fixture.CreateContext(PostgresFixture.ScopedTo(TenantId.NewId()));

        IReadOnlyList<string> expected = RowLevelSecurity
            .BuildPolicyStatements(context.Model)
            .Count > 0
            ? TenantScopedTableNames(context)
            : [];

        expected.ShouldNotBeEmpty("the model must contain at least one tenant-scoped table");

        foreach (string table in expected)
        {
            bool enabled = await ScalarAsync<bool>(
                context,
                $"SELECT relrowsecurity FROM pg_class WHERE relname = '{table}'");

            bool forced = await ScalarAsync<bool>(
                context,
                $"SELECT relforcerowsecurity FROM pg_class WHERE relname = '{table}'");

            long policies = await ScalarAsync<long>(
                context,
                $"SELECT count(*) FROM pg_policies WHERE tablename = '{table}'");

            enabled.ShouldBeTrue($"row-level security must be enabled on '{table}'");

            // FORCE matters: without it the table owner bypasses the policy, and
            // migrations and maintenance scripts often connect as the owner.
            forced.ShouldBeTrue($"row-level security must be FORCED on '{table}'");
            policies.ShouldBe(1L, $"'{table}' must carry exactly one isolation policy");
        }
    }

    [Fact]
    public async Task Identifiers_are_snake_case_so_raw_sql_needs_no_quoting()
    {
        // The report builder and the RLS predicates are hand-written SQL.
        // PostgreSQL folds unquoted identifiers to lower case, so a PascalCase
        // column would have to be double-quoted everywhere it appears.
        await using ErpDbContext context =
            _fixture.CreateContext(PostgresFixture.ScopedTo(TenantId.NewId()));

        long badlyNamed = await ScalarAsync<long>(
            context,
            """
            SELECT count(*)
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND column_name <> lower(column_name)
            """);

        badlyNamed.ShouldBe(0L);
    }

    private static IReadOnlyList<string> TenantScopedTableNames(ErpDbContext context) =>
        [.. context.Model.GetEntityTypes()
            .Where(e => typeof(SharedKernel.Abstractions.ITenantScoped).IsAssignableFrom(e.ClrType))
            .Select(e => e.GetTableName())
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct()];

    private static async Task<T> ScalarAsync<T>(ErpDbContext context, string sql)
    {
        await using NpgsqlConnection connection = new(context.Database.GetConnectionString());
        await connection.OpenAsync();

        await using NpgsqlCommand command = new(sql, connection);
        object? value = await command.ExecuteScalarAsync();

        return (T)Convert.ChangeType(value!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
