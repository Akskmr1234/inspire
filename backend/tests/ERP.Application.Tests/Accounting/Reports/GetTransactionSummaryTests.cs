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
/// Tests for <see cref="GetTransactionSummaryQueryHandler"/>.
/// </summary>
/// <remarks>
/// The reader hands back cells cut by type, status and month; the handler's whole job
/// is to pivot them into the two views the report presents. What matters is that both
/// views are built from the one set of cells and therefore agree - a summary whose
/// by-type total differed from its by-month total would be entirely wrong, since
/// totals are all it contains.
/// </remarks>
public sealed class GetTransactionSummaryTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    [Fact]
    public async Task Cells_of_the_same_type_are_rolled_up_across_months()
    {
        Fixture fixture = new();
        fixture.Cell(VoucherType.Journal, VoucherStatus.Posted, 2026, 1, 3, 300m);
        fixture.Cell(VoucherType.Journal, VoucherStatus.Posted, 2026, 2, 2, 200m);

        TransactionSummaryResponse report = (await fixture.Handle()).Value;

        TransactionSummaryType type = report.Types.ShouldHaveSingleItem();
        type.Type.ShouldBe(VoucherType.Journal);
        type.VoucherCount.ShouldBe(5);
        type.TotalAmount.ShouldBe(500m);
    }

    [Fact]
    public async Task Cells_of_the_same_month_are_rolled_up_across_types()
    {
        Fixture fixture = new();
        fixture.Cell(VoucherType.Journal, VoucherStatus.Posted, 2026, 1, 3, 300m);
        fixture.Cell(VoucherType.BankReceipt, VoucherStatus.Posted, 2026, 1, 1, 150m);

        TransactionSummaryResponse report = (await fixture.Handle()).Value;

        TransactionSummaryMonth month = report.Months.ShouldHaveSingleItem();
        month.Year.ShouldBe(2026);
        month.Month.ShouldBe(1);
        month.VoucherCount.ShouldBe(4);
        month.TotalAmount.ShouldBe(450m);
    }

    [Fact]
    public async Task The_two_views_total_to_the_same_figure()
    {
        // Both are pivots of the one set of cells, and a report made only of totals
        // has nothing left if its own two views disagree.
        Fixture fixture = new();
        fixture.Cell(VoucherType.Journal, VoucherStatus.Posted, 2026, 1, 3, 300m);
        fixture.Cell(VoucherType.BankReceipt, VoucherStatus.Draft, 2026, 2, 1, 150m);
        fixture.Cell(VoucherType.CashPayment, VoucherStatus.Cancelled, 2026, 3, 2, 75m);

        TransactionSummaryResponse report = (await fixture.Handle()).Value;

        report.Types.Sum(t => t.TotalAmount).ShouldBe(report.TotalAmount);
        report.Months.Sum(m => m.TotalAmount).ShouldBe(report.TotalAmount);
        report.Types.Sum(t => t.VoucherCount).ShouldBe(report.VoucherCount);
        report.Months.Sum(m => m.VoucherCount).ShouldBe(report.VoucherCount);
        report.TotalAmount.ShouldBe(525m);
        report.VoucherCount.ShouldBe(6);
    }

    [Fact]
    public async Task Months_come_out_oldest_first_across_a_year_boundary()
    {
        Fixture fixture = new();
        fixture.Cell(VoucherType.Journal, VoucherStatus.Posted, 2027, 1, 1, 10m);
        fixture.Cell(VoucherType.Journal, VoucherStatus.Posted, 2026, 11, 1, 20m);
        fixture.Cell(VoucherType.Journal, VoucherStatus.Posted, 2026, 12, 1, 30m);

        TransactionSummaryResponse report = (await fixture.Handle()).Value;

        report.Months.Select(m => (m.Year, m.Month))
            .ShouldBe([(2026, 11), (2026, 12), (2027, 1)]);
    }

    [Fact]
    public async Task A_types_statuses_are_counted_separately_from_its_total()
    {
        Fixture fixture = new();
        fixture.Cell(VoucherType.Journal, VoucherStatus.Posted, 2026, 1, 4, 400m);
        fixture.Cell(VoucherType.Journal, VoucherStatus.Draft, 2026, 1, 2, 0m);
        fixture.Cell(VoucherType.Journal, VoucherStatus.Draft, 2026, 2, 1, 50m);

        TransactionSummaryResponse report = (await fixture.Handle()).Value;

        TransactionSummaryType type = report.Types.ShouldHaveSingleItem();
        type.CountByStatus[VoucherStatus.Posted].ShouldBe(4);
        type.CountByStatus[VoucherStatus.Draft].ShouldBe(3);
        report.CountByStatus[VoucherStatus.Draft].ShouldBe(3);
    }

    [Fact]
    public async Task An_empty_period_reports_nothing_rather_than_failing()
    {
        Fixture fixture = new();

        TransactionSummaryResponse report = (await fixture.Handle()).Value;

        report.Types.ShouldBeEmpty();
        report.Months.ShouldBeEmpty();
        report.VoucherCount.ShouldBe(0);
        report.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task The_status_filter_reaches_the_reader()
    {
        Fixture fixture = new();

        await fixture.Handle(new GetTransactionSummaryQuery(From, To, VoucherStatus.Draft));

        await fixture.Reader.Received(1).ReadAsync(
            fixture.Firm.Id, From, To, VoucherStatus.Draft, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Running_the_report_without_a_firm_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);

        Result<TransactionSummaryResponse> result = await fixture.Handle();

        result.Error.Code.ShouldBe("Report.NoFirmSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    /// <summary>A handler over a reader returning whatever cells a test registers.</summary>
    private sealed class Fixture
    {
        private readonly List<TransactionSummaryBucket> _cells = [];
        private readonly GetTransactionSummaryQueryHandler _handler;

        internal Fixture(bool firmSelected = true)
        {
            Firm = Domain.Tenancy.Firm.Create(
                TenantId.NewId(), "ACME", "Acme Trading", CurrencyCode.Qar,
                TaxRegime.GccVat, "Asia/Qatar").Value;

            Reader = Substitute.For<ITransactionSummaryReader>();
            Reader
                .ReadAsync(
                    Arg.Any<FirmId>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                    Arg.Any<VoucherStatus?>(), Arg.Any<CancellationToken>())
                .Returns(_ => _cells);

            IFirmRepository firms = Substitute.For<IFirmRepository>();
            firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

            ITenantContext tenant = Substitute.For<ITenantContext>();
            tenant.IsResolved.Returns(true);
            tenant.TenantId.Returns(Firm.TenantId);
            tenant.FirmId.Returns(firmSelected ? Firm.Id : null);

            _handler = new GetTransactionSummaryQueryHandler(Reader, firms, tenant);
        }

        internal Firm Firm { get; }

        internal ITransactionSummaryReader Reader { get; }

        /// <summary>Registers one aggregated cell.</summary>
        internal void Cell(
            VoucherType type,
            VoucherStatus status,
            int year,
            int month,
            int count,
            decimal total) =>
            _cells.Add(new TransactionSummaryBucket(type, status, year, month, count, total));

        internal Task<Result<TransactionSummaryResponse>> Handle(
            GetTransactionSummaryQuery? query = null) =>
            _handler.Handle(
                query ?? new GetTransactionSummaryQuery(From, To),
                TestContext.Current.CancellationToken);
    }
}
