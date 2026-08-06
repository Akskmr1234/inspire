using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Tests.Accounting.Reports;

/// <summary>
/// Tests for <see cref="GetOutstandingBillsQueryHandler"/>.
/// </summary>
/// <remarks>
/// The debtors and creditors reports. What matters here is not the arithmetic of a
/// single bill - the aggregate settles that - but the shape of the answer: that
/// parties come out grouped and ordered the way somebody chasing a debt reads them,
/// that overdue is counted from the due date, and that "overdue only" narrows the
/// list without corrupting the totals beside it.
/// </remarks>
public sealed class GetOutstandingBillsTests
{
    private static readonly DateOnly AsAt = new(2026, 6, 30);

    [Fact]
    public async Task Bills_are_grouped_by_party_in_ledger_code_order()
    {
        Fixture fixture = new();
        fixture.Open("2000", "Al Mansoor", "INV-2", 100m, due: AsAt);
        fixture.Open("1000", "Zenith Stores", "INV-1", 250m, due: AsAt);

        OutstandingBillsResponse report = (await fixture.Handle(Query())).Value;

        report.Parties.Count.ShouldBe(2);

        // Code order, not name order - the code is what the chart of accounts is
        // filed under, so a reader comparing this to the ledger finds their place.
        report.Parties[0].LedgerCode.ShouldBe("1000");
        report.Parties[1].LedgerCode.ShouldBe("2000");
        report.TotalOutstanding.ShouldBe(350m);
    }

    [Fact]
    public async Task A_partly_settled_bill_reports_only_what_remains()
    {
        Fixture fixture = new();
        fixture.Open("1000", "Zenith Stores", "INV-1", 1_000m, due: AsAt, settled: 400m);

        OutstandingBillsResponse report = (await fixture.Handle(Query())).Value;

        OutstandingBill bill = report.Parties.ShouldHaveSingleItem().Bills.ShouldHaveSingleItem();
        bill.OriginalAmount.ShouldBe(1_000m);
        bill.SettledAmount.ShouldBe(400m);
        bill.OutstandingAmount.ShouldBe(600m);
        report.TotalOutstanding.ShouldBe(600m);
    }

    [Fact]
    public async Task Bills_are_listed_oldest_due_date_first()
    {
        // The report is read to decide who to chase, and the bill owed longest is
        // the one that decides it.
        Fixture fixture = new();
        fixture.Open("1000", "Zenith Stores", "NEWER", 100m, due: AsAt.AddDays(-5));
        fixture.Open("1000", "Zenith Stores", "OLDEST", 100m, due: AsAt.AddDays(-90));
        fixture.Open("1000", "Zenith Stores", "MIDDLE", 100m, due: AsAt.AddDays(-40));

        OutstandingBillsResponse report = (await fixture.Handle(Query())).Value;

        report.Parties.ShouldHaveSingleItem().Bills
            .Select(b => b.BillNumber)
            .ShouldBe(["OLDEST", "MIDDLE", "NEWER"]);
    }

    [Fact]
    public async Task Days_overdue_are_counted_from_the_due_date()
    {
        // Not from the bill date. Aging from when the invoice was raised would report
        // every bill as overdue the moment it is issued.
        Fixture fixture = new();
        fixture.Open(
            "1000", "Zenith Stores", "INV-1", 500m,
            due: AsAt.AddDays(-45), billDate: AsAt.AddDays(-75));

        OutstandingBillsResponse report = (await fixture.Handle(Query())).Value;

        report.Parties[0].Bills[0].DaysOverdue.ShouldBe(45);
    }

    [Fact]
    public async Task A_bill_due_today_is_not_yet_overdue()
    {
        Fixture fixture = new();
        fixture.Open("1000", "Zenith Stores", "INV-1", 500m, due: AsAt);

        OutstandingBillsResponse report = (await fixture.Handle(Query())).Value;

        report.Parties[0].Bills[0].DaysOverdue.ShouldBe(0);
        report.TotalOverdue.ShouldBe(0m);
        report.TotalOutstanding.ShouldBe(500m);
    }

    [Fact]
    public async Task The_overdue_total_counts_only_the_late_bills()
    {
        Fixture fixture = new();
        fixture.Open("1000", "Zenith Stores", "LATE", 300m, due: AsAt.AddDays(-10));
        fixture.Open("1000", "Zenith Stores", "CURRENT", 700m, due: AsAt.AddDays(20));

        OutstandingBillsResponse report = (await fixture.Handle(Query())).Value;

        report.TotalOutstanding.ShouldBe(1_000m);
        report.TotalOverdue.ShouldBe(300m);
        report.Parties[0].TotalOverdue.ShouldBe(300m);
    }

