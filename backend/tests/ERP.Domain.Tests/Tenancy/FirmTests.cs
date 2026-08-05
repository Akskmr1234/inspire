using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Tenancy;

/// <summary>Tests for <see cref="Firm"/> and <see cref="Branch"/>.</summary>
public sealed class FirmTests
{
    private static readonly TenantId Tenant = TenantId.NewId();

    // ---------------------------------------------------------------- creation

    [Fact]
    public void A_firm_is_created_active_with_a_normalised_code()
    {
        Firm firm = CreateFirm(code: "  startech  ");

        firm.Code.ShouldBe("STARTECH");
        firm.IsActive.ShouldBeTrue();
        firm.Branches.ShouldBeEmpty();
    }

    [Fact]
    public void Two_firms_in_one_tenant_can_run_different_tax_regimes()
    {
        // The whole point of a per-firm regime: one instance serving a Qatar VAT
        // business and an Indian GST business side by side.
        Firm qatar = Create("QA", CurrencyCode.Qar, TaxRegime.GccVat, "Asia/Qatar").Value;
        Firm india = Create("IN", CurrencyCode.Inr, TaxRegime.IndiaGst, "Asia/Kolkata").Value;

        qatar.TenantId.ShouldBe(india.TenantId);
        qatar.TaxRegime.ShouldBe(TaxRegime.GccVat);
        india.TaxRegime.ShouldBe(TaxRegime.IndiaGst);
        qatar.BaseCurrency.ShouldBe(CurrencyCode.Qar);
        india.BaseCurrency.ShouldBe(CurrencyCode.Inr);
    }

    [Theory]
    [InlineData("", "Firm.CodeRequired")]
    [InlineData("   ", "Firm.CodeRequired")]
    public void A_blank_code_is_rejected(string code, string expectedError) =>
        Create(code).Error.Code.ShouldBe(expectedError);

    [Fact]
    public void An_over_long_code_is_rejected() =>
        Create(new string('X', 21)).Error.Code.ShouldBe("Firm.CodeTooLong");

    [Fact]
    public void An_unknown_time_zone_is_rejected()
    {
        Result<Firm> result = Create(
            "ACME", CurrencyCode.Qar, TaxRegime.GccVat, "Mars/Olympus_Mons");

        result.Error.Code.ShouldBe("Firm.UnknownTimeZone");
    }

    [Fact]
    public void An_unspecified_base_currency_is_rejected()
    {
        Result<Firm> result = Firm.Create(
            Tenant, "ACME", "Acme", default, TaxRegime.GccVat, "Asia/Qatar");

        result.Error.Code.ShouldBe("Firm.BaseCurrencyRequired");
    }

    // ---------------------------------------------------------------- branches

    [Fact]
    public void Branches_are_added_through_the_firm()
    {
        Firm firm = CreateFirm();

        Result<Branch> head = firm.AddBranch("HO", "Head Office", isHeadOffice: true);
        Result<Branch> second = firm.AddBranch("br1", "Branch 1");

        head.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        firm.Branches.Count.ShouldBe(2);
        second.Value.Code.ShouldBe("BR1");
        second.Value.FirmId.ShouldBe(firm.Id);
        second.Value.TenantId.ShouldBe(firm.TenantId);
    }

    [Fact]
    public void A_branch_inherits_the_firms_time_zone_by_default()
    {
        Firm firm = Create("QA", CurrencyCode.Qar, TaxRegime.GccVat, "Asia/Qatar").Value;

        firm.AddBranch("HO", "Head Office").Value.TimeZoneId.ShouldBe("Asia/Qatar");
    }

    [Fact]
    public void A_branch_may_override_the_time_zone()
    {
        // A firm can legitimately span time zones; the till closes on local time.
        Firm firm = Create("QA", CurrencyCode.Qar, TaxRegime.GccVat, "Asia/Qatar").Value;
        Branch branch = firm.AddBranch("IN1", "Kochi").Value;

        branch.SetTimeZone("Asia/Kolkata").IsSuccess.ShouldBeTrue();
        branch.TimeZoneId.ShouldBe("Asia/Kolkata");

        branch.SetTimeZone("Nowhere/Nothing").Error.Code.ShouldBe("Branch.UnknownTimeZone");
    }

