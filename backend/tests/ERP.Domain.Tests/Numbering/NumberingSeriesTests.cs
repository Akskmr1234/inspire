using ERP.Domain.Accounting;
using ERP.Domain.Numbering;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tests.Numbering;

/// <summary>
/// Tests for <see cref="NumberingSeries"/>.
/// </summary>
/// <remarks>
/// The four format cases come straight from section 11 of the specification. The
/// exhaustion and gap-avoidance tests matter because a reissued document number
/// corrupts the audit trail in a way that is very hard to unpick afterwards.
/// </remarks>
public sealed class NumberingSeriesTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();

    // ------------------------------------------ the specification's four examples

    [Fact]
    public void Prefix_only()
    {
        // Specification 17.2: SL001, SL002
        NumberingSeries series = Series(numberLength: 3);
        series.SetFormat(prefix: "SL");

        series.Reserve().Value.ShouldBe("SL001");
        series.Reserve().Value.ShouldBe("SL002");
    }

    [Fact]
    public void Suffix_only()
    {
        // Specification 17.2: 001-SL, 002-SL
        NumberingSeries series = Series(numberLength: 3);
        series.SetFormat(prefix: null, suffix: "SL", separator: "-");

        series.Reserve().Value.ShouldBe("001-SL");
        series.Reserve().Value.ShouldBe("002-SL");
    }

    [Fact]
    public void Prefix_and_suffix()
    {
        // Specification 17.2: SL001A, SL002A
        NumberingSeries series = Series(numberLength: 3);
        series.SetFormat(prefix: "SL", suffix: "A");

        series.Reserve().Value.ShouldBe("SL001A");
        series.Reserve().Value.ShouldBe("SL002A");
    }

    [Fact]
    public void Financial_year_wise()
    {
        // Specification 17.2: SL/2026/0001, SL/2026/0002
        NumberingSeries series = Series(numberLength: 4);
        series.SetFormat(prefix: "SL", suffix: null, separator: "/", financialYearLabel: "2026");

        series.Reserve().Value.ShouldBe("SL/2026/0001");
        series.Reserve().Value.ShouldBe("SL/2026/0002");
    }

    // ------------------------------------------ padding and starting number

    [Theory]
    [InlineData(1, 1, "1")]
    [InlineData(1, 4, "0001")]
    [InlineData(42, 4, "0042")]
    [InlineData(1000, 4, "1000")]
    [InlineData(7, 8, "00000007")]
    public void The_counter_is_padded_to_the_configured_length(
        int start,
        int length,
        string expected)
    {
        NumberingSeries series = Series(startingNumber: start, numberLength: length);

        series.Reserve().Value.ShouldBe(expected);
    }

    [Fact]
    public void The_series_starts_at_the_configured_number()
    {
        NumberingSeries series = Series(startingNumber: 500, numberLength: 4);
        series.SetFormat("PR");

        series.Reserve().Value.ShouldBe("PR0500");
        series.Reserve().Value.ShouldBe("PR0501");
    }

    [Fact]
    public void Zero_is_a_valid_starting_number()
    {
        Create(startingNumber: 0).IsSuccess.ShouldBeTrue();
    }

    // ------------------------------------------ peek does not consume

    [Fact]
    public void Peeking_does_not_burn_a_number()
    {
        // A form that reserved on open would leave a gap every time a user changed
        // their mind - and gaps are what an audit asks about.
        NumberingSeries series = Series(numberLength: 4);
        series.SetFormat("JV");

        series.Peek().ShouldBe("JV0001");
        series.Peek().ShouldBe("JV0001");
        series.NextNumber.ShouldBe(1);

        series.Reserve().Value.ShouldBe("JV0001");
        series.Peek().ShouldBe("JV0002");
    }

    // ------------------------------------------ exhaustion

    [Fact]
    public void An_exhausted_series_refuses_rather_than_wrapping_round()
    {
        // Wrapping would reissue numbers already used, which corrupts the audit
        // trail silently. Refusing forces a deliberate configuration change.
        NumberingSeries series = Series(startingNumber: 998, numberLength: 3);

        series.Reserve().Value.ShouldBe("998");
        series.Reserve().Value.ShouldBe("999");

        Result<string> exhausted = series.Reserve();
        exhausted.IsFailure.ShouldBeTrue();
        exhausted.Error.Code.ShouldBe("NumberingSeries.Exhausted");
    }

    [Fact]
    public void Widening_an_exhausted_series_lets_it_continue()
    {
        NumberingSeries series = Series(startingNumber: 999, numberLength: 3);
        series.Reserve();
        series.Reserve().Error.Code.ShouldBe("NumberingSeries.Exhausted");

        series.WidenTo(4).IsSuccess.ShouldBeTrue();

        series.Reserve().Value.ShouldBe("1000");
    }

    [Fact]
    public void Narrowing_is_refused_because_it_would_reshape_existing_numbers()
    {
        NumberingSeries series = Series(numberLength: 6);

        Result result = series.WidenTo(4);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("NumberingSeries.CannotNarrow");
        series.NumberLength.ShouldBe(6);
    }

    // ------------------------------------------ scope

    [Fact]
    public void A_series_may_be_scoped_to_a_branch_a_year_both_or_neither()
    {
        BranchId branch = BranchId.NewId();
        Domain.Tenancy.FinancialYearId year = Domain.Tenancy.FinancialYearId.NewId();

        // Branch-wise: two branches each count independently, so both can hold
        // invoice 0001 without colliding.
        NumberingSeries perBranch = Create(branchId: branch).Value;
        perBranch.BranchId.ShouldBe(branch);
        perBranch.FinancialYearId.ShouldBeNull();

        NumberingSeries perYear = Create(financialYearId: year).Value;
        perYear.FinancialYearId.ShouldBe(year);
        perYear.BranchId.ShouldBeNull();

        NumberingSeries both = Create(branchId: branch, financialYearId: year).Value;
        both.BranchId.ShouldBe(branch);
        both.FinancialYearId.ShouldBe(year);

        NumberingSeries shared = Create().Value;
        shared.BranchId.ShouldBeNull();
        shared.FinancialYearId.ShouldBeNull();
    }

    // ------------------------------------------ lifecycle and validation

    [Fact]
    public void An_inactive_series_issues_nothing()
    {
        NumberingSeries series = Series();
        series.Deactivate();

        series.Reserve().Error.Code.ShouldBe("NumberingSeries.Inactive");

        series.Activate();
        series.Reserve().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Resetting_restarts_the_counter()
    {
        // What happens when a new financial year opens.
        NumberingSeries series = Series(numberLength: 4);
        series.SetFormat("SL");
        series.Reserve();
        series.Reserve();

        series.Reset(1).IsSuccess.ShouldBeTrue();

        series.Reserve().Value.ShouldBe("SL0001");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_document_type_is_required(string documentType) =>
        NumberingSeries.Create(Tenant, Firm, documentType).Error.Code
            .ShouldBe("NumberingSeries.DocumentTypeRequired");

    [Fact]
    public void The_document_type_is_normalised() =>
        NumberingSeries.Create(Tenant, Firm, "  Sales.Invoice  ").Value.DocumentType
            .ShouldBe("sales.invoice");

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void The_number_length_is_bounded(int length) =>
        Create(numberLength: length).Error.Code
            .ShouldBe("NumberingSeries.NumberLengthOutOfRange");

    [Fact]
    public void A_negative_starting_number_is_refused() =>
        Create(startingNumber: -1).Error.Code
            .ShouldBe("NumberingSeries.StartingNumberNegative");

    [Fact]
    public void An_over_long_affix_is_refused()
    {
        NumberingSeries series = Series();

        series.SetFormat(new string('X', 21)).Error.Code
            .ShouldBe("NumberingSeries.AffixTooLong");
        series.SetFormat("SL", new string('X', 21)).Error.Code
            .ShouldBe("NumberingSeries.AffixTooLong");
    }

    // ------------------------------------------ voucher type mapping

    [Theory]
    [InlineData(VoucherType.Journal, "accounting.journal")]
    [InlineData(VoucherType.CashReceipt, "accounting.cash-receipt")]
    [InlineData(VoucherType.BankPayment, "accounting.bank-payment")]
    [InlineData(VoucherType.Contra, "accounting.contra")]
    [InlineData(VoucherType.OpeningBalance, "accounting.opening-balance")]
    public void Every_voucher_type_maps_to_a_document_type(
        VoucherType type,
        string expected) =>
        DocumentTypes.ForVoucher(type).ShouldBe(expected);

    [Fact]
    public void All_voucher_types_are_mapped()
    {
        // Guards against a voucher type being added without a numbering series to
        // number it - which would surface as an exception at the worst moment,
        // mid-posting.
        foreach (VoucherType type in Enum.GetValues<VoucherType>())
        {
            Should.NotThrow(() => DocumentTypes.ForVoucher(type));
        }
    }

    // ------------------------------------------ helpers

    private static Result<NumberingSeries> Create(
        BranchId? branchId = null,
        Domain.Tenancy.FinancialYearId? financialYearId = null,
        int startingNumber = 1,
        int numberLength = 4) =>
        NumberingSeries.Create(
            Tenant, Firm, DocumentTypes.Journal, branchId, financialYearId,
            startingNumber, numberLength);

    private static NumberingSeries Series(int startingNumber = 1, int numberLength = 4) =>
        Create(startingNumber: startingNumber, numberLength: numberLength).Value;
}
