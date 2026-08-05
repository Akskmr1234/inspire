using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Proves that one tenant cannot read another's data.
/// </summary>
/// <remarks>
/// The most important tests in the suite. Every other defect in this system costs
/// somebody time; this one costs a customer their confidentiality. Both isolation
/// layers are exercised separately, because the whole reason for having two is
/// that either might one day be circumvented on its own.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TenantIsolationTests
{
    private readonly PostgresFixture _fixture;

    public TenantIsolationTests(PostgresFixture fixture) => _fixture = fixture;

    // ------------------------------------------------ layer 1: EF query filters

    [Fact]
    public async Task A_tenant_sees_only_its_own_firms()
    {
        TenantId alpha = TenantId.NewId();
        TenantId beta = TenantId.NewId();

        await CreateFirmAsync(alpha, "ALPHA");
        await CreateFirmAsync(beta, "BETA");

        await using ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(alpha));

        List<Firm> visible = await context.Firms.ToListAsync();

        visible.ShouldHaveSingleItem().Code.ShouldBe("ALPHA");
    }

    [Fact]
    public async Task Fetching_another_tenants_firm_by_its_exact_id_returns_nothing()
    {
        // Knowing the primary key must not be enough. This is the shape a
        // parameter-tampering attack takes: a valid identifier from another
        // tenant pasted into a request.
        TenantId alpha = TenantId.NewId();
        TenantId beta = TenantId.NewId();

        FirmId betaFirmId = await CreateFirmAsync(beta, "BETA");

        await using ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(alpha));

        Firm? stolen = await context.Firms.FirstOrDefaultAsync(f => f.Id == betaFirmId);

        stolen.ShouldBeNull();
    }

    [Fact]
    public async Task An_unresolved_tenant_sees_nothing_rather_than_everything()
    {
        // Fail closed. A query that runs without a tenant - a misconfigured job,
        // a middleware ordering mistake - must return an empty set, never the
        // whole table.
        await CreateFirmAsync(TenantId.NewId(), "SOMEONE");

        await using ErpDbContext context = _fixture.CreateContext(new AmbientTenantContext());

        (await context.Firms.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Child_collections_are_isolated_too()
    {
        TenantId alpha = TenantId.NewId();
        TenantId beta = TenantId.NewId();

        await CreateFirmAsync(alpha, "ALPHA", withBranch: true);
        await CreateFirmAsync(beta, "BETA", withBranch: true);

        await using ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(alpha));

        List<Branch> branches = await context.Branches.ToListAsync();

        branches.ShouldHaveSingleItem().TenantId.ShouldBe(alpha);
    }

    [Fact]
    public async Task Soft_deleted_rows_are_hidden_but_retained()
    {
        TenantId tenant = TenantId.NewId();
        FirmId firmId = await CreateFirmAsync(tenant, "GONE");

        await using (ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(tenant)))
        {
            Firm firm = await context.Firms.SingleAsync();
            context.Firms.Remove(firm);
            await context.SaveChangesAsync();
        }

        await using (ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(tenant)))
        {
            // Hidden from ordinary queries...
            (await context.Firms.CountAsync()).ShouldBe(0);

            // ...but still on disk, because an audit must be able to read what a
            // deleted record contained.
            Firm retained = await context.Firms
                .IgnoreQueryFilters()
                .SingleAsync(f => f.Id == firmId);

            retained.IsDeleted.ShouldBeTrue();
            retained.DeletedAtUtc.ShouldNotBeNull();
            retained.Code.ShouldBe("GONE");
        }
    }

    // ------------------------------------------------ layer 2: PostgreSQL RLS

    [Fact]
    public async Task Row_level_security_blocks_raw_sql_that_bypasses_ef()
    {
        // The case the query filter cannot cover: hand-written SQL, as the report
        // builder and dashboard widgets will emit. EF's filter is not involved
        // here at all, so anything returned comes from the database's own policy.
        TenantId alpha = TenantId.NewId();
        TenantId beta = TenantId.NewId();

        await CreateFirmAsync(alpha, "ALPHA");
        await CreateFirmAsync(beta, "BETA");

        long visibleToAlpha = await CountFirmsViaRawSqlAsync(alpha);
        long visibleToBeta = await CountFirmsViaRawSqlAsync(beta);
        long visibleWithNoTenant = await CountFirmsViaRawSqlAsync(tenantId: null);

        visibleToAlpha.ShouldBe(1L);
        visibleToBeta.ShouldBe(1L);
        visibleWithNoTenant.ShouldBe(0L);
    }

    [Fact]
    public async Task Row_level_security_survives_ignore_query_filters()
    {
        // IgnoreQueryFilters disables layer one deliberately. Layer two must still
        // hold, otherwise a single well-intentioned call somewhere in the codebase
        // silently reopens the whole database.
        TenantId alpha = TenantId.NewId();
        TenantId beta = TenantId.NewId();

        await CreateFirmAsync(alpha, "ALPHA");
        await CreateFirmAsync(beta, "BETA");

        await using ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(alpha));

        List<Firm> everythingAlphaCanReach = await context.Firms
            .IgnoreQueryFilters()
            .ToListAsync();

        everythingAlphaCanReach.ShouldHaveSingleItem().Code.ShouldBe("ALPHA");
    }

    [Fact]
    public async Task Row_level_security_blocks_writing_into_another_tenant()
    {
        // The WITH CHECK half of the policy. Reading someone else's data is bad;
        // planting a row in their books would be worse.
        TenantId alpha = TenantId.NewId();
        TenantId beta = TenantId.NewId();

        await using NpgsqlConnection connection = new(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using (NpgsqlCommand setTenant = new(
            "SELECT set_config('app.current_tenant', @tenant, false)", connection))
        {
            setTenant.Parameters.AddWithValue("@tenant", alpha.Value.ToString());
            await setTenant.ExecuteNonQueryAsync();
        }

        await using NpgsqlCommand insert = new(
            """
            INSERT INTO public.firms
                (id, tenant_id, code, name, base_currency, tax_regime, time_zone_id,
                 is_active, created_at_utc, created_by, is_deleted)
            VALUES
                (@id, @tenant, 'SMUGGLED', 'Smuggled', 'QAR', 1, 'Asia/Qatar',
                 true, now(), @actor, false)
            """,
            connection);

        insert.Parameters.AddWithValue("@id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("@tenant", beta.Value);
        insert.Parameters.AddWithValue("@actor", UserId.System.Value);

        PostgresException failure =
            await Should.ThrowAsync<PostgresException>(insert.ExecuteNonQueryAsync());

        // 42501 is insufficient_privilege - the policy's WITH CHECK refusing it.
        failure.SqlState.ShouldBe("42501");
    }

    // ------------------------------------------------ write-path guards

    [Fact]
    public async Task The_tenant_is_stamped_automatically_on_insert()
    {
        TenantId tenant = TenantId.NewId();

        await using ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(tenant));

        // Note that nothing here passes a tenant; the interceptor supplies it.
        Firm firm = Firm.Create(
            tenant, "STAMPED", "Stamped", CurrencyCode.Qar,
            TaxRegime.GccVat, "Asia/Qatar").Value;

        context.Firms.Add(firm);
        await context.SaveChangesAsync();

        Firm saved = await context.Firms.SingleAsync(f => f.Code == "STAMPED");

        saved.TenantId.ShouldBe(tenant);
        saved.CreatedAtUtc.ShouldNotBe(default);
        saved.CreatedBy.ShouldBe(UserId.System);
    }

    [Fact]
    public async Task Saving_a_tenant_scoped_entity_without_a_tenant_fails_loudly()
    {
        await using ErpDbContext context = _fixture.CreateContext(new AmbientTenantContext());

        Firm firm = Firm.Create(
            default, "ORPHAN", "Orphan", CurrencyCode.Qar,
            TaxRegime.GccVat, "Asia/Qatar").Value;

        context.Firms.Add(firm);

        // A row saved with no tenant would be hidden from everyone by the query
        // filter - it would look like the save silently did nothing. Better to
        // refuse at the point of the mistake.
        InvalidOperationException failure =
            await Should.ThrowAsync<InvalidOperationException>(context.SaveChangesAsync());

        failure.Message.ShouldContain("no tenant has been resolved");
    }

    [Fact]
    public async Task A_row_cannot_be_moved_between_tenants()
    {
        TenantId alpha = TenantId.NewId();
        await CreateFirmAsync(alpha, "ALPHA");

        await using ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(alpha));

        Firm firm = await context.Firms.SingleAsync();

        // Reassigning the tenant would hand one customer's record to another.
        context.Entry(firm).Property(nameof(Firm.TenantId)).CurrentValue = TenantId.NewId();

        InvalidOperationException failure =
            await Should.ThrowAsync<InvalidOperationException>(context.SaveChangesAsync());

        failure.Message.ShouldContain("cannot be changed");
    }

    [Fact]
    public async Task Audit_stamps_record_the_acting_user_on_update()
    {
        TenantId tenant = TenantId.NewId();
        UserId actor = UserId.NewId();

        await CreateFirmAsync(tenant, "AUDITED");

        AmbientCurrentUser user = new();
        user.BeginScope(actor, "test.user");

        await using ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(tenant), user);

        Firm firm = await context.Firms.SingleAsync();
        firm.SetArabicName("شركة");
        await context.SaveChangesAsync();

        Firm updated = await context.Firms.SingleAsync();

        updated.ModifiedBy.ShouldBe(actor);
        updated.ModifiedAtUtc.ShouldNotBeNull();
        updated.NameArabic.ShouldBe("شركة");
    }

    // ------------------------------------------------ helpers

    private async Task<FirmId> CreateFirmAsync(
        TenantId tenantId,
        string code,
        bool withBranch = false)
    {
        await using ErpDbContext context = _fixture.CreateContext(
            PostgresFixture.ScopedTo(tenantId));

        Firm firm = Firm.Create(
            tenantId, code, $"{code} Limited", CurrencyCode.Qar,
            TaxRegime.GccVat, "Asia/Qatar").Value;

        if (withBranch)
        {
            firm.AddBranch("HO", "Head Office", isHeadOffice: true);
        }

        context.Firms.Add(firm);
        await context.SaveChangesAsync();

        return firm.Id;
    }

    /// <summary>
    /// Counts firms through a raw connection, so only the database's own policy
    /// governs what comes back.
    /// </summary>
    private async Task<long> CountFirmsViaRawSqlAsync(TenantId? tenantId)
    {
        await using NpgsqlConnection connection = new(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using (NpgsqlCommand setTenant = new(
            "SELECT set_config('app.current_tenant', @tenant, false)", connection))
        {
            setTenant.Parameters.AddWithValue(
                "@tenant", tenantId?.Value.ToString() ?? string.Empty);
            await setTenant.ExecuteNonQueryAsync();
        }

        await using NpgsqlCommand count = new("SELECT count(*) FROM public.firms", connection);

        return (long)(await count.ExecuteScalarAsync())!;
    }
}
