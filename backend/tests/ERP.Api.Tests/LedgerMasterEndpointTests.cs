using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Inventory.Masters;
using ERP.Domain.Accounting;

namespace ERP.Api.Tests;

/// <summary>The masters a firm keeps for itself: its own ledgers and groups, and names it corrects.</summary>
/// <remarks>
/// Before these, the chart of accounts could only be read. A firm could not open a ledger
/// for its rent, a second bank account or an employee advance, and every voucher had to be
/// posted to one of the seeded accounts.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class LedgerMasterEndpointTests
{
    private const string Ledgers = "/api/v1/accounting/ledgers";
    private const string Groups = "/api/v1/accounting/account-groups";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="LedgerMasterEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public LedgerMasterEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_firm_opens_corrects_and_withdraws_its_own_ledgers()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        List<AccountGroupSummary> groups = (await client.GetFromJsonAsync<List<AccountGroupSummary>>(Groups))!;
        AccountGroupSummary expenses = groups.Single(group => group.Code == "5900");
        AccountGroupSummary cashAndBank = groups.Single(group => group.Code == "1110");

        // No code given: one is made from the name, and a second whose name makes the same code gets -2.
        LedgerDetail rent = await CreateAsync(client, new
        {
            Name = $"Office rent {tag}",
            AccountGroupId = expenses.AccountGroupId,
            Kind = LedgerKind.General,
            OpeningBalance = 1500m,
            OpeningSide = EntrySide.Debit,
        });

        // Left blank, the code is the next in the firm's sequence, 10000 at the least.
        long.Parse(rent.Code, System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(10000);
        rent.GroupName.ShouldBe(expenses.Name);
        rent.Nature.ShouldBe(AccountNature.Expense);
        rent.OpeningBalance.ShouldBe(1500m);
        rent.IsEditable.ShouldBeTrue();

        // The same name again is refused; a name that only makes the same code is not.
        (await client.PostAsJsonAsync(Ledgers, new
        {
            Name = $"OFFICE RENT {tag}",
            AccountGroupId = expenses.AccountGroupId,
            Kind = LedgerKind.General,
        })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        LedgerDetail twin = await CreateAsync(client, new
        {
            Name = $"Office-rent {tag}",
            AccountGroupId = expenses.AccountGroupId,
            Kind = LedgerKind.General,
        });

        // The next in the sequence: a code is never made from the name any more.
        long.Parse(twin.Code, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(long.Parse(rent.Code, System.Globalization.CultureInfo.InvariantCulture) + 1);

        // A code somebody holds is refused by name.
        (await client.PostAsJsonAsync(Ledgers, new
        {
            Code = rent.Code,
            Name = "Clash",
            AccountGroupId = expenses.AccountGroupId,
            Kind = LedgerKind.General,
        })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Customers are opened on their own screen, with their terms.
        (await client.PostAsJsonAsync(Ledgers, new
        {
            Name = "Not here",
            AccountGroupId = expenses.AccountGroupId,
            Kind = LedgerKind.Customer,
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Corrected into a bank account under Cash and Bank; the code stays.
        HttpResponseMessage moved = await client.PutAsJsonAsync($"{Ledgers}/{twin.LedgerId}", new
        {
            Name = $"Commercial Bank {tag}",
            AccountGroupId = cashAndBank.AccountGroupId,
            Kind = LedgerKind.Bank,
            Address = "Branch: West Bay, A/C 0012-3456",
        });

        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        LedgerDetail bank = (await moved.Content.ReadFromJsonAsync<LedgerDetail>())!;
        bank.Code.ShouldBe(twin.Code);
        bank.Kind.ShouldBe(LedgerKind.Bank);
        bank.Nature.ShouldBe(AccountNature.Asset);
        bank.Address.ShouldBe("Branch: West Bay, A/C 0012-3456");

        List<LedgerSummary> listed = (await client.GetFromJsonAsync<List<LedgerSummary>>(Ledgers))!;
        listed.ShouldContain(ledger => ledger.LedgerId == bank.LedgerId && ledger.GroupCode == "1110");

        // A tax or charge ledger is kept on its own setup, not here.
        LedgerSummary charge = listed.First(ledger => !Ledger.IsPlain(ledger.Kind));
        (await client.PutAsJsonAsync($"{Ledgers}/{charge.LedgerId}", new
        {
            charge.Name,
            AccountGroupId = expenses.AccountGroupId,
            Kind = LedgerKind.General,
        })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Withdrawn, it leaves the pickers but stays on record.
        (await client.PostAsJsonAsync($"{Ledgers}/{rent.LedgerId}/active", new { IsActive = false }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<LedgerSummary>>(Ledgers))!
            .ShouldNotContain(ledger => ledger.LedgerId == rent.LedgerId);
        (await client.GetFromJsonAsync<LedgerDetail>($"{Ledgers}/{rent.LedgerId}"))!
            .IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task A_group_takes_its_parents_nature_and_can_be_renamed()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        List<AccountGroupSummary> groups = (await client.GetFromJsonAsync<List<AccountGroupSummary>>(Groups))!;
        AccountGroupSummary liabilities = groups.Single(group => group.Code == "2000");

        HttpResponseMessage created = await client.PostAsJsonAsync(Groups, new
        {
            Name = $"Loans {tag}",
            ParentId = liabilities.AccountGroupId,
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        AccountGroupSummary loans = (await created.Content.ReadFromJsonAsync<AccountGroupSummary>())!;
        long.Parse(loans.Code, System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(10000);
        loans.Nature.ShouldBe(AccountNature.Liability);
        loans.ParentId.ShouldBe(liabilities.AccountGroupId);

        HttpResponseMessage renamed = await client.PutAsJsonAsync($"{Groups}/{loans.AccountGroupId}", new
        {
            Name = $"Secured loans {tag}",
            NameArabic = "قروض مضمونة",
        });

        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await renamed.Content.ReadFromJsonAsync<AccountGroupSummary>())!.Name.ShouldBe($"Secured loans {tag}");

        // A ledger can be opened under it straight away.
        LedgerDetail loan = await CreateAsync(client, new
        {
            Name = $"Vehicle loan {tag}",
            AccountGroupId = loans.AccountGroupId,
            Kind = LedgerKind.General,
            OpeningBalance = 40000m,
            OpeningSide = EntrySide.Credit,
        });

        loan.Nature.ShouldBe(AccountNature.Liability);
        loan.OpeningSide.ShouldBe(EntrySide.Credit);
    }

    [Fact]
    public async Task Categories_brands_and_warehouses_can_be_corrected()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        HttpResponseMessage category = await client.PostAsJsonAsync(
            "/api/v1/inventory/categories", new { Code = $"C{tag}", Name = $"Phnes {tag}" });
        category.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid categoryId = await category.Content.ReadFromJsonAsync<Guid>();

        (await client.PutAsJsonAsync($"/api/v1/inventory/Category/{categoryId}", new
        {
            Name = "Phones",
            NameArabic = "هواتف",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        CategorySummary corrected = (await client.GetFromJsonAsync<List<CategorySummary>>(
            "/api/v1/inventory/categories"))!.Single(c => c.Id == categoryId);
        corrected.Name.ShouldBe("Phones");
        corrected.NameArabic.ShouldBe("هواتف");
        corrected.Code.ShouldBe($"C{tag}");

        HttpResponseMessage warehouse = await client.PostAsJsonAsync(
            "/api/v1/inventory/warehouses", new { Code = $"W{tag}", Name = $"Back store {tag}" });
        warehouse.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid warehouseId = await warehouse.Content.ReadFromJsonAsync<Guid>();

        (await client.PutAsJsonAsync($"/api/v1/inventory/Warehouse/{warehouseId}", new
        {
            Name = "Back store (Industrial Area)",
            Address = "Street 24, Industrial Area",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        WarehouseSummary moved = (await client.GetFromJsonAsync<List<WarehouseSummary>>(
            "/api/v1/inventory/warehouses"))!.Single(w => w.Id == warehouseId);
        moved.Name.ShouldBe("Back store (Industrial Area)");
        moved.Address.ShouldBe("Street 24, Industrial Area");

        // A blank name is refused; a unit is renamed at its own address.
        (await client.PutAsJsonAsync($"/api/v1/inventory/Brand/{Guid.NewGuid()}", new { Name = "X" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/v1/inventory/Category/{categoryId}", new { Name = " " }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<LedgerDetail> CreateAsync(HttpClient client, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(Ledgers, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LedgerDetail>())!;
    }
}
