using ERP.Domain.Accounting;
using ERP.Domain.Taxation;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Accounting;

/// <summary>Tests for <see cref="TaxAccountMap"/>: where each tax head posts.</summary>
/// <remarks>
/// This is what makes one posting produce a correct return in either jurisdiction, so
/// what these cover is the part a filing depends on: that output and input are kept
/// apart, that a head nobody has chosen an account for stops the document rather than
/// posting anywhere, and that the message says which head is missing.
/// </remarks>
public sealed class TaxAccountMapTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();

    [Fact]
    public void One_head_has_two_accounts_and_they_are_kept_apart()
    {
        // VAT charged to a customer is owed to the state; VAT paid to a supplier is
        // recoverable from it, and the return is the difference. One account for both
        // would net them silently and leave nothing to file.
        TaxAccountMap map = TaxAccountMap.Create(Tenant, Firm);
        Ledger output = LedgerIn(Firm, "2300", "Output VAT");
        Ledger input = LedgerIn(Firm, "1500", "Input VAT");

        map.Assign(TaxComponentType.Vat, TaxDirection.Output, output).IsSuccess.ShouldBeTrue();
        map.Assign(TaxComponentType.Vat, TaxDirection.Input, input).IsSuccess.ShouldBeTrue();

        map.For(TaxComponentType.Vat, TaxDirection.Output).Value.ShouldBe(output.Id);
        map.For(TaxComponentType.Vat, TaxDirection.Input).Value.ShouldBe(input.Id);
    }

    [Fact]
    public void A_head_nobody_chose_an_account_for_stops_the_document_and_says_which()
    {
        TaxAccountMap map = TaxAccountMap.Create(Tenant, Firm);

        var failure = map.For(TaxComponentType.Igst, TaxDirection.Output);

        failure.Error.Code.ShouldBe("TaxAccounts.NotConfigured");
        failure.Error.Description.ShouldContain("Igst");
    }

    [Fact]
    public void Choosing_again_repoints_rather_than_adding_a_second_answer()
    {
        TaxAccountMap map = TaxAccountMap.Create(Tenant, Firm);
        Ledger first = LedgerIn(Firm, "2300", "Output VAT");
        Ledger second = LedgerIn(Firm, "2310", "Output VAT (new)");

        map.Assign(TaxComponentType.Vat, TaxDirection.Output, first);
        map.Assign(TaxComponentType.Vat, TaxDirection.Output, second);

        map.Accounts.Count.ShouldBe(1);
        map.For(TaxComponentType.Vat, TaxDirection.Output).Value.ShouldBe(second.Id);
    }

    [Fact]
    public void An_account_from_another_firm_or_a_withdrawn_one_cannot_be_chosen()
    {
        TaxAccountMap map = TaxAccountMap.Create(Tenant, Firm);

        map.Assign(
                TaxComponentType.Vat, TaxDirection.Output,
                LedgerIn(FirmId.NewId(), "2300", "Theirs"))
            .Error.Code.ShouldBe("TaxAccounts.LedgerNotInFirm");

        Ledger closed = LedgerIn(Firm, "2300", "Old Output VAT");
        closed.Deactivate();

        map.Assign(TaxComponentType.Vat, TaxDirection.Output, closed)
            .Error.Code.ShouldBe("TaxAccounts.LedgerWithdrawn");
    }

    [Fact]
    public void A_firm_only_needs_accounts_for_the_heads_its_regime_uses()
    {
        // A VAT firm has no CGST to post, and asking it to choose an account for one
        // would be asking about a tax it does not pay.
        TaxAccountMap map = TaxAccountMap.Create(Tenant, Firm);

        map.Assign(
                TaxComponentType.Vat, TaxDirection.Output, LedgerIn(Firm, "2300", "Output VAT"))
            .IsSuccess.ShouldBeTrue();

        map.Accounts.ShouldHaveSingleItem();
        map.For(TaxComponentType.Vat, TaxDirection.Output).IsSuccess.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ helpers

    private static Ledger LedgerIn(FirmId firmId, string code, string name)
    {
        AccountGroup group = AccountGroup.CreateRoot(
            Tenant, firmId, $"G{code}", $"Group {code}", AccountNature.Liability).Value;

        return Ledger.Create(group, code, name, LedgerKind.Tax, CurrencyCode.Qar).Value;
    }
}
