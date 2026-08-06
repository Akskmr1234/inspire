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
/// Tests for the three cheque reports: the post-dated cheque report, the PDC
/// calendar, and the cheque register.
/// </summary>
/// <remarks>
/// The reader is substituted, so what these prove is not that the query runs but that
/// the handler shapes the answer the way each report is read: the PDC report soonest
/// due first with the days still to run, the calendar grouped by the day cheques land
/// with a net a treasurer can act on, and the register newest first with a tally of
/// where every cheque stands. The bank a line shows - the firm's own account, or the
/// payer's bank when there is no account yet - is settled here too.
/// </remarks>
public sealed class GetChequeReportsTests
{
    private static readonly DateOnly AsAt = new(2026, 8, 6);

    [Fact]
    public async Task Post_dated_cheques_are_listed_soonest_due_first()
    {
        // A treasurer reads the one maturing next first, so that is what leads.
        Fixture fixture = new();
        fixture.Add("CHQ-LATER", ChequeDirection.Received, AsAt.AddDays(30), 300m);
        fixture.Add("CHQ-SOONER", ChequeDirection.Received, AsAt.AddDays(5), 100m);
        fixture.Add("CHQ-MIDDLE", ChequeDirection.Received, AsAt.AddDays(15), 200m);

        PostDatedChequesResponse report = (await fixture.HandlePostDated()).Value;

        report.Cheques
            .Select(c => c.ChequeNumber)
            .ShouldBe(["CHQ-SOONER", "CHQ-MIDDLE", "CHQ-LATER"]);
    }

    [Fact]
    public async Task The_days_still_to_run_are_counted_from_the_reporting_date()
    {
        Fixture fixture = new();
        fixture.Add("CHQ-1", ChequeDirection.Received, AsAt.AddDays(12), 500m);

        PostDatedChequesResponse report = (await fixture.HandlePostDated()).Value;

        report.Cheques.ShouldHaveSingleItem().DaysUntilDue.ShouldBe(12);
    }

    [Fact]
    public async Task Receivable_and_payable_are_totalled_by_direction()
    {
        Fixture fixture = new();
        fixture.Add("IN-1", ChequeDirection.Received, AsAt.AddDays(10), 300m);
        fixture.Add("IN-2", ChequeDirection.Received, AsAt.AddDays(20), 200m);
        fixture.Add("OUT-1", ChequeDirection.Issued, AsAt.AddDays(15), 400m, bankAccountName: "HSBC Current");

        PostDatedChequesResponse report = (await fixture.HandlePostDated()).Value;

        report.TotalReceivable.ShouldBe(500m);
        report.TotalPayable.ShouldBe(400m);
    }

    [Fact]
    public async Task A_line_shows_the_firms_account_when_it_has_one_and_the_payers_bank_otherwise()
    {
        // An issued cheque is drawn on a known account; a received cheque in hand has
        // none yet, and the only bank to show is the one printed on its face.
        Fixture fixture = new();
        fixture.Add(
            "OUT-1", ChequeDirection.Issued, AsAt.AddDays(10), 400m,
            bankAccountName: "HSBC Current", drawnOnBank: "ignored when an account is known");
        fixture.Add(
            "IN-1", ChequeDirection.Received, AsAt.AddDays(20), 300m,
            drawnOnBank: "Doha Bank");

        PostDatedChequesResponse report = (await fixture.HandlePostDated()).Value;

        report.Cheques.Single(c => c.ChequeNumber == "OUT-1").BankName.ShouldBe("HSBC Current");
        report.Cheques.Single(c => c.ChequeNumber == "IN-1").BankName.ShouldBe("Doha Bank");
    }

    [Fact]
    public async Task The_currencies_in_play_are_reported_alongside_the_totals()
    {
        // Receivable and payable summed across currencies are not figures to print
        // blind; naming the currencies lets the caller refuse to.
        Fixture fixture = new();
        fixture.Add("QAR-1", ChequeDirection.Received, AsAt.AddDays(10), 100m);
        fixture.Add("USD-1", ChequeDirection.Received, AsAt.AddDays(20), 200m, currency: "USD");

        PostDatedChequesResponse report = (await fixture.HandlePostDated()).Value;

        report.Currencies.ShouldBe(["QAR", "USD"]);
    }

