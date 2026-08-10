using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Accounting;

/// <summary>Tests for <see cref="AdditionalLedger"/>: section 9's charge matrix.</summary>
/// <remarks>
/// The matrix decides which charges appear on a document and which way each moves the
/// total. What these cover is the part that would otherwise be found on an invoice: a
/// charge that applies under no mode, a discount that adds instead of deducting, and a
/// mapping withdrawn while documents still carry it.
/// </remarks>
public sealed class AdditionalLedgerTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();

    [Fact]
    public void A_charge_applies_under_every_mode_until_somebody_narrows_it()
    {
        // A mapping that applied to nothing would sit on a settings screen looking
        // enabled and never appear on a document.
        AdditionalLedger freight = Map("5200", "Freight Charge");

        freight.AppliesTo(TaxMode.Tax).ShouldBeTrue();
        freight.AppliesTo(TaxMode.Cst).ShouldBeTrue();
        freight.AppliesTo(TaxMode.NonTax).ShouldBeTrue();
    }

    [Fact]
    public void A_charge_can_be_narrowed_to_the_modes_it_belongs_on()
    {
        AdditionalLedger charge = Map("5200", "Freight Charge");

        charge.SetModes(underTax: true, underCst: false, underNonTax: false)
            .IsSuccess.ShouldBeTrue();

        charge.AppliesTo(TaxMode.Tax).ShouldBeTrue();
        charge.AppliesTo(TaxMode.NonTax).ShouldBeFalse();
    }

    [Fact]
    public void A_charge_that_applies_under_no_mode_is_refused()
    {
        // It reads as a way to disable the charge, and there is already a way to do
        // that which says so.
        Map("5200", "Freight Charge")
            .SetModes(underTax: false, underCst: false, underNonTax: false)
            .Error.Code.ShouldBe("AdditionalLedger.NoModes");
    }

    [Fact]
    public void Direction_is_a_flag_rather_than_the_sign_somebody_types()
    {
        // A negative freight and a positive discount are both mistakes worth catching,
        // and neither can be caught without knowing which way the charge should go.
        Map("5200", "Freight Charge", isAddition: true).IsAddition.ShouldBeTrue();
        Map("5900", "Discount Allowed", isAddition: false).IsAddition.ShouldBeFalse();
    }

    [Fact]
    public void Only_what_is_marked_default_loads_onto_a_new_document()
    {
        AdditionalLedger rounding = Map("4900", "Round Off");

        rounding.IsDefault.ShouldBeFalse();

        rounding.SetDefault(true);

        rounding.IsDefault.ShouldBeTrue();
    }

    [Fact]
    public void A_withdrawn_charge_stops_appearing_without_being_deleted()
    {
        // Documents already carrying it keep it and keep pointing at the account it
        // posted to. A deleted mapping would leave those documents explaining
        // themselves with a row that no longer exists.
        AdditionalLedger charge = Map("5200", "Freight Charge");

        charge.Withdraw();

        charge.IsActive.ShouldBeFalse();
        charge.AppliesTo(TaxMode.Tax).ShouldBeFalse();

        charge.Restore();

        charge.AppliesTo(TaxMode.Tax).ShouldBeTrue();
    }

    [Fact]
    public void A_ledger_from_another_firm_cannot_be_mapped()
    {
        AdditionalLedger.Map(
                Tenant, Firm, ChargeableDocument.Sales, LedgerIn(FirmId.NewId(), "5200", "Theirs"))
            .Error.Code.ShouldBe("AdditionalLedger.LedgerNotInFirm");
    }

    [Fact]
    public void A_display_order_cannot_be_negative()
    {
        AdditionalLedger.Map(
                Tenant, Firm, ChargeableDocument.Sales, LedgerIn(Firm, "5200", "Freight"),
                displayOrder: -1)
            .Error.Code.ShouldBe("AdditionalLedger.OrderNegative");

        Map("5200", "Freight Charge").SetDisplayOrder(-1).Error.Code
            .ShouldBe("AdditionalLedger.OrderNegative");
    }

    // ------------------------------------------------------------------ helpers

    private static AdditionalLedger Map(string code, string name, bool isAddition = true) =>
        AdditionalLedger.Map(
            Tenant, Firm, ChargeableDocument.Sales, LedgerIn(Firm, code, name), isAddition).Value;

    private static Ledger LedgerIn(FirmId firmId, string code, string name)
    {
        AccountGroup group = AccountGroup.CreateRoot(
            Tenant, firmId, $"G{code}", $"Group {code}", AccountNature.Expense).Value;

        return Ledger.Create(
            group, code, name, LedgerKind.AdditionalCharge, CurrencyCode.Qar).Value;
    }
}
