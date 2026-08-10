using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Inventory;

/// <summary>Tests for <see cref="InventoryAccountMap"/>: the answer to open question 8a.</summary>
/// <remarks>
/// The map decides where the other side of every stock movement lands. What these cover
/// is the part that protects somebody's books: that an account from another firm cannot
/// be chosen, that a withdrawn one cannot, and that asking for an account nobody has
/// chosen fails in a way that names what is missing rather than posting anyway.
/// </remarks>
public sealed class InventoryAccountMapTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();

    [Fact]
    public void A_new_map_has_chosen_nothing_and_is_not_complete()
    {
        InventoryAccountMap map = InventoryAccountMap.Create(Tenant, Firm);

        map.Accounts.ShouldBeEmpty();
        map.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void An_account_is_chosen_per_kind_of_movement()
    {
        InventoryAccountMap map = InventoryAccountMap.Create(Tenant, Firm);
        Ledger stock = LedgerIn(Firm, "1300", "Stock in Hand");
        Ledger consumption = LedgerIn(Firm, "5200", "Materials Consumed");

        map.Assign(StockAccount.Inventory, stock).IsSuccess.ShouldBeTrue();
        map.Assign(StockAccount.Consumption, consumption).IsSuccess.ShouldBeTrue();

        map.For(StockAccount.Inventory).Value.ShouldBe(stock.Id);
        map.For(StockAccount.Consumption).Value.ShouldBe(consumption.Id);
    }

    [Fact]
    public void Choosing_again_repoints_rather_than_adding_a_second_answer()
    {
        // Two rows for one kind of posting would be two answers to "where does an issue
        // go", and a posting would take whichever was read first.
        InventoryAccountMap map = InventoryAccountMap.Create(Tenant, Firm);
        Ledger first = LedgerIn(Firm, "5200", "Materials Consumed");
        Ledger second = LedgerIn(Firm, "5210", "Works in Progress");

        map.Assign(StockAccount.Consumption, first);
        map.Assign(StockAccount.Consumption, second);

        map.Accounts.Count.ShouldBe(1);
        map.For(StockAccount.Consumption).Value.ShouldBe(second.Id);
    }

    [Fact]
    public void An_account_from_another_firm_cannot_be_chosen()
    {
        // The one thing the map can check, and the one that would otherwise post a
        // firm's stock into somebody else's books.
        InventoryAccountMap map = InventoryAccountMap.Create(Tenant, Firm);

        map.Assign(StockAccount.Inventory, LedgerIn(FirmId.NewId(), "1300", "Theirs"))
            .Error.Code.ShouldBe("InventoryAccounts.LedgerNotInFirm");
    }

    [Fact]
    public void A_withdrawn_account_cannot_be_chosen()
    {
        InventoryAccountMap map = InventoryAccountMap.Create(Tenant, Firm);
        Ledger closed = LedgerIn(Firm, "5900", "Old Loss Account");
        closed.Deactivate();

        map.Assign(StockAccount.Loss, closed).Error.Code
            .ShouldBe("InventoryAccounts.LedgerWithdrawn");
    }

    [Fact]
    public void Asking_for_an_account_nobody_chose_says_which_one_is_missing()
    {
        // The message is the point. A posting stops either way; telling the reader they
        // have to choose one thing, and which, is a minute's work instead of a hunt.
        InventoryAccountMap map = InventoryAccountMap.Create(Tenant, Firm);

        var failure = map.For(StockAccount.OpeningEquity);

        failure.Error.Code.ShouldBe("InventoryAccounts.NotConfigured");
        failure.Error.Description.ShouldContain("opening stock is credited to");
    }

    [Fact]
    public void A_map_is_complete_only_when_every_kind_has_an_account()
    {
        InventoryAccountMap map = InventoryAccountMap.Create(Tenant, Firm);

        foreach (StockAccount account in Enum.GetValues<StockAccount>())
        {
            map.IsComplete.ShouldBeFalse();
            map.Assign(account, LedgerIn(Firm, $"9{(int)account:000}", $"{account}"));
        }

        map.IsComplete.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ helpers

    private static Ledger LedgerIn(FirmId firmId, string code, string name)
    {
        AccountGroup group = AccountGroup.CreateRoot(
            Tenant, firmId, $"G{code}", $"Group {code}", AccountNature.Asset).Value;

        return Ledger.Create(group, code, name, LedgerKind.General, CurrencyCode.Qar).Value;
    }
}
