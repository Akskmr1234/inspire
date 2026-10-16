using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Accounting;

/// <summary>Tests for the parts of <see cref="Ledger"/> the chart of accounts corrects.</summary>
/// <remarks>
/// A ledger may move between groups and change between the ordinary kinds at any time,
/// because its balance is summed from postings and neither decides how. What may not
/// change is a customer, supplier, tax or charge ledger's kind: each has records of its
/// own that say what it is.
/// </remarks>
public sealed class LedgerTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly CurrencyCode Currency = CurrencyCode.FromTrusted("QAR");

    [Fact]
    public void A_ledger_moves_to_another_group_of_its_firm()
    {
        AccountGroup expenses = Group("5900", "Indirect Expenses", AccountNature.Expense, Firm);
        AccountGroup assets = Group("1110", "Cash and Bank", AccountNature.Asset, Firm);
        Ledger ledger = Ledger.Create(expenses, "RENT", "Rent", LedgerKind.General, Currency).Value;

        ledger.MoveTo(assets).IsSuccess.ShouldBeTrue();

        ledger.AccountGroupId.ShouldBe(assets.Id);
    }

    [Fact]
    public void A_ledger_does_not_move_into_another_firms_group()
    {
        AccountGroup ours = Group("5900", "Indirect Expenses", AccountNature.Expense, Firm);
        AccountGroup theirs = Group("5900", "Indirect Expenses", AccountNature.Expense, FirmId.NewId());
        Ledger ledger = Ledger.Create(ours, "RENT", "Rent", LedgerKind.General, Currency).Value;

        Result moved = ledger.MoveTo(theirs);

        moved.IsFailure.ShouldBeTrue();
        moved.Error.Code.ShouldBe("Ledger.GroupInAnotherFirm");
        ledger.AccountGroupId.ShouldBe(ours.Id);
    }

    [Theory]
    [InlineData(LedgerKind.General, LedgerKind.Bank)]
    [InlineData(LedgerKind.Bank, LedgerKind.Cash)]
    [InlineData(LedgerKind.Cash, LedgerKind.Employee)]
    public void An_ordinary_ledger_changes_between_the_ordinary_kinds(LedgerKind from, LedgerKind to)
    {
        Ledger ledger = Ledger.Create(
            Group("1110", "Cash and Bank", AccountNature.Asset, Firm), "ACC", "Account", from, Currency).Value;

        ledger.ChangeKind(to).IsSuccess.ShouldBeTrue();

        ledger.Kind.ShouldBe(to);
    }

    [Theory]
    [InlineData(LedgerKind.Customer, LedgerKind.General)]
    [InlineData(LedgerKind.General, LedgerKind.Supplier)]
    [InlineData(LedgerKind.Tax, LedgerKind.General)]
    [InlineData(LedgerKind.General, LedgerKind.AdditionalCharge)]
    public void A_kind_with_records_of_its_own_stays_as_it_is(LedgerKind from, LedgerKind to)
    {
        Ledger ledger = Ledger.Create(
            Group("1200", "Sundry Debtors", AccountNature.Asset, Firm), "ACC", "Account", from, Currency).Value;

        Result changed = ledger.ChangeKind(to);

        changed.IsFailure.ShouldBeTrue();
        changed.Error.Code.ShouldBe("Ledger.KindFixed");
        ledger.Kind.ShouldBe(from);
    }

    private static AccountGroup Group(string code, string name, AccountNature nature, FirmId firm) =>
        AccountGroup.CreateRoot(Tenant, firm, code, name, nature).Value;
}
