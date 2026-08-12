using ERP.Application.Abstractions.Security;
using ERP.Domain.Accounting;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Seeding;
using ERP.Infrastructure.Tenancy;
using ERP.Infrastructure.Time;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Proves that a firm can post the taxes its own jurisdiction charges, on its first
/// day, without anybody visiting a settings screen.
/// </summary>
/// <remarks>
/// <para>
/// The map refuses a document charging a head it has no account for, which is the
/// behaviour that makes a return reconcile. That refusal is only tolerable because
/// seeding fills the heads a regime actually uses - so a defect here does not show
/// up as a wrong figure, it shows up as a firm unable to raise its first invoice.
/// </para>
/// <para>
/// The failure this is really guarding against is silent: seeding looks a ledger up
/// by the code the chart template seeds, and a code that does not match leaves the
/// head simply unassigned. Nothing throws, nothing logs, and the firm finds out at
/// the counter.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TaxAccountSeedingTests
{
    private readonly PostgresFixture _fixture;

    public TaxAccountSeedingTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_VAT_firm_is_given_both_directions_of_the_one_head_it_charges()
    {
        (TenantId tenant, FirmId firm) = await SeedAsync(TaxRegime.GccVat, "vat-seed", "VATCO");

        IReadOnlyDictionary<(TaxComponentType Component, TaxDirection Direction), string> accounts =
            await ReadAsync(tenant, firm);

        // Both halves, kept apart: what the firm owes the state and what it may
        // recover from it. One account for the pair would net them silently and
        // leave nothing to file.
        accounts[(TaxComponentType.Vat, TaxDirection.Output)].ShouldBe("VAT-OUTPUT");
        accounts[(TaxComponentType.Vat, TaxDirection.Input)].ShouldBe("VAT-INPUT");
    }

    [Fact]
    public async Task A_VAT_firm_is_offered_no_account_for_a_tax_it_does_not_pay()
    {
        // Completeness is not a property of this map, unlike the stock one. Asking a
        // Qatar firm where its CGST posts would be asking about a tax that does not
        // exist where it trades.
        (TenantId tenant, FirmId firm) = await SeedAsync(TaxRegime.GccVat, "vat-only", "VATONLY");

        IReadOnlyDictionary<(TaxComponentType Component, TaxDirection Direction), string> accounts =
            await ReadAsync(tenant, firm);

        accounts.Count.ShouldBe(2);
        accounts.Keys.ShouldAllBe(head => head.Component == TaxComponentType.Vat);
    }

    [Fact]
    public async Task A_GST_firm_is_given_all_four_heads_in_both_directions()
    {
        (TenantId tenant, FirmId firm) = await SeedAsync(TaxRegime.IndiaGst, "gst-seed", "GSTCO");

        IReadOnlyDictionary<(TaxComponentType Component, TaxDirection Direction), string> accounts =
            await ReadAsync(tenant, firm);

        accounts.Count.ShouldBe(8);

        accounts[(TaxComponentType.Cgst, TaxDirection.Output)].ShouldBe("CGST-OUTPUT");
        accounts[(TaxComponentType.Sgst, TaxDirection.Output)].ShouldBe("SGST-OUTPUT");
        accounts[(TaxComponentType.Igst, TaxDirection.Output)].ShouldBe("IGST-OUTPUT");
        accounts[(TaxComponentType.Cess, TaxDirection.Output)].ShouldBe("CESS-OUTPUT");
        accounts[(TaxComponentType.Cgst, TaxDirection.Input)].ShouldBe("CGST-INPUT");
        accounts[(TaxComponentType.Sgst, TaxDirection.Input)].ShouldBe("SGST-INPUT");
        accounts[(TaxComponentType.Igst, TaxDirection.Input)].ShouldBe("IGST-INPUT");
        accounts[(TaxComponentType.Cess, TaxDirection.Input)].ShouldBe("CESS-INPUT");
    }

    [Fact]
    public async Task Reseeding_leaves_a_head_an_accountant_has_repointed_alone()
    {
        // Seeding fills gaps; it does not correct people. A firm that posts its
        // output VAT to its own liability account must not find the next deployment
        // has quietly moved it back.
        (TenantId tenant, FirmId firm) = await SeedAsync(TaxRegime.GccVat, "vat-keep", "VATKEEP");

        await using (ErpDbContext repointing =
            _fixture.CreateContext(PostgresFixture.ScopedTo(tenant)))
        {
            TaxAccountMap map = await LoadMapAsync(repointing, firm);

            Ledger elsewhere = await repointing.Ledgers
                .FirstAsync(ledger => ledger.FirmId == firm && ledger.Code == "CAPITAL");

            map.Assign(TaxComponentType.Vat, TaxDirection.Output, elsewhere)
                .IsSuccess.ShouldBeTrue();

            await repointing.SaveChangesAsync();
        }

        await SeedAsync(TaxRegime.GccVat, "vat-keep", "VATKEEP");

        IReadOnlyDictionary<(TaxComponentType Component, TaxDirection Direction), string> accounts =
            await ReadAsync(tenant, firm);

        accounts[(TaxComponentType.Vat, TaxDirection.Output)].ShouldBe("CAPITAL");

        // And the head nobody touched is still where seeding put it.
        accounts[(TaxComponentType.Vat, TaxDirection.Input)].ShouldBe("VAT-INPUT");
    }

    [Fact]
    public async Task A_firm_whose_map_predates_a_head_gains_only_what_it_was_missing()
    {
        // The case that made the inventory map's seeding gap-filling rather than
        // all-or-nothing: a firm seeded before a head existed would otherwise be
        // unable to post that head for ever, because a map already present was
        // taken as a map already finished.
        (TenantId tenant, FirmId firm) = await SeedAsync(TaxRegime.IndiaGst, "gst-gap", "GSTGAP");

        await using (ErpDbContext removing =
            _fixture.CreateContext(PostgresFixture.ScopedTo(tenant)))
        {
            TaxAccountMap map = await LoadMapAsync(removing, firm);

            TaxAccountAssignment cess = map.Accounts.Single(entry =>
                entry.Component == TaxComponentType.Cess
                && entry.Direction == TaxDirection.Output);

            removing.TaxAccountAssignments.Remove(cess);
            await removing.SaveChangesAsync();
        }

        await SeedAsync(TaxRegime.IndiaGst, "gst-gap", "GSTGAP");

        IReadOnlyDictionary<(TaxComponentType Component, TaxDirection Direction), string> accounts =
            await ReadAsync(tenant, firm);

        accounts.Count.ShouldBe(8);
        accounts[(TaxComponentType.Cess, TaxDirection.Output)].ShouldBe("CESS-OUTPUT");
    }

    private static Task<TaxAccountMap> LoadMapAsync(ErpDbContext context, FirmId firmId) =>
        context.TaxAccountMaps
            .Include(map => map.Accounts)
            .FirstAsync(map => map.FirmId == firmId);

    /// <summary>Runs the real seeder, as a deployment would, and returns what it made.</summary>
    private async Task<(TenantId Tenant, FirmId Firm)> SeedAsync(
        TaxRegime regime,
        string tenantCode,
        string firmCode)
    {
        AmbientTenantContext tenantContext = new();

        IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("hashed");

        SeedOptions options = new()
        {
            TenantCode = tenantCode,
            TenantName = tenantCode,
            FirmCode = firmCode,
            FirmName = firmCode,
            BaseCurrency = regime == TaxRegime.IndiaGst ? "INR" : "QAR",
            TaxRegime = regime,
            TimeZoneId = regime == TaxRegime.IndiaGst ? "Asia/Kolkata" : "Asia/Qatar",
            AdministratorUserName = $"{tenantCode}-admin",
            AdministratorEmail = $"{tenantCode}@inspire.local",
            AdministratorPassword = "Seed-Password-1",
        };

        await using ErpDbContext context = _fixture.CreateContext(tenantContext);

        DatabaseSeeder seeder = new(
            context,
            tenantContext,
            hasher,
            new SystemClock(),
            Options.Create(options),
            NullLogger<DatabaseSeeder>.Instance);

        Result<SeedSummary> summary = await seeder.SeedAsync();

        summary.IsSuccess.ShouldBeTrue(summary.IsFailure ? summary.Error.Description : null);

        Tenant tenant = await context.Tenants.FirstAsync(t => t.Code == tenantCode);

        using IDisposable scope = tenantContext.BeginScope(tenant.Id);

        Firm firm = await context.Firms.FirstAsync(f => f.Code == firmCode);

        return (tenant.Id, firm.Id);
    }

    /// <summary>Reads a firm's map back as heads to the ledger codes they post to.</summary>
    private async Task<IReadOnlyDictionary<(TaxComponentType Component, TaxDirection Direction), string>> ReadAsync(
        TenantId tenantId,
        FirmId firmId)
    {
        await using ErpDbContext context =
            _fixture.CreateContext(PostgresFixture.ScopedTo(tenantId));

        TaxAccountMap map = await LoadMapAsync(context, firmId);

        Dictionary<LedgerId, string> codes = await context.Ledgers
            .Where(ledger => ledger.FirmId == firmId)
            .ToDictionaryAsync(ledger => ledger.Id, ledger => ledger.Code);

        return map.Accounts.ToDictionary(
            entry => (entry.Component, entry.Direction),
            entry => codes[entry.LedgerId]);
    }
}
