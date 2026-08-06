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
/// Tests for <see cref="GetAccountGroupSummaryQueryHandler"/>.
/// </summary>
/// <remarks>
/// The account group report is the trial balance summed a level up, over the same
/// reader, so what matters here is the rollup rather than the aggregation: that
/// ledgers land under the right group, that a group holding balances on both sides
/// shows both subtotals, that the groups come out in statement order, and that the
/// column totals still balance - because a group report that did not reconcile with
/// the trial balance it summarises would be worse than none.
/// </remarks>
public sealed class GetAccountGroupSummaryTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    [Fact]
    public async Task Ledgers_are_rolled_up_into_the_group_they_report_under()
    {
        Fixture fixture = new();
        fixture.Movement("SD", "Sundry Debtors", AccountNature.Asset, "2001", "Al Mansoor", 1_000m, periodCredit: 400m);
        fixture.Movement("SD", "Sundry Debtors", AccountNature.Asset, "2002", "Zenith", 500m, periodDebit: 200m);

        AccountGroupSummaryResponse report = (await fixture.Handle()).Value;

        AccountGroupSummaryRow group = report.Groups.ShouldHaveSingleItem();
        group.GroupCode.ShouldBe("SD");
        group.LedgerCount.ShouldBe(2);
        group.OpeningDebit.ShouldBe(1_500m);
        group.PeriodDebit.ShouldBe(200m);
        group.PeriodCredit.ShouldBe(400m);
        // 1000 - 400 = 600, and 500 + 200 = 700.
        group.ClosingDebit.ShouldBe(1_300m);
        group.Ledgers.Select(l => l.LedgerCode).ShouldBe(["2001", "2002"]);
    }

    [Fact]
    public async Task A_group_with_ledgers_on_both_sides_shows_a_debit_and_a_credit_subtotal()
    {
        // A Sundry Debtors group with one customer in credit is the ordinary case, and
        // netting it to one figure would hide a real receivable behind a real advance.
        Fixture fixture = new();
        fixture.Movement("SD", "Sundry Debtors", AccountNature.Asset, "2001", "Al Mansoor", 1_000m);
        fixture.Movement("SD", "Sundry Debtors", AccountNature.Asset, "2002", "Zenith", -300m);

        AccountGroupSummaryResponse report = (await fixture.Handle()).Value;

        AccountGroupSummaryRow group = report.Groups.ShouldHaveSingleItem();
        group.OpeningDebit.ShouldBe(1_000m);
        group.OpeningCredit.ShouldBe(300m);
    }

    [Fact]
    public async Task Groups_come_out_in_statement_order_not_code_order()
    {
        // Assets, then income, then expenses - the order a set of financial statements
        // reads. By code alone the expense group would lead; by nature it comes last.
        Fixture fixture = new();
        fixture.Movement("1000", "Direct Expenses", AccountNature.Expense, "5001", "Purchases", 100m);
        fixture.Movement("9000", "Fixed Assets", AccountNature.Asset, "1501", "Plant", 200m);
        fixture.Movement("4000", "Sales", AccountNature.Income, "4001", "Sales", -300m);

        AccountGroupSummaryResponse report = (await fixture.Handle()).Value;

        report.Groups.Select(g => g.GroupCode).ShouldBe(["9000", "4000", "1000"]);
        report.Groups.Select(g => g.Nature).ShouldBe(
            [AccountNature.Asset, AccountNature.Income, AccountNature.Expense]);
    }

    [Fact]
    public async Task A_group_whose_every_ledger_is_dormant_is_dropped()
    {
        Fixture fixture = new();
        fixture.Movement("SD", "Sundry Debtors", AccountNature.Asset, "2001", "Dormant", 0m);

        (await fixture.Handle()).Value.Groups.ShouldBeEmpty();

        AccountGroupSummaryResponse withZeros =
            (await fixture.Handle(Query() with { IncludeZeroBalances = true })).Value;

        AccountGroupSummaryRow group = withZeros.Groups.ShouldHaveSingleItem();
        group.LedgerCount.ShouldBe(1);
        group.Ledgers.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Group_totals_only_omits_the_ledger_detail_but_keeps_the_subtotal()
    {
        Fixture fixture = new();
        fixture.Movement("SD", "Sundry Debtors", AccountNature.Asset, "2001", "Al Mansoor", 1_000m);
        fixture.Movement("SD", "Sundry Debtors", AccountNature.Asset, "2002", "Zenith", 500m);

        AccountGroupSummaryResponse report =
            (await fixture.Handle(Query() with { IncludeLedgers = false })).Value;

        AccountGroupSummaryRow group = report.Groups.ShouldHaveSingleItem();
        group.OpeningDebit.ShouldBe(1_500m);
        group.LedgerCount.ShouldBe(2);
        group.Ledgers.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_columns_balance_and_reconcile_when_the_postings_do()
    {
        // The point of the report, inherited from the trial balance: the group
        // subtotals must add back to equal debit and credit grand totals.
        Fixture fixture = new();
        fixture.Movement("FA", "Fixed Assets", AccountNature.Asset, "1501", "Plant", 1_000m, periodDebit: 500m);
        fixture.Movement("CAP", "Capital", AccountNature.Liability, "3001", "Owner", -1_000m, periodCredit: 500m);

        AccountGroupSummaryResponse report = (await fixture.Handle()).Value;

        report.IsBalanced.ShouldBeTrue();
        report.TotalOpeningDebit.ShouldBe(1_000m);
        report.TotalOpeningCredit.ShouldBe(1_000m);
        report.TotalPeriodDebit.ShouldBe(500m);
        report.TotalPeriodCredit.ShouldBe(500m);
        report.TotalClosingDebit.ShouldBe(1_500m);
        report.TotalClosingCredit.ShouldBe(1_500m);
    }

    [Fact]
    public async Task An_out_of_balance_set_is_reported_rather_than_hidden()
    {
        // If the books are broken the report must say so, the way the trial balance
        // does, rather than print two totals that do not agree.
        Fixture fixture = new();
        fixture.Movement("FA", "Fixed Assets", AccountNature.Asset, "1501", "Plant", 1_000m);

        AccountGroupSummaryResponse report = (await fixture.Handle()).Value;

        report.IsBalanced.ShouldBeFalse();
        report.TotalOpeningDebit.ShouldBe(1_000m);
        report.TotalOpeningCredit.ShouldBe(0m);
    }

    [Fact]
    public async Task Running_the_report_without_a_firm_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);

        Result<AccountGroupSummaryResponse> result = await fixture.Handle();

        result.Error.Code.ShouldBe("Report.NoFirmSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    private static GetAccountGroupSummaryQuery Query() => new(From, To);

    /// <summary>A handler over a reader returning whatever movements a test registers.</summary>
    private sealed class Fixture
    {
        private readonly List<LedgerMovement> _movements = [];
        private readonly GetAccountGroupSummaryQueryHandler _handler;

        internal Fixture(bool firmSelected = true)
        {
            Firm = Domain.Tenancy.Firm.Create(
                TenantId.NewId(), "ACME", "Acme Trading", CurrencyCode.Qar,
                TaxRegime.GccVat, "Asia/Qatar").Value;

            Reader = Substitute.For<ITrialBalanceReader>();
            Reader
                .GetMovementsAsync(
                    Arg.Any<FirmId>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => _movements);

            IFirmRepository firms = Substitute.For<IFirmRepository>();
            firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

            ITenantContext tenant = Substitute.For<ITenantContext>();
            tenant.IsResolved.Returns(true);
            tenant.TenantId.Returns(Firm.TenantId);
            tenant.FirmId.Returns(firmSelected ? Firm.Id : null);

            _handler = new GetAccountGroupSummaryQueryHandler(Reader, firms, tenant);
        }

        internal Firm Firm { get; }

        internal ITrialBalanceReader Reader { get; }

        /// <summary>Registers one ledger's opening position and movement.</summary>
        internal void Movement(
            string groupCode,
            string groupName,
            AccountNature nature,
            string ledgerCode,
            string ledgerName,
            decimal openingSigned,
            decimal periodDebit = 0m,
            decimal periodCredit = 0m) =>
            _movements.Add(new LedgerMovement(
                Guid.CreateVersion7(),
                ledgerCode,
                ledgerName,
                groupCode,
                groupName,
                nature,
                openingSigned,
                periodDebit,
                periodCredit));

        internal Task<Result<AccountGroupSummaryResponse>> Handle(
            GetAccountGroupSummaryQuery? query = null) =>
            _handler.Handle(query ?? Query(), TestContext.Current.CancellationToken);
    }
}
