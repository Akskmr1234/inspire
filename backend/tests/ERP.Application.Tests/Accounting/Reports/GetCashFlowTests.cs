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
/// Tests for <see cref="GetCashFlowQueryHandler"/> and
/// <see cref="CashFlowClassification"/>.
/// </summary>
/// <remarks>
/// The reader supplies movements; the handler decides which heading each falls under
/// and whether the result can be trusted. The classification rule gets tests of its
/// own because it is the one piece of judgement in the report, and the case that
/// matters most is the one where the obvious implementation is wrong: a debtor is an
/// asset and a creditor a liability, so classifying by nature alone would file
/// ordinary trading under investing and financing.
/// </remarks>
public sealed class GetCashFlowTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    [Theory]
    [InlineData(LedgerKind.Customer, AccountNature.Asset, CashFlowCategory.Operating)]
    [InlineData(LedgerKind.Supplier, AccountNature.Liability, CashFlowCategory.Operating)]
    [InlineData(LedgerKind.General, AccountNature.Income, CashFlowCategory.Operating)]
    [InlineData(LedgerKind.General, AccountNature.Expense, CashFlowCategory.Operating)]
    [InlineData(LedgerKind.General, AccountNature.Equity, CashFlowCategory.Financing)]
    [InlineData(LedgerKind.General, AccountNature.Liability, CashFlowCategory.Financing)]
    [InlineData(LedgerKind.General, AccountNature.Asset, CashFlowCategory.Investing)]
    public void Accounts_are_classified_by_what_they_represent_before_their_nature(
        LedgerKind kind,
        AccountNature nature,
        CashFlowCategory expected) =>
        CashFlowClassification.Classify(kind, nature).ShouldBe(expected);

    [Fact]
    public async Task All_three_headings_appear_even_when_one_is_empty()
    {
        // A statement with no investing section reads as though the report forgot to
        // look; one showing investing at nil says plainly that nothing was bought.
        Fixture fixture = new();
        fixture.Movement("4000", "Sales", LedgerKind.General, AccountNature.Income, inflow: 500m);

        CashFlowResponse report = (await fixture.Handle()).Value;

        report.Sections.Select(s => s.Category).ShouldBe(
            [CashFlowCategory.Operating, CashFlowCategory.Investing, CashFlowCategory.Financing]);
        report.Sections[1].Lines.ShouldBeEmpty();
        report.Sections[1].Net.ShouldBe(0m);
    }

    [Fact]
    public async Task Movements_are_totalled_within_their_heading()
    {
        Fixture fixture = new();
        fixture.Movement("4000", "Sales", LedgerKind.General, AccountNature.Income, inflow: 900m);
        fixture.Movement("5000", "Rent", LedgerKind.General, AccountNature.Expense, outflow: 300m);
        fixture.Movement("1500", "Plant", LedgerKind.General, AccountNature.Asset, outflow: 2_000m);

        CashFlowResponse report = (await fixture.Handle()).Value;

        CashFlowSection operating = report.Sections[0];
        operating.Inflow.ShouldBe(900m);
        operating.Outflow.ShouldBe(300m);
        operating.Net.ShouldBe(600m);

        report.Sections[1].Net.ShouldBe(-2_000m);
        report.NetChange.ShouldBe(-1_400m);
    }

    [Fact]
    public async Task The_largest_mover_leads_its_section()
    {
        // A reader scanning a section wants the account that explains most of it.
        Fixture fixture = new();
        fixture.Movement("4001", "Small", LedgerKind.General, AccountNature.Income, inflow: 10m);
        fixture.Movement("4002", "Large", LedgerKind.General, AccountNature.Income, inflow: 900m);
        fixture.Movement("4003", "Middle", LedgerKind.General, AccountNature.Expense, outflow: 400m);

        CashFlowResponse report = (await fixture.Handle()).Value;

        report.Sections[0].Lines.Select(l => l.LedgerName)
            .ShouldBe(["Large", "Middle", "Small"]);
    }

    [Fact]
    public async Task A_line_keeps_both_directions_and_states_its_net()
    {
        Fixture fixture = new();
        fixture.Movement(
            "2000", "Al Mansoor", LedgerKind.Customer, AccountNature.Asset,
            inflow: 900m, outflow: 300m);

        CashFlowLine line = (await fixture.Handle()).Value
            .Sections[0].Lines.ShouldHaveSingleItem();

        line.Inflow.ShouldBe(900m);
        line.Outflow.ShouldBe(300m);
        line.Net.ShouldBe(600m);
    }

    [Fact]
    public async Task The_statement_reconciles_when_the_movement_explains_the_change()
    {
        Fixture fixture = new(openingBalance: 1_000m, closingBalance: 1_600m);
        fixture.Movement("4000", "Sales", LedgerKind.General, AccountNature.Income, inflow: 900m);
        fixture.Movement("5000", "Rent", LedgerKind.General, AccountNature.Expense, outflow: 300m);

        CashFlowResponse report = (await fixture.Handle()).Value;

        report.NetChange.ShouldBe(600m);
        report.IsReconciled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_statement_that_does_not_account_for_the_change_says_so()
    {
        // The check that makes the report worth printing. If the sections do not add
        // back to the change in cash, something moved through the bank the statement
        // has not explained, and three plausible sections would be worse than none.
        Fixture fixture = new(openingBalance: 1_000m, closingBalance: 5_000m);
        fixture.Movement("4000", "Sales", LedgerKind.General, AccountNature.Income, inflow: 900m);

        CashFlowResponse report = (await fixture.Handle()).Value;

        report.IsReconciled.ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_period_still_produces_a_reconciled_statement()
    {
        Fixture fixture = new(openingBalance: 250m, closingBalance: 250m);

        CashFlowResponse report = (await fixture.Handle()).Value;

        report.Sections.Count.ShouldBe(3);
        report.NetChange.ShouldBe(0m);
        report.IsReconciled.ShouldBeTrue();
    }

    [Fact]
    public async Task Running_the_statement_without_a_firm_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);

        Result<CashFlowResponse> result = await fixture.Handle();

        result.Error.Code.ShouldBe("Report.NoFirmSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    /// <summary>A handler over a reader returning whatever movements a test registers.</summary>
    private sealed class Fixture
    {
        private readonly List<CashFlowMovement> _movements = [];

        private readonly GetCashFlowQueryHandler _handler;

        internal Fixture(
            bool firmSelected = true,
            decimal openingBalance = 0m,
            decimal closingBalance = 0m)
        {
            Firm = Domain.Tenancy.Firm.Create(
                TenantId.NewId(), "ACME", "Acme Trading", CurrencyCode.Qar,
                TaxRegime.GccVat, "Asia/Qatar").Value;

            ICashFlowReader reader = Substitute.For<ICashFlowReader>();
            reader
                .ReadAsync(
                    Arg.Any<FirmId>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => new CashFlowData(openingBalance, closingBalance, _movements));

            IFirmRepository firms = Substitute.For<IFirmRepository>();
            firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

            ITenantContext tenant = Substitute.For<ITenantContext>();
            tenant.IsResolved.Returns(true);
            tenant.TenantId.Returns(Firm.TenantId);
            tenant.FirmId.Returns(firmSelected ? Firm.Id : null);

            _handler = new GetCashFlowQueryHandler(reader, firms, tenant);
        }

        internal Firm Firm { get; }

        /// <summary>Registers one account's movement against cash.</summary>
        internal void Movement(
            string code,
            string name,
            LedgerKind kind,
            AccountNature nature,
            decimal inflow = 0m,
            decimal outflow = 0m) =>
            _movements.Add(new CashFlowMovement(
                Guid.CreateVersion7(), code, name, kind, nature, inflow, outflow));

        internal Task<Result<CashFlowResponse>> Handle() =>
            _handler.Handle(
                new GetCashFlowQuery(From, To), TestContext.Current.CancellationToken);
    }
}