    [Fact]
    public async Task The_pdc_report_asks_only_for_pending_cheques_dated_after_the_reporting_date()
    {
        // The defining filter: a post-dated cheque is one still in hand whose date has
        // not arrived. Anything already banked, or dated on or before today, is not
        // what this report is for.
        Fixture fixture = new();

        await fixture.HandlePostDated(new GetPostDatedChequesQuery(
            AsAt, ChequeDirection.Received, fixture.PartyId("2000")));

        await fixture.Reader.Received(1).ReadAsync(
            Arg.Is<ChequeReportCriteria>(c =>
                c != null
                && c.FirmId == fixture.Firm.Id
                && c.From == AsAt.AddDays(1)
                && c.To == DateOnly.MaxValue
                && c.ByInstrumentDate
                && c.Status == ChequeStatus.Pending
                && c.OpenOnly
                && c.Direction == ChequeDirection.Received
                && c.LedgerId == LedgerId.From(fixture.PartyId("2000"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Running_the_pdc_report_without_a_firm_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);

        Result<PostDatedChequesResponse> result = await fixture.HandlePostDated();

        result.Error.Code.ShouldBe("Report.NoFirmSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task The_calendar_groups_cheques_by_the_day_they_fall_due()
    {
        Fixture fixture = new();
        fixture.Add("A", ChequeDirection.Received, new DateOnly(2026, 9, 10), 100m);
        fixture.Add("B", ChequeDirection.Received, new DateOnly(2026, 9, 10), 150m);
        fixture.Add("C", ChequeDirection.Received, new DateOnly(2026, 9, 25), 200m);

        ChequeCalendarResponse report = (await fixture.HandleCalendar()).Value;

        report.Days.Select(d => d.Date).ShouldBe(
            [new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 25)]);
        report.Days[0].Cheques.Count.ShouldBe(2);
        report.Days[0].Receivable.ShouldBe(250m);
        report.Days[1].Receivable.ShouldBe(200m);
    }

    [Fact]
    public async Task A_calendar_days_net_is_receivable_less_payable()
    {
        // The figure a treasurer actually reads: what the day does to the position,
        // not the two gross columns behind it.
        Fixture fixture = new();
        fixture.Add("IN", ChequeDirection.Received, new DateOnly(2026, 9, 10), 500m);
        fixture.Add(
            "OUT", ChequeDirection.Issued, new DateOnly(2026, 9, 10), 300m,
            bankAccountName: "HSBC Current");

        ChequeCalendarResponse report = (await fixture.HandleCalendar()).Value;

        ChequeCalendarDay day = report.Days.ShouldHaveSingleItem();
        day.Receivable.ShouldBe(500m);
        day.Payable.ShouldBe(300m);
        day.Net.ShouldBe(200m);
        report.TotalReceivable.ShouldBe(500m);
        report.TotalPayable.ShouldBe(300m);
    }

    [Fact]
    public async Task The_calendar_asks_for_open_cheques_by_their_face_date_over_the_range()
    {
        Fixture fixture = new();
        var query = new GetChequeCalendarQuery(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), ChequeDirection.Issued);

        await fixture.HandleCalendar(query);

        await fixture.Reader.Received(1).ReadAsync(
            Arg.Is<ChequeReportCriteria>(c =>
                c != null
                && c.FirmId == fixture.Firm.Id
                && c.From == query.From
                && c.To == query.To
                && c.ByInstrumentDate
                && c.Status == null
                && c.OpenOnly
                && c.Direction == ChequeDirection.Issued),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_register_lists_the_most_recently_taken_in_first()
    {
        // A register is usually opened to find something recent, so it pages from the
        // newest end.
        Fixture fixture = new();
        fixture.Add(
            "OLD", ChequeDirection.Received, new DateOnly(2026, 7, 1), 100m,
            recordedOn: new DateOnly(2026, 7, 1));
        fixture.Add(
            "NEW", ChequeDirection.Received, new DateOnly(2026, 8, 1), 200m,
            recordedOn: new DateOnly(2026, 8, 1));
        fixture.Add(
            "MID", ChequeDirection.Received, new DateOnly(2026, 7, 15), 150m,
            recordedOn: new DateOnly(2026, 7, 15));

        ChequeRegisterResponse report = (await fixture.HandleRegister()).Value;

        report.Cheques.Select(c => c.ChequeNumber).ShouldBe(["NEW", "MID", "OLD"]);
    }

    [Fact]
    public async Task The_register_totals_what_was_taken_in_and_written_out()
    {
        Fixture fixture = new();
        fixture.Add("IN-1", ChequeDirection.Received, AsAt, 300m);
        fixture.Add("IN-2", ChequeDirection.Received, AsAt, 200m);
        fixture.Add("OUT-1", ChequeDirection.Issued, AsAt, 400m, bankAccountName: "HSBC Current");

        ChequeRegisterResponse report = (await fixture.HandleRegister()).Value;

        report.TotalReceived.ShouldBe(500m);
        report.TotalIssued.ShouldBe(400m);
    }

    [Fact]
    public async Task The_register_counts_how_many_cheques_stand_in_each_status()
    {
        Fixture fixture = new();
        fixture.Add("P-1", ChequeDirection.Received, AsAt, 100m, status: ChequeStatus.Pending);
        fixture.Add("P-2", ChequeDirection.Received, AsAt, 100m, status: ChequeStatus.Pending);
        fixture.Add(
            "C-1", ChequeDirection.Received, AsAt, 100m, status: ChequeStatus.Cleared,
            closedOn: AsAt);
        fixture.Add(
            "B-1", ChequeDirection.Received, AsAt, 100m, status: ChequeStatus.Bounced,
            closedOn: AsAt, closureReason: "Insufficient funds");

        ChequeRegisterResponse report = (await fixture.HandleRegister()).Value;

        report.CountByStatus[ChequeStatus.Pending].ShouldBe(2);
        report.CountByStatus[ChequeStatus.Cleared].ShouldBe(1);
        report.CountByStatus[ChequeStatus.Bounced].ShouldBe(1);
        report.CountByStatus.ContainsKey(ChequeStatus.Stopped).ShouldBeFalse();
    }

    [Fact]
    public async Task A_closed_cheque_carries_its_outcome_onto_the_register()
    {
        Fixture fixture = new();
        fixture.Add(
            "B-1", ChequeDirection.Received, AsAt, 100m, status: ChequeStatus.Bounced,
            closedOn: new DateOnly(2026, 8, 4), closureReason: "Insufficient funds");

        ChequeRegisterResponse report = (await fixture.HandleRegister()).Value;

        ChequeReportLine line = report.Cheques.ShouldHaveSingleItem();
        line.Status.ShouldBe(ChequeStatus.Bounced);
        line.ClosedOn.ShouldBe(new DateOnly(2026, 8, 4));
        line.ClosureReason.ShouldBe("Insufficient funds");
    }

    [Fact]
    public async Task The_register_reads_by_the_recorded_date_and_shows_closed_cheques_too()
    {
        Fixture fixture = new();
        var query = new GetChequeRegisterQuery(
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31),
            ChequeDirection.Received, ChequeStatus.Cleared, fixture.PartyId("2000"));

        await fixture.HandleRegister(query);

        await fixture.Reader.Received(1).ReadAsync(
            Arg.Is<ChequeReportCriteria>(c =>
                c != null
                && c.FirmId == fixture.Firm.Id
                && c.From == query.From
                && c.To == query.To
                && !c.ByInstrumentDate
                && !c.OpenOnly
                && c.Direction == ChequeDirection.Received
                && c.Status == ChequeStatus.Cleared
                && c.LedgerId == LedgerId.From(query.LedgerId!.Value)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The three handlers over one substitute reader that returns whatever rows a
    /// test registers.
    /// </summary>
    private sealed class Fixture
    {
        private readonly List<ChequeReportRow> _rows = [];
        private readonly Dictionary<string, Guid> _partyIds = new(StringComparer.Ordinal);
        private readonly GetPostDatedChequesQueryHandler _postDated;
        private readonly GetChequeCalendarQueryHandler _calendar;
        private readonly GetChequeRegisterQueryHandler _register;

        internal Fixture(bool firmSelected = true)
        {
            Firm = Domain.Tenancy.Firm.Create(
                TenantId.NewId(), "ACME", "Acme Trading", CurrencyCode.Qar,
                TaxRegime.GccVat, "Asia/Qatar").Value;

            Reader = Substitute.For<IChequeReportReader>();
            Reader
                .ReadAsync(Arg.Any<ChequeReportCriteria>(), Arg.Any<CancellationToken>())
                .Returns(_ => _rows);

            IFirmRepository firms = Substitute.For<IFirmRepository>();
            firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

            ITenantContext tenant = Substitute.For<ITenantContext>();
            tenant.IsResolved.Returns(true);
            tenant.TenantId.Returns(Firm.TenantId);
            tenant.FirmId.Returns(firmSelected ? Firm.Id : null);

            _postDated = new GetPostDatedChequesQueryHandler(Reader, firms, tenant);
            _calendar = new GetChequeCalendarQueryHandler(Reader, firms, tenant);
            _register = new GetChequeRegisterQueryHandler(Reader, firms, tenant);
        }

        internal Firm Firm { get; }

        internal IChequeReportReader Reader { get; }

        /// <summary>The stable ledger id a party code stands for.</summary>
        /// <param name="partyCode">The ledger code.</param>
        /// <returns>The id, created on first mention.</returns>
        internal Guid PartyId(string partyCode)
        {
            if (!_partyIds.TryGetValue(partyCode, out Guid id))
            {
                id = Guid.CreateVersion7();
                _partyIds[partyCode] = id;
            }

            return id;
        }

        /// <summary>Registers one cheque the reader will return.</summary>
        internal void Add(
            string chequeNumber,
            ChequeDirection direction,
            DateOnly instrumentDate,
            decimal amount,
            ChequeStatus status = ChequeStatus.Pending,
            string partyCode = "2000",
            string partyName = "Al Mansoor Trading",
            DateOnly? recordedOn = null,
            string currency = "QAR",
            string? bankAccountName = null,
            string? drawnOnBank = null,
            DateOnly? depositedOn = null,
            DateOnly? closedOn = null,
            string? closureReason = null) =>
            _rows.Add(new ChequeReportRow(
                Guid.CreateVersion7(),
                chequeNumber,
                direction,
                status,
                PartyId(partyCode),
                partyCode,
                partyName,
                instrumentDate,
                recordedOn ?? instrumentDate.AddDays(-30),
                amount,
                currency,
                bankAccountName,
                drawnOnBank,
                depositedOn,
                closedOn,
                closureReason));

        internal Task<Result<PostDatedChequesResponse>> HandlePostDated(
            GetPostDatedChequesQuery? query = null) =>
            _postDated.Handle(
                query ?? new GetPostDatedChequesQuery(AsAt),
                TestContext.Current.CancellationToken);

        internal Task<Result<ChequeCalendarResponse>> HandleCalendar(
            GetChequeCalendarQuery? query = null) =>
            _calendar.Handle(
                query ?? new GetChequeCalendarQuery(
                    new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
                TestContext.Current.CancellationToken);

        internal Task<Result<ChequeRegisterResponse>> HandleRegister(
            GetChequeRegisterQuery? query = null) =>
            _register.Handle(
                query ?? new GetChequeRegisterQuery(
                    new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31)),
                TestContext.Current.CancellationToken);
    }
}
