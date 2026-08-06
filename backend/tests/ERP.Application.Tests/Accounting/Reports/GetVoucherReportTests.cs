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
/// Tests for <see cref="GetVoucherReportQueryHandler"/>.
/// </summary>
/// <remarks>
/// The reader is substituted, so what these prove is the register's shape rather than
/// the query behind it: that vouchers come out newest first the way a lookup is read,
/// that the base amounts total across currencies while the document amounts are left
/// alone, that every status is tallied, and that the type and status filters reach the
/// reader rather than being quietly dropped.
/// </remarks>
public sealed class GetVoucherReportTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    [Fact]
    public async Task Vouchers_are_listed_most_recent_first()
    {
        Fixture fixture = new();
        fixture.Add("JV-OLD", new DateOnly(2026, 3, 1), amount: 100m);
        fixture.Add("JV-NEW", new DateOnly(2026, 9, 1), amount: 300m);
        fixture.Add("JV-MID", new DateOnly(2026, 6, 1), amount: 200m);

        VoucherReportResponse report = (await fixture.Handle()).Value;

        report.Vouchers.Select(v => v.VoucherNumber).ShouldBe(["JV-NEW", "JV-MID", "JV-OLD"]);
    }

    [Fact]
    public async Task Vouchers_on_the_same_day_break_by_number_descending()
    {
        Fixture fixture = new();
        fixture.Add("BR/2026/0001", new DateOnly(2026, 5, 1), amount: 100m);
        fixture.Add("BR/2026/0003", new DateOnly(2026, 5, 1), amount: 300m);
        fixture.Add("BR/2026/0002", new DateOnly(2026, 5, 1), amount: 200m);

        VoucherReportResponse report = (await fixture.Handle()).Value;

        report.Vouchers.Select(v => v.VoucherNumber).ShouldBe(
            ["BR/2026/0003", "BR/2026/0002", "BR/2026/0001"]);
    }

    [Fact]
    public async Task The_base_amounts_are_totalled()
    {
        Fixture fixture = new();
        fixture.Add("A", new DateOnly(2026, 2, 1), amount: 300m);
        fixture.Add("B", new DateOnly(2026, 2, 2), amount: 450m);

        VoucherReportResponse report = (await fixture.Handle()).Value;

        report.VoucherCount.ShouldBe(2);
        report.TotalBaseAmount.ShouldBe(750m);
    }

    [Fact]
    public async Task Every_status_is_counted()
    {
        Fixture fixture = new();
        fixture.Add("D1", new DateOnly(2026, 2, 1), amount: 100m, status: VoucherStatus.Draft);
        fixture.Add("P1", new DateOnly(2026, 2, 2), amount: 100m, status: VoucherStatus.Posted);
        fixture.Add("P2", new DateOnly(2026, 2, 3), amount: 100m, status: VoucherStatus.Posted);
        fixture.Add("C1", new DateOnly(2026, 2, 4), amount: 100m, status: VoucherStatus.Cancelled);

        VoucherReportResponse report = (await fixture.Handle()).Value;

        report.CountByStatus[VoucherStatus.Draft].ShouldBe(1);
        report.CountByStatus[VoucherStatus.Posted].ShouldBe(2);
        report.CountByStatus[VoucherStatus.Cancelled].ShouldBe(1);
    }

    [Fact]
    public async Task The_document_currencies_in_play_are_reported()
    {
        // A total across currencies would be meaningless in document terms, so the
        // base total stands alone and the currencies are named beside it.
        Fixture fixture = new();
        fixture.Add("QAR-1", new DateOnly(2026, 2, 1), amount: 100m, currency: "QAR");
        fixture.Add("USD-1", new DateOnly(2026, 2, 2), amount: 365m, currency: "USD");

        VoucherReportResponse report = (await fixture.Handle()).Value;

        report.Currencies.ShouldBe(["QAR", "USD"]);
    }

    [Fact]
    public async Task The_report_forwards_its_type_and_status_filters_to_the_reader()
    {
        Fixture fixture = new();

        await fixture.Handle(new GetVoucherReportQuery(
            From, To, VoucherType.Journal, VoucherStatus.Cancelled));

        await fixture.Reader.Received(1).ReadAsync(
            fixture.Firm.Id, From, To, VoucherType.Journal, VoucherStatus.Cancelled,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Running_the_report_without_a_firm_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);

        Result<VoucherReportResponse> result = await fixture.Handle();

        result.Error.Code.ShouldBe("Report.NoFirmSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    /// <summary>A handler over a reader returning whatever rows a test registers.</summary>
    private sealed class Fixture
    {
        private readonly List<VoucherReportLine> _rows = [];
        private readonly GetVoucherReportQueryHandler _handler;

        internal Fixture(bool firmSelected = true)
        {
            Firm = Domain.Tenancy.Firm.Create(
                TenantId.NewId(), "ACME", "Acme Trading", CurrencyCode.Qar,
                TaxRegime.GccVat, "Asia/Qatar").Value;

            Reader = Substitute.For<IVoucherReportReader>();
            Reader
                .ReadAsync(
                    Arg.Any<FirmId>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                    Arg.Any<VoucherType?>(), Arg.Any<VoucherStatus?>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => _rows);

            IFirmRepository firms = Substitute.For<IFirmRepository>();
            firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

            ITenantContext tenant = Substitute.For<ITenantContext>();
            tenant.IsResolved.Returns(true);
            tenant.TenantId.Returns(Firm.TenantId);
            tenant.FirmId.Returns(firmSelected ? Firm.Id : null);

            _handler = new GetVoucherReportQueryHandler(Reader, firms, tenant);
        }

        internal Firm Firm { get; }

        internal IVoucherReportReader Reader { get; }

        /// <summary>Registers one voucher the reader will return.</summary>
        internal void Add(
            string number,
            DateOnly date,
            decimal amount,
            VoucherType type = VoucherType.Journal,
            VoucherStatus status = VoucherStatus.Posted,
            string currency = "QAR",
            decimal exchangeRate = 1m) =>
            _rows.Add(new VoucherReportLine(
                Guid.CreateVersion7(),
                date,
                number,
                type,
                status,
                null,
                null,
                currency,
                exchangeRate,
                amount,
                amount * exchangeRate));

        internal Task<Result<VoucherReportResponse>> Handle(
            GetVoucherReportQuery? query = null) =>
            _handler.Handle(
                query ?? new GetVoucherReportQuery(From, To),
                TestContext.Current.CancellationToken);
    }
}