    [Fact]
    public void Duplicate_branch_codes_are_rejected_within_a_firm()
    {
        Firm firm = CreateFirm();
        firm.AddBranch("HO", "Head Office");

        Result<Branch> duplicate = firm.AddBranch("ho", "Another");

        duplicate.Error.Code.ShouldBe("Branch.DuplicateCode");
        duplicate.Error.Kind.ShouldBe(ErrorKind.Conflict);
        firm.Branches.Count.ShouldBe(1);
    }

    [Fact]
    public void The_same_branch_code_may_be_reused_in_a_different_firm()
    {
        // Codes are unique within a firm, not across the tenant.
        Firm first = Create("F1").Value;
        Firm second = Create("F2").Value;

        first.AddBranch("HO", "Head Office").IsSuccess.ShouldBeTrue();
        second.AddBranch("HO", "Head Office").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_firm_may_have_only_one_head_office()
    {
        Firm firm = CreateFirm();
        firm.AddBranch("HO", "Head Office", isHeadOffice: true);

        Result<Branch> second = firm.AddBranch("HO2", "Another HO", isHeadOffice: true);

        second.Error.Code.ShouldBe("Branch.HeadOfficeAlreadyExists");
    }

    [Fact]
    public void An_inactive_firm_takes_no_new_branches()
    {
        Firm firm = CreateFirm();
        firm.Deactivate();

        firm.AddBranch("HO", "Head Office").Error.Code.ShouldBe("Firm.Inactive");
    }

    [Fact]
    public void A_branch_without_a_name_is_rejected()
    {
        CreateFirm().AddBranch("HO", "  ").Error.Code.ShouldBe("Branch.NameRequired");
    }

    // ---------------------------------------------------------------- tax registration

    [Fact]
    public void A_gst_firm_must_supply_a_state_code()
    {
        // Without the firm's own state there is nothing to compare against, so
        // every supply would look intra-state and under-charge inter-state sales.
        Firm firm = Create("IN", CurrencyCode.Inr, TaxRegime.IndiaGst, "Asia/Kolkata").Value;

        firm.SetTaxRegistration("29ABCDE1234F1Z5", null)
            .Error.Code.ShouldBe("Firm.StateCodeRequiredForGst");

        firm.SetTaxRegistration("29ABCDE1234F1Z5", "KA").IsSuccess.ShouldBeTrue();
        firm.StateCode.ShouldBe("KA");
    }

    [Fact]
    public void A_vat_firm_needs_no_state_code()
    {
        // Place of supply is an Indian GST concept.
        Firm firm = Create("QA", CurrencyCode.Qar, TaxRegime.GccVat, "Asia/Qatar").Value;

        firm.SetTaxRegistration("QA123456789", null).IsSuccess.ShouldBeTrue();
        firm.TaxRegistrationNumber.ShouldBe("QA123456789");
    }

    // ---------------------------------------------------------------- lifecycle

    [Fact]
    public void Deactivating_a_firm_raises_an_event_once()
    {
        Firm firm = CreateFirm();

        firm.Deactivate();
        firm.Deactivate();

        firm.IsActive.ShouldBeFalse();
        firm.DomainEvents.OfType<FirmDeactivated>().Count().ShouldBe(1);
    }

    [Fact]
    public void Adding_a_branch_raises_an_event()
    {
        Firm firm = CreateFirm();
        Branch branch = firm.AddBranch("HO", "Head Office").Value;

        BranchAdded raised = firm.DomainEvents.OfType<BranchAdded>().ShouldHaveSingleItem();
        raised.BranchId.ShouldBe(branch.Id);
        raised.FirmId.ShouldBe(firm.Id);
        raised.Code.ShouldBe("HO");
    }

    [Fact]
    public void A_rejected_branch_raises_no_event_and_is_not_added()
    {
        Firm firm = CreateFirm();
        firm.AddBranch("HO", "Head Office");
        firm.ClearDomainEvents();

        firm.AddBranch("HO", "Duplicate");

        firm.DomainEvents.ShouldBeEmpty();
        firm.Branches.Count.ShouldBe(1);
    }

    // ---------------------------------------------------------------- helpers

    private static Result<Firm> Create(
        string code,
        CurrencyCode? currency = null,
        TaxRegime regime = TaxRegime.GccVat,
        string timeZone = "Asia/Qatar") =>
        Firm.Create(Tenant, code, "Test Firm", currency ?? CurrencyCode.Qar, regime, timeZone);

    private static Firm CreateFirm(string code = "ACME") => Create(code).Value;
}
