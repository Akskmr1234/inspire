using ERP.Application.Abstractions.Security;
using ERP.Domain.Platform;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Seeding;
using ERP.Infrastructure.Tenancy;
using ERP.Infrastructure.Time;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Proves that a release's new screens reach every firm's sidebar on the next start,
/// with seeding switched off and for firms seeding never created.
/// </summary>
/// <remarks>
/// The sidebar is built from stored menu rows. Before this, only the one firm seeding
/// created was given new entries, and only where seeding was enabled - so a deployment
/// with it off carried the new screens in its code and never showed them.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class MenuSyncTests
{
    private const string Quotations = "transactions.sales-quotations";

    private readonly PostgresFixture _fixture;

    public MenuSyncTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Every_firm_gains_the_entries_it_is_missing_with_seeding_off()
    {
        AmbientTenantContext tenantContext = new();
        TenantId tenant;
        FirmId seeded;
        FirmId other;

        await using (ErpDbContext context = _fixture.CreateContext(tenantContext))
        {
            Result<SeedSummary> summary = await Seeder(context, tenantContext, enabled: true).SeedAsync();
            summary.IsSuccess.ShouldBeTrue(summary.IsFailure ? summary.Error.Description : null);

            tenant = (await context.Tenants.FirstAsync(t => t.Code == "menu-sync")).Id;

            using IDisposable scope = tenantContext.BeginScope(tenant);

            seeded = (await context.Firms.FirstAsync(f => f.Code == "MENUSYNC")).Id;

            // A screen shipped after this firm's menu was made.
            MenuItem quotations = await context.MenuItems
                .FirstAsync(item => item.FirmId == seeded && item.Code == Quotations);
            context.MenuItems.Remove(quotations);

            // And a firm seeding never created, with no menu at all.
            Firm unseeded = Firm.Create(
                tenant, "MENUOTHER", "Other firm", CurrencyCode.Create("QAR").Value,
                TaxRegime.GccVat, "Asia/Qatar").Value;
            context.Firms.Add(unseeded);
            other = unseeded.Id;

            await context.SaveChangesAsync();
        }

        int added;

        await using (ErpDbContext context = _fixture.CreateContext(tenantContext))
        {
            added = await Seeder(context, tenantContext, enabled: false).SyncMenusAsync();
        }

        added.ShouldBeGreaterThan(1);

        await using ErpDbContext reading = _fixture.CreateContext(PostgresFixture.ScopedTo(tenant));

        List<string> first = await CodesAsync(reading, seeded);
        List<string> second = await CodesAsync(reading, other);

        first.ShouldContain(Quotations);
        second.ShouldBe(first, ignoreOrder: true);

        // Nothing is added twice.
        await using ErpDbContext again = _fixture.CreateContext(tenantContext);
        (await Seeder(again, tenantContext, enabled: false).SyncMenusAsync()).ShouldBe(0);
    }

    private static Task<List<string>> CodesAsync(ErpDbContext context, FirmId firm) =>
        context.MenuItems
            .Where(item => item.FirmId == firm)
            .Select(item => item.Code)
            .ToListAsync();

    private static DatabaseSeeder Seeder(ErpDbContext context, AmbientTenantContext tenantContext, bool enabled)
    {
        IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("hashed");

        SeedOptions options = new()
        {
            Enabled = enabled,
            TenantCode = "menu-sync",
            TenantName = "menu-sync",
            FirmCode = "MENUSYNC",
            FirmName = "MENUSYNC",
            BaseCurrency = "QAR",
            TaxRegime = TaxRegime.GccVat,
            TimeZoneId = "Asia/Qatar",
            AdministratorUserName = "menu-sync-admin",
            AdministratorEmail = "menu-sync@inspire.local",
            AdministratorPassword = "Seed-Password-1",
        };

        return new DatabaseSeeder(
            context,
            tenantContext,
            hasher,
            new SystemClock(),
            Options.Create(options),
            NullLogger<DatabaseSeeder>.Instance);
    }
}
