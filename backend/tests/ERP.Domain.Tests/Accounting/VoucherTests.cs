using System.Globalization;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Accounting;

/// <summary>
/// Tests for <see cref="Voucher"/>.
/// </summary>
/// <remarks>
/// The balance invariant and the multi-currency conversion carry the most weight.
/// An unbalanced posting does not announce itself: the voucher prints correctly,
/// the ledger looks plausible, and the discrepancy surfaces weeks later as a trial
/// balance that will not close, by which time hundreds of documents are suspect.
/// </remarks>
public sealed class VoucherTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly BranchId Branch = BranchId.NewId();
    private static readonly LedgerId Cash = LedgerId.NewId();
    private static readonly LedgerId Sales = LedgerId.NewId();
    private static readonly LedgerId Vat = LedgerId.NewId();
    private static readonly UserId Poster = UserId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly CurrencyCode Usd = CurrencyCode.Usd;

    // ------------------------------------------------------------- the balance rule

    [Fact]
    public void A_balanced_voucher_posts()
    {
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 105m);
        voucher.AddLine(Sales, EntrySide.Credit, 100m);
        voucher.AddLine(Vat, EntrySide.Credit, 5m);

        voucher.IsBalanced.ShouldBeTrue();
        voucher.Post(Poster, Now).IsSuccess.ShouldBeTrue();

        voucher.Status.ShouldBe(VoucherStatus.Posted);
        voucher.PostedBy.ShouldBe(Poster);
        voucher.PostedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void An_unbalanced_voucher_is_refused_and_says_by_how_much()
    {
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 100m);
        voucher.AddLine(Sales, EntrySide.Credit, 90m);

        Result result = voucher.Post(Poster, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Voucher.NotBalanced");
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);

        // The difference belongs in the message: it is what tells an accountant
        // they have transposed a digit rather than mistyped an entire figure.
        result.Error.Description.ShouldContain("10");
        voucher.Status.ShouldBe(VoucherStatus.Draft);
    }

    [Fact]
    public void A_single_line_voucher_cannot_balance()
    {
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 100m);

        voucher.Post(Poster, Now).Error.Code.ShouldBe("Voucher.TooFewLines");
    }

    [Fact]
    public void A_voucher_with_no_lines_cannot_post()
    {
        Draft().Post(Poster, Now).Error.Code.ShouldBe("Voucher.TooFewLines");
    }

    [Fact]
    public void Lines_on_only_one_side_cannot_post_even_when_they_sum()
    {
        // Two debits of 50 total 100 and would pass a careless "is the total
        // right?" check, but there is no credit for them to balance against.
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 50m);
        voucher.AddLine(Sales, EntrySide.Debit, 50m);

        voucher.Post(Poster, Now).Error.Code.ShouldBe("Voucher.SingleSided");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void A_line_must_have_a_positive_amount(decimal amount)
    {
        // Zero is rejected as well as negative: a zero line passes the balance
        // check and prints as a baffling blank row.
        Voucher voucher = Draft();

        Result<VoucherLine> result = voucher.AddLine(Cash, EntrySide.Debit, amount);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Voucher.LineAmountNotPositive");
        voucher.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void Many_lines_on_both_sides_balance()
    {
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 33.33m);
        voucher.AddLine(Cash, EntrySide.Debit, 33.33m);
        voucher.AddLine(Cash, EntrySide.Debit, 33.34m);
        voucher.AddLine(Sales, EntrySide.Credit, 50m);
        voucher.AddLine(Vat, EntrySide.Credit, 50m);

        voucher.TotalDebit.Amount.ShouldBe(100m);
        voucher.TotalCredit.Amount.ShouldBe(100m);
        voucher.Post(Poster, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_running_difference_is_visible_while_entering()
    {
        // The entry grid shows this as the user types; it is how the mistake is
        // caught before posting rather than after.
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 100m);

        voucher.Difference.Amount.ShouldBe(100m);
        voucher.IsBalanced.ShouldBeFalse();

        voucher.AddLine(Sales, EntrySide.Credit, 60m);
        voucher.Difference.Amount.ShouldBe(40m);

        voucher.AddLine(Vat, EntrySide.Credit, 40m);
        voucher.Difference.IsZero.ShouldBeTrue();
        voucher.IsBalanced.ShouldBeTrue();
    }

    // ------------------------------------------------------------- multi-currency

    [Fact]
    public void Base_amounts_equal_entry_amounts_when_the_currency_matches()
    {
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 100m);
        voucher.AddLine(Sales, EntrySide.Credit, 100m);
        voucher.Post(Poster, Now);

        voucher.Lines.ShouldAllBe(l => l.BaseAmount.Amount == l.Amount.Amount);
        voucher.Lines.ShouldAllBe(l => l.BaseAmount.Currency == Qar);
    }

    [Fact]
    public void Converting_a_foreign_voucher_keeps_base_debits_equal_to_base_credits()
    {
        // The failure this prevents: converting each line independently at 3.6405
        // gives credits of 364.05 + 182.03 = 546.08 against a single debit of
        // 546.08 - or, with other figures, 546.07. A one-fils gap in the base
        // currency stops the trial balance closing, for a reason invisible on the
        // voucher itself.
        Voucher voucher = Draft(Usd, rate: 3.6405m);
        voucher.AddLine(Cash, EntrySide.Debit, 150m);
        voucher.AddLine(Sales, EntrySide.Credit, 100m);
        voucher.AddLine(Vat, EntrySide.Credit, 50m);

        voucher.Post(Poster, Now).IsSuccess.ShouldBeTrue();

        Money baseDebits = SumBase(voucher, EntrySide.Debit);
        Money baseCredits = SumBase(voucher, EntrySide.Credit);

        baseDebits.ShouldBe(baseCredits);
        baseDebits.Amount.ShouldBe(546.08m);
        voucher.Lines.ShouldAllBe(l => l.BaseAmount.Currency == Qar);
    }

    [Theory]
    [InlineData(3.6405)]
    [InlineData(0.27)]
    [InlineData(1.0001)]
    [InlineData(83.4567)]
    [InlineData(0.000123)]
    public void Base_currency_always_balances_whatever_the_rate(decimal rate)
    {
        Voucher voucher = Draft(Usd, rate);
        voucher.AddLine(Cash, EntrySide.Debit, 33.33m);
        voucher.AddLine(Sales, EntrySide.Credit, 11.11m);
        voucher.AddLine(Vat, EntrySide.Credit, 11.11m);
        voucher.AddLine(Sales, EntrySide.Credit, 11.11m);

        voucher.Post(Poster, Now).IsSuccess.ShouldBeTrue();

        SumBase(voucher, EntrySide.Debit).ShouldBe(SumBase(voucher, EntrySide.Credit));
    }

    [Fact]
    public void The_signed_base_amounts_of_a_posted_voucher_sum_to_zero()
    {
        // This property, summed across every posted line in a firm, *is* the trial
        // balance.
        Voucher voucher = Draft(Usd, rate: 3.6405m);
        voucher.AddLine(Cash, EntrySide.Debit, 99.99m);
        voucher.AddLine(Sales, EntrySide.Credit, 33.33m);
        voucher.AddLine(Vat, EntrySide.Credit, 66.66m);
        voucher.Post(Poster, Now);

        voucher.Lines.Sum(l => l.SignedBaseAmount).ShouldBe(0m);
    }

    [Fact]
    public void An_exchange_rate_must_be_positive()
    {
        DraftResult(Usd, rate: 0m).Error.Code.ShouldBe("Voucher.ExchangeRateInvalid");
        DraftResult(Usd, rate: -1m).Error.Code.ShouldBe("Voucher.ExchangeRateInvalid");
    }

    [Fact]
    public void A_rate_other_than_one_is_refused_when_the_currencies_match()
    {
        // Would silently restate the books: every base figure scaled, with nothing
        // on the voucher to indicate why.
        DraftResult(Qar, rate: 3.64m).Error.Code.ShouldBe("Voucher.ExchangeRateMustBeOne");
    }

    // ------------------------------------------------------------- financial year

    [Fact]
    public void A_voucher_cannot_be_dated_outside_its_financial_year()
    {
        FinancialYear year = Year();

        Result<Voucher> result = Voucher.CreateDraft(
            Tenant, Firm, Branch, year, VoucherType.Journal, "JV/0001",
            new DateOnly(2027, 6, 1), Qar, Qar);

        result.Error.Code.ShouldBe("FinancialYear.DateOutOfRange");
    }

    [Fact]
    public void A_voucher_cannot_post_into_a_closed_year()
    {
        FinancialYear year = Year();
        year.Close();

        Result<Voucher> result = Voucher.CreateDraft(
            Tenant, Firm, Branch, year, VoucherType.Journal, "JV/0001",
            new DateOnly(2026, 6, 1), Qar, Qar);

        result.Error.Code.ShouldBe("FinancialYear.Closed");
    }

    // ------------------------------------------------------------- lifecycle

    [Fact]
    public void A_posted_voucher_cannot_be_changed()
    {
        Voucher voucher = Posted();

        voucher.AddLine(Sales, EntrySide.Credit, 10m).Error.Code
            .ShouldBe("Voucher.NotEditable");
        voucher.RemoveLine(voucher.Lines[0].Id).Error.Code
            .ShouldBe("Voucher.NotEditable");
        voucher.SetDetails("REF", "narration", "CASH").Error.Code
            .ShouldBe("Voucher.NotEditable");
    }

    [Fact]
    public void A_voucher_cannot_be_posted_twice()
    {
        Voucher voucher = Posted();

        voucher.Post(Poster, Now).Error.Code.ShouldBe("Voucher.AlreadyPosted");
    }

    [Fact]
    public void Cancelling_retains_the_voucher_and_its_number()
    {
        // A number that vanished would leave an unexplained gap in the sequence,
        // which is exactly what an audit treats as suspicious.
        Voucher voucher = Posted();

        voucher.Cancel("Duplicate entry").IsSuccess.ShouldBeTrue();

        voucher.Status.ShouldBe(VoucherStatus.Cancelled);
        voucher.CancellationReason.ShouldBe("Duplicate entry");
        voucher.Number.ShouldBe("JV/0001");
        voucher.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public void Cancelling_requires_a_reason()
    {
        Posted().Cancel("   ").Error.Code.ShouldBe("Voucher.CancellationReasonRequired");
    }

    [Fact]
    public void A_draft_cannot_be_cancelled()
    {
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 10m);

        voucher.Cancel("Mistake").Error.Code.ShouldBe("Voucher.NotPosted");
    }

    [Fact]
    public void Removing_a_line_renumbers_the_rest()
    {
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 100m);
        VoucherLine middle = voucher.AddLine(Sales, EntrySide.Credit, 60m).Value;
        voucher.AddLine(Vat, EntrySide.Credit, 40m);

        voucher.RemoveLine(middle.Id).IsSuccess.ShouldBeTrue();

        voucher.Lines.Count.ShouldBe(2);
        voucher.Lines.Select(l => l.LineNumber).ShouldBe([1, 2]);
    }

    [Fact]
    public void Removing_an_unknown_line_fails()
    {
        Draft().RemoveLine(VoucherLineId.NewId()).Error.Code.ShouldBe("Voucher.LineNotFound");
    }

    [Fact]
    public void Debit_and_credit_projections_match_the_entry_grid()
    {
        Voucher voucher = Draft();
        VoucherLine debit = voucher.AddLine(Cash, EntrySide.Debit, 100m).Value;
        VoucherLine credit = voucher.AddLine(Sales, EntrySide.Credit, 100m).Value;

        debit.DebitAmount.Amount.ShouldBe(100m);
        debit.CreditAmount.IsZero.ShouldBeTrue();
        credit.CreditAmount.Amount.ShouldBe(100m);
        credit.DebitAmount.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Posting_raises_an_event_carrying_the_total()
    {
        Voucher voucher = Posted();

        VoucherPosted raised = voucher.DomainEvents.OfType<VoucherPosted>().ShouldHaveSingleItem();
        raised.Number.ShouldBe("JV/0001");
        raised.Total.Amount.ShouldBe(100m);
        raised.Type.ShouldBe(VoucherType.Journal);
    }

    [Fact]
    public void A_refused_posting_raises_no_event()
    {
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 100m);
        voucher.AddLine(Sales, EntrySide.Credit, 90m);

        voucher.Post(Poster, Now);

        voucher.DomainEvents.ShouldBeEmpty();
    }

    // ------------------------------------------------------------- helpers

    private static DateTimeOffset Now => new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private static FinancialYear Year() => FinancialYear.Create(
        Tenant, Firm, "2026",
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

    private static Result<Voucher> DraftResult(CurrencyCode currency, decimal rate) =>
        Voucher.CreateDraft(
            Tenant, Firm, Branch, Year(), VoucherType.Journal, "JV/0001",
            new DateOnly(2026, 6, 15), currency, Qar, rate);

    private static Voucher Draft(CurrencyCode? currency = null, decimal rate = 1m) =>
        DraftResult(currency ?? Qar, rate).Value;

    private static Voucher Posted()
    {
        Voucher voucher = Draft();
        voucher.AddLine(Cash, EntrySide.Debit, 100m);
        voucher.AddLine(Sales, EntrySide.Credit, 100m);
        voucher.Post(Poster, Now);

        return voucher;
    }

    private static Money SumBase(Voucher voucher, EntrySide side) =>
        Money.Sum(
            voucher.Lines.Where(l => l.Side == side).Select(l => l.BaseAmount),
            voucher.BaseCurrency);
}