    [Fact]
    public async Task Overdue_only_drops_the_current_bills_and_the_parties_with_none()
    {
        // The specification's "Over Due Sales Invoice". A party whose every bill is
        // current must disappear entirely rather than appear as an empty section.
        Fixture fixture = new();
        fixture.Open("1000", "Zenith Stores", "LATE", 300m, due: AsAt.AddDays(-10));
        fixture.Open("1000", "Zenith Stores", "CURRENT", 700m, due: AsAt.AddDays(20));
        fixture.Open("2000", "Al Mansoor", "ALSO-CURRENT", 900m, due: AsAt.AddDays(5));

        OutstandingBillsResponse report =
            (await fixture.Handle(Query() with { OverdueOnly = true })).Value;

        report.Parties.ShouldHaveSingleItem().Bills.ShouldHaveSingleItem()
            .BillNumber.ShouldBe("LATE");
        report.TotalOutstanding.ShouldBe(300m);
        report.TotalOverdue.ShouldBe(300m);
    }

    [Fact]
    public async Task The_currencies_in_play_are_reported_alongside_the_total()
    {
        // A total across currencies is not a number anybody should print. Saying so
        // is better than silently summing riyals and dollars into one figure.
        Fixture fixture = new();
        fixture.Open("1000", "Zenith Stores", "QAR-1", 100m, due: AsAt);
        fixture.Open("2000", "Al Mansoor", "USD-1", 200m, due: AsAt, currency: "USD");

        OutstandingBillsResponse report = (await fixture.Handle(Query())).Value;

        report.Currencies.ShouldBe(["QAR", "USD"]);
    }

    [Fact]
    public async Task The_report_asks_the_reader_for_the_type_it_was_run_for()
    {
        Fixture fixture = new();

        await fixture.Handle(Query() with { Type = BillType.Payable });

        await fixture.Reader.Received(1).ReadAsync(
            fixture.Firm.Id, BillType.Payable, AsAt, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Running_the_report_without_a_firm_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);

        Result<OutstandingBillsResponse> result = await fixture.Handle(Query());

        result.Error.Code.ShouldBe("Report.NoFirmSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    private static GetOutstandingBillsQuery Query() =>
        new(BillType.Receivable, AsAt);

    /// <summary>A handler over a reader returning whatever the test registers.</summary>
    private sealed class Fixture
    {
        private readonly List<OutstandingBillRow> _rows = [];
        private readonly GetOutstandingBillsQueryHandler _handler;

        internal Fixture(bool firmSelected = true)
        {
            Firm = Domain.Tenancy.Firm.Create(
                TenantId.NewId(), "ACME", "Acme Trading", CurrencyCode.Qar,
                TaxRegime.GccVat, "Asia/Qatar").Value;

            Reader = Substitute.For<IOutstandingBillsReader>();
            Reader
                .ReadAsync(
                    Arg.Any<FirmId>(), Arg.Any<BillType>(), Arg.Any<DateOnly>(),
                    Arg.Any<LedgerId?>(), Arg.Any<CancellationToken>())
                .Returns(_ => _rows);

            IFirmRepository firms = Substitute.For<IFirmRepository>();
            firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

            ITenantContext tenant = Substitute.For<ITenantContext>();
            tenant.IsResolved.Returns(true);
            tenant.TenantId.Returns(Firm.TenantId);
            tenant.FirmId.Returns(firmSelected ? Firm.Id : null);

            _handler = new GetOutstandingBillsQueryHandler(Reader, firms, tenant);
        }

        internal Firm Firm { get; }

        internal IOutstandingBillsReader Reader { get; }

        /// <summary>Registers one open bill for a party, creating the party as needed.</summary>
        internal void Open(
            string ledgerCode,
            string ledgerName,
            string billNumber,
            decimal amount,
            DateOnly due,
            decimal settled = 0m,
            DateOnly? billDate = null,
            string currency = "QAR")
        {
            Guid ledgerId = _rows
                .FirstOrDefault(r => r.LedgerCode == ledgerCode)?.LedgerId
                ?? Guid.CreateVersion7();

            _rows.Add(new OutstandingBillRow(
                Guid.CreateVersion7(),
                ledgerId,
                ledgerCode,
                ledgerName,
                billNumber,
                billDate ?? due.AddDays(-30),
                due,
                amount,
                settled,
                currency));
        }

        internal Task<Result<OutstandingBillsResponse>> Handle(GetOutstandingBillsQuery query) =>
            _handler.Handle(query, TestContext.Current.CancellationToken);
    }
}
