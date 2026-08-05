using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using FluentValidation.Results;

namespace ERP.Application.Tests.Accounting.Reports;

/// <summary>
/// Tests for <see cref="GetDayBookQueryHandler"/> and its validator.
/// </summary>
/// <remarks>
/// The first tests in this project. Domain invariants and persistence were already
/// well covered; what had no tests at all was the layer in between - the handler
/// that resolves the firm, calls the reader, and totals the result. That is where a
/// report silently reports on the wrong firm, or presents figures that do not add
/// up, and neither a domain test nor a database test would notice.
/// </remarks>
public sealed class GetDayBookTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly DateOnly From = new(2026, 4, 1);
    private static readonly DateOnly To = new(2026, 4, 30);

    // ------------------------------------------------------------ firm resolution

    [Fact]
    public async Task A_report_cannot_be_run_without_a_firm_selected()
    {
        // Figures from two firms must never be mixed. Refusing outright is the only
        // safe answer; guessing a firm would silently produce a report nobody can
        // trust.
        IFirmRepository firms = Substitute.For<IFirmRepository>();
        ITenantContext tenant = TenantScope(firmId: null);

        GetDayBookQueryHandler handler = new(Substitute.For<IDayBookReader>(), firms, tenant);

        Result<DayBookResponse> result = await handler.Handle(
            new GetDayBookQuery(From, To), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Report.NoFirmSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task A_selected_firm_that_no_longer_exists_is_reported_as_not_found()
    {
        FirmId firmId = FirmId.NewId();
        IFirmRepository firms = Substitute.For<IFirmRepository>();
        firms.FindAsync(firmId, Arg.Any<CancellationToken>()).Returns((Firm?)null);

        GetDayBookQueryHandler handler = new(
            Substitute.For<IDayBookReader>(), firms, TenantScope(firmId));

        Result<DayBookResponse> result = await handler.Handle(
            new GetDayBookQuery(From, To), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("Firm.NotFound");
    }

    [Fact]
    public async Task The_reader_is_scoped_to_the_selected_firm()
    {
        // The check that matters most: if the handler ever passed a different firm,
        // one customer's day book would show another's vouchers.
        Firm firm = CreateFirm();
        IDayBookReader reader = ReaderReturning([]);

        GetDayBookQueryHandler handler = new(reader, FirmsContaining(firm), TenantScope(firm.Id));

        await handler.Handle(
            new GetDayBookQuery(From, To), TestContext.Current.CancellationToken);

        await reader.Received(1).ReadAsync(
            firm.Id, From, To, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_voucher_type_filter_is_passed_through_to_the_reader()
    {
        Firm firm = CreateFirm();
        IDayBookReader reader = ReaderReturning([]);

        GetDayBookQueryHandler handler = new(reader, FirmsContaining(firm), TenantScope(firm.Id));

        await handler.Handle(
            new GetDayBookQuery(From, To, VoucherType.CashPayment),
            TestContext.Current.CancellationToken);

        await reader.Received(1).ReadAsync(
            firm.Id, From, To, VoucherType.CashPayment, Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------ totalling

    [Fact]
    public async Task Period_totals_are_summed_across_every_line_of_every_voucher()
    {
        Firm firm = CreateFirm();

        DayBookEntry receipt = Entry("CR/2026/0001", VoucherType.CashReceipt,
            Line("1000", debit: 500m), Line("4000", credit: 500m));

        DayBookEntry journal = Entry("JV/2026/0001", VoucherType.Journal,
            Line("5000", debit: 120m), Line("5100", debit: 80m), Line("2000", credit: 200m));

        GetDayBookQueryHandler handler = new(
            ReaderReturning([receipt, journal]), FirmsContaining(firm), TenantScope(firm.Id));

        DayBookResponse response = (await handler.Handle(
            new GetDayBookQuery(From, To), TestContext.Current.CancellationToken)).Value;

        response.TotalDebit.ShouldBe(700m);
        response.TotalCredit.ShouldBe(700m);
        response.VoucherCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_two_column_totals_agree_because_every_voucher_balances()
    {
        // The day book's own self-check. Debits equal credits by the voucher
        // aggregate's invariant, so the report's columns must agree too - and both
        // are surfaced precisely so a reader can see that they do.
        Firm firm = CreateFirm();

        DayBookEntry[] entries =
        [
            Entry("CR/1", VoucherType.CashReceipt, Line("1000", debit: 1234.56m), Line("4000", credit: 1234.56m)),
            Entry("BP/1", VoucherType.BankPayment, Line("5000", debit: 99.99m), Line("1010", credit: 99.99m)),
            Entry("CN/1", VoucherType.Contra, Line("1010", debit: 5000m), Line("1000", credit: 5000m)),
        ];

        GetDayBookQueryHandler handler = new(
            ReaderReturning(entries), FirmsContaining(firm), TenantScope(firm.Id));

        DayBookResponse response = (await handler.Handle(
            new GetDayBookQuery(From, To), TestContext.Current.CancellationToken)).Value;

        response.TotalDebit.ShouldBe(response.TotalCredit);
        response.TotalDebit.ShouldBe(6334.55m);
    }

    [Fact]
    public async Task An_empty_period_reports_zeroes_rather_than_failing()
    {
        // A firm that posted nothing in a period is entirely normal - a new branch,
        // a closed month. It must render as an empty register, not an error.
        Firm firm = CreateFirm();

        GetDayBookQueryHandler handler = new(
            ReaderReturning([]), FirmsContaining(firm), TenantScope(firm.Id));

        DayBookResponse response = (await handler.Handle(
            new GetDayBookQuery(From, To), TestContext.Current.CancellationToken)).Value;

        response.Entries.ShouldBeEmpty();
        response.VoucherCount.ShouldBe(0);
        response.TotalDebit.ShouldBe(0m);
        response.TotalCredit.ShouldBe(0m);
    }

    [Fact]
    public async Task Figures_are_labelled_with_the_firms_base_currency()
    {
        // A report of bare numbers is unreadable when a tenant runs a Qatari and an
        // Indian firm side by side.
        Firm firm = CreateFirm(CurrencyCode.Qar);

        GetDayBookQueryHandler handler = new(
            ReaderReturning([]), FirmsContaining(firm), TenantScope(firm.Id));

        DayBookResponse response = (await handler.Handle(
            new GetDayBookQuery(From, To), TestContext.Current.CancellationToken)).Value;

        response.Currency.ShouldBe("QAR");
        response.From.ShouldBe(From);
        response.To.ShouldBe(To);
    }

    // ------------------------------------------------------------ validation

    [Fact]
    public void A_range_ending_before_it_starts_is_rejected()
    {
        // Bound to neutral locals first. Passing From and To in swapped positions
        // is the whole point of the test, but written directly it reads - to a
        // human and to the analyzer alike - like a mistake in the test itself.
        DateOnly laterDate = To;
        DateOnly earlierDate = From;

        ValidationResult result = Validate(new GetDayBookQuery(laterDate, earlierDate));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("cannot precede"));
    }

    [Fact]
    public void A_single_day_range_is_accepted()
    {
        // The commonest request of all: "what happened today".
        Validate(new GetDayBookQuery(From, From)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_range_longer_than_a_year_is_rejected()
    {
        // The day book returns every line of every voucher. Unbounded, one request
        // could pull a year of postings into memory and hold the connection while it
        // serialises.
        GetDayBookQuery tooLong = new(
            From, From.AddDays(GetDayBookQueryValidator.MaximumRangeDays));

        Validate(tooLong).IsValid.ShouldBeFalse();

        GetDayBookQuery justInside = new(
            From, From.AddDays(GetDayBookQueryValidator.MaximumRangeDays - 1));

        Validate(justInside).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_missing_date_is_rejected()
    {
        Validate(new GetDayBookQuery(default, To)).IsValid.ShouldBeFalse();
        Validate(new GetDayBookQuery(From, default)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void An_unrecognised_voucher_type_is_rejected()
    {
        Validate(new GetDayBookQuery(From, To, (VoucherType)999)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void An_absent_voucher_type_is_accepted_and_means_every_kind()
    {
        Validate(new GetDayBookQuery(From, To, null)).IsValid.ShouldBeTrue();
    }

    // ------------------------------------------------------------ helpers

    private static ValidationResult Validate(GetDayBookQuery query) =>
        new GetDayBookQueryValidator().Validate(query);

    private static Firm CreateFirm(CurrencyCode? currency = null) => Firm.Create(
        Tenant,
        "ACME",
        "Acme Trading",
        currency ?? CurrencyCode.Qar,
        TaxRegime.GccVat,
        "Asia/Qatar").Value;

    private static IFirmRepository FirmsContaining(Firm firm)
    {
        IFirmRepository firms = Substitute.For<IFirmRepository>();
        firms.FindAsync(firm.Id, Arg.Any<CancellationToken>()).Returns(firm);
        return firms;
    }

    private static IDayBookReader ReaderReturning(IReadOnlyList<DayBookEntry> entries)
    {
        IDayBookReader reader = Substitute.For<IDayBookReader>();

        reader.ReadAsync(
                Arg.Any<FirmId>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<VoucherType?>(),
                Arg.Any<CancellationToken>())
            .Returns(entries);

        return reader;
    }

    private static ITenantContext TenantScope(FirmId? firmId)
    {
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.IsResolved.Returns(true);
        tenant.TenantId.Returns(Tenant);
        tenant.FirmId.Returns(firmId);
        return tenant;
    }

    private static DayBookEntry Entry(
        string number,
        VoucherType type,
        params DayBookLine[] lines) =>
        new(Guid.CreateVersion7(), From, number, type, null, null, lines.Sum(l => l.Debit), lines);

    private static DayBookLine Line(string ledgerCode, decimal debit = 0m, decimal credit = 0m) =>
        new(Guid.CreateVersion7(), ledgerCode, $"Ledger {ledgerCode}", null, debit, credit);
}
