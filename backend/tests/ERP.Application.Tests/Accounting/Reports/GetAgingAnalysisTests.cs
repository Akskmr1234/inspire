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
/// Tests for <see cref="GetAgingAnalysisQueryHandler"/>.
/// </summary>
/// <remarks>
/// The age-wise debtors and creditors reports. The bucket boundaries are the whole
/// substance of this report, so most of what is proved here is that a bill lands in
/// exactly one of them, that the edges belong where an accountant expects, and that
/// what is not yet due stays out of the overdue columns entirely.
/// </remarks>
public sealed class GetAgingAnalysisTests
{
    private static readonly DateOnly AsAt = new(2026, 6, 30);

    [Fact]
    public async Task Each_bill_lands_in_the_bucket_its_age_puts_it_in()
    {
        Fixture fixture = new();
        fixture.Open("1000", "Zenith", 100m, daysOverdue: 15);
        fixture.Open("1000", "Zenith", 200m, daysOverdue: 45);
        fixture.Open("1000", "Zenith", 300m, daysOverdue: 75);
        fixture.Open("1000", "Zenith", 400m, daysOverdue: 200);

        AgingAnalysisResponse report = (await fixture.Handle(Query())).Value;

        AgingRow row = report.Rows.ShouldHaveSingleItem();
        row.Buckets.ShouldBe([100m, 200m, 300m, 400m]);
        row.Total.ShouldBe(1_000m);
    }

    [Fact]
    public async Task A_bill_exactly_on_a_boundary_falls_in_the_lower_bucket()
    {
        // 30 days overdue is the last day of the 1-30 bucket, not the first day of
        // 31-60. Off by one here quietly moves money between columns an accountant
        // is reconciling against a printed statement.
        Fixture fixture = new();
        fixture.Open("1000", "Zenith", 100m, daysOverdue: 30);
        fixture.Open("1000", "Zenith", 200m, daysOverdue: 31);

        AgingAnalysisResponse report = (await fixture.Handle(Query())).Value;

        report.Rows[0].Buckets.ShouldBe([100m, 200m, 0m, 0m]);
    }

    [Fact]
    public async Task What_is_not_yet_due_is_kept_out_of_the_overdue_buckets()
    {
        // The point of the report. Folding current bills into the first bucket would
        // report a customer who has never paid late as 30 days overdue.
        Fixture fixture = new();
        fixture.Open("1000", "Zenith", 500m, daysOverdue: 0);
        fixture.Open("1000", "Zenith", 250m, daysOverdue: 10);

        AgingAnalysisResponse report = (await fixture.Handle(Query())).Value;

        report.Rows[0].NotDue.ShouldBe(500m);
        report.Rows[0].Buckets.ShouldBe([250m, 0m, 0m, 0m]);
        report.Rows[0].Total.ShouldBe(750m);
        report.TotalNotDue.ShouldBe(500m);
    }

    [Fact]
    public async Task The_column_totals_add_up_to_the_grand_total()
    {
        Fixture fixture = new();
        fixture.Open("1000", "Zenith", 100m, daysOverdue: 5);
        fixture.Open("2000", "Al Mansoor", 200m, daysOverdue: 50);
        fixture.Open("3000", "Gulf", 300m, daysOverdue: 0);

        AgingAnalysisResponse report = (await fixture.Handle(Query())).Value;

        report.BucketTotals.ShouldBe([100m, 200m, 0m, 0m]);
        report.TotalNotDue.ShouldBe(300m);
        report.Total.ShouldBe(600m);
        report.Total.ShouldBe(report.TotalNotDue + report.BucketTotals.Sum());
    }

    [Fact]
    public async Task The_default_buckets_are_thirty_sixty_ninety_and_over()
    {
        Fixture fixture = new();

        AgingAnalysisResponse report = (await fixture.Handle(Query())).Value;

        report.BucketLabels.ShouldBe(
            ["1-30 days", "31-60 days", "61-90 days", "Over 90 days"]);
    }

    [Fact]
    public async Task The_buckets_can_be_cut_wherever_the_firm_ages_on()
    {
        // Recorded in the specification as an assumption rather than a requirement,
        // so a firm that ages on fortnights must not need a deployment to say so.
        Fixture fixture = new();
        fixture.Open("1000", "Zenith", 100m, daysOverdue: 10);
        fixture.Open("1000", "Zenith", 200m, daysOverdue: 20);

        AgingAnalysisResponse report =
            (await fixture.Handle(Query() with { BucketDays = [14, 28] })).Value;

        report.BucketLabels.ShouldBe(["1-14 days", "15-28 days", "Over 28 days"]);
        report.Rows[0].Buckets.ShouldBe([100m, 200m, 0m]);
    }

    [Fact]
    public async Task Rows_come_out_in_ledger_code_order()
    {
        Fixture fixture = new();
        fixture.Open("3000", "Gulf", 100m, daysOverdue: 5);
        fixture.Open("1000", "Zenith", 100m, daysOverdue: 5);
        fixture.Open("2000", "Al Mansoor", 100m, daysOverdue: 5);

        AgingAnalysisResponse report = (await fixture.Handle(Query())).Value;

        report.Rows.Select(r => r.LedgerCode).ShouldBe(["1000", "2000", "3000"]);
    }

    [Fact]
    public async Task An_empty_ledger_produces_an_empty_report_rather_than_a_failure()
    {
        Fixture fixture = new();

        AgingAnalysisResponse report = (await fixture.Handle(Query())).Value;

        report.Rows.ShouldBeEmpty();
        report.Total.ShouldBe(0m);
        report.BucketTotals.ShouldBe([0m, 0m, 0m, 0m]);
    }

    [Fact]
    public async Task Running_the_report_without_a_firm_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);

        Result<AgingAnalysisResponse> result = await fixture.Handle(Query());

        result.Error.Code.ShouldBe("Report.NoFirmSelected");
    }

    // ------------------------------------------------------------ validation

    [Theory]
    [InlineData(new[] { 60, 30 })]
    [InlineData(new[] { 30, 30 })]
    public void Buckets_that_do_not_ascend_are_rejected(int[] boundaries)
    {
        // Overlapping buckets would count one bill twice and make the row totals
        // disagree with the outstanding report they are meant to break down.
        GetAgingAnalysisQueryValidator validator = new();

        validator
            .Validate(new GetAgingAnalysisQuery(BillType.Receivable, AsAt, boundaries))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_bucket_boundary_of_zero_days_is_rejected()
    {
        GetAgingAnalysisQueryValidator validator = new();

        validator
            .Validate(new GetAgingAnalysisQuery(BillType.Receivable, AsAt, [0, 30]))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Omitting_the_buckets_altogether_is_valid()
    {
        GetAgingAnalysisQueryValidator validator = new();

        validator
            .Validate(new GetAgingAnalysisQuery(BillType.Receivable, AsAt))
            .IsValid.ShouldBeTrue();
    }

    private static GetAgingAnalysisQuery Query() => new(BillType.Receivable, AsAt);

    /// <summary>A handler over a reader returning whatever the test registers.</summary>
    private sealed class Fixture
    {
        private readonly List<OutstandingBillRow> _rows = [];
        private readonly GetAgingAnalysisQueryHandler _handler;

        internal Fixture(bool firmSelected = true)
        {
            Firm = Domain.Tenancy.Firm.Create(
                TenantId.NewId(), "ACME", "Acme Trading", CurrencyCode.Qar,
                TaxRegime.GccVat, "Asia/Qatar").Value;

            IOutstandingBillsReader reader = Substitute.For<IOutstandingBillsReader>();
            reader
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

            _handler = new GetAgingAnalysisQueryHandler(reader, firms, tenant);
        }

        internal Firm Firm { get; }

        /// <summary>Registers one open bill at a given age, creating the party as needed.</summary>
        internal void Open(
            string ledgerCode,
            string ledgerName,
            decimal outstanding,
            int daysOverdue)
        {
            Guid ledgerId = _rows
                .FirstOrDefault(r => r.LedgerCode == ledgerCode)?.LedgerId
                ?? Guid.CreateVersion7();

            // Not yet due is expressed as a due date after the reporting date, so
            // the handler derives the age exactly as it would from real data.
            DateOnly due = daysOverdue > 0
                ? AsAt.AddDays(-daysOverdue)
                : AsAt.AddDays(10);

            _rows.Add(new OutstandingBillRow(
                Guid.CreateVersion7(),
                ledgerId,
                ledgerCode,
                ledgerName,
                $"INV-{_rows.Count + 1:D3}",
                due.AddDays(-30),
                due,
                outstanding,
                0m,
                "QAR"));
        }

        internal Task<Result<AgingAnalysisResponse>> Handle(GetAgingAnalysisQuery query) =>
            _handler.Handle(query, TestContext.Current.CancellationToken);
    }
}
