using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Reporting;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="CashFlowReader"/> against a real PostgreSQL instance.
/// </summary>
/// <remarks>
/// <para>
/// The direct method rests on one claim: that the non-cash lines of a cash-touching
/// voucher account for exactly the movement in cash. These tests are mostly an attempt
/// to break that claim on the cases where it looks least likely to hold - a transfer
/// between the firm's own accounts, and a transfer carrying a bank charge - because if
/// it survives those it survives the ordinary receipt and payment trivially.
/// </para>
/// <para>
/// The reconciliation check is asserted throughout rather than in one test of its own.
/// It is the property that makes the statement worth printing, and a report that
/// reconciled on simple data and quietly stopped on real data would be worse than one
/// that never reconciled at all.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CashFlowReaderTests
{
    private static readonly DateOnly YearStart = new(2026, 1, 1);
    private static readonly DateOnly YearEnd = new(2026, 12, 31);
    private static readonly DateOnly PostedOn = new(2026, 6, 1);

    private readonly PostgresFixture _fixture;

    public CashFlowReaderTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_cash_sale_is_an_inflow_against_the_income_account()
    {
        Books books = await Books.CreateAsync(_fixture);

        // Debit cash, credit sales: money in.
        await books.PostAsync("CR-1", PostedOn, (books.Cash, EntrySide.Debit, 1_000m),
            (books.Sales, EntrySide.Credit, 1_000m));

        CashFlowData data = await books.ReadAsync();

        CashFlowMovement movement = data.Movements.ShouldHaveSingleItem();
        movement.LedgerCode.ShouldBe("4000");
        movement.Inflow.ShouldBe(1_000m);
        movement.Outflow.ShouldBe(0m);
        movement.Nature.ShouldBe(AccountNature.Income);

        data.ClosingBalance.ShouldBe(1_000m);
        Reconciles(data).ShouldBeTrue();
    }

    [Fact]
    public async Task Paying_an_expense_is_an_outflow()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.PostAsync("PY-1", PostedOn, (books.Rent, EntrySide.Debit, 400m),
            (books.Bank, EntrySide.Credit, 400m));

        CashFlowData data = await books.ReadAsync();

        CashFlowMovement movement = data.Movements.ShouldHaveSingleItem();
        movement.LedgerCode.ShouldBe("5000");
        movement.Outflow.ShouldBe(400m);
        movement.Inflow.ShouldBe(0m);

        data.ClosingBalance.ShouldBe(-400m);
        Reconciles(data).ShouldBeTrue();
    }

    [Fact]
    public async Task A_transfer_between_the_firms_own_accounts_contributes_nothing()
    {
        // The case the method has to get right without being told about it. Moving
        // money from the till to the bank does not change what the firm holds, and a
        // statement reporting it as activity would overstate both sides.
        Books books = await Books.CreateAsync(_fixture);

        await books.PostAsync("CN-1", PostedOn, (books.Bank, EntrySide.Debit, 5_000m),
            (books.Cash, EntrySide.Credit, 5_000m));

        CashFlowData data = await books.ReadAsync();

        data.Movements.ShouldBeEmpty();
        data.ClosingBalance.ShouldBe(data.OpeningBalance);
        Reconciles(data).ShouldBeTrue();
    }

    [Fact]
    public async Task A_transfer_carrying_a_charge_contributes_exactly_the_charge()
    {
        // Debit bank 990, debit charges 10, credit cash 1,000. The firm is ten poorer,
        // and the only thing that left is the charge.
        Books books = await Books.CreateAsync(_fixture);

        await books.PostAsync("CN-2", PostedOn,
            (books.Bank, EntrySide.Debit, 990m),
            (books.Rent, EntrySide.Debit, 10m),
            (books.Cash, EntrySide.Credit, 1_000m));

        CashFlowData data = await books.ReadAsync();

        CashFlowMovement movement = data.Movements.ShouldHaveSingleItem();
        movement.Outflow.ShouldBe(10m);

        data.ClosingBalance.ShouldBe(-10m);
        Reconciles(data).ShouldBeTrue();
    }

    [Fact]
    public async Task A_receipt_from_a_customer_is_reported_against_the_customer()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.PostAsync("CR-2", PostedOn, (books.Bank, EntrySide.Debit, 750m),
            (books.Customer, EntrySide.Credit, 750m));

        CashFlowMovement movement = (await books.ReadAsync()).Movements.ShouldHaveSingleItem();

        movement.Kind.ShouldBe(LedgerKind.Customer);
        movement.Nature.ShouldBe(AccountNature.Asset);
        movement.Inflow.ShouldBe(750m);

        // A debtor is an asset, so classifying by nature alone would file an ordinary
        // trading receipt under investing.
        CashFlowClassification.Classify(movement.Kind, movement.Nature)
            .ShouldBe(CashFlowCategory.Operating);
    }

    [Fact]
    public async Task Capital_introduced_is_financing()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.PostAsync("JV-1", PostedOn, (books.Bank, EntrySide.Debit, 20_000m),
            (books.Capital, EntrySide.Credit, 20_000m));

        CashFlowMovement movement = (await books.ReadAsync()).Movements.ShouldHaveSingleItem();

        CashFlowClassification.Classify(movement.Kind, movement.Nature)
            .ShouldBe(CashFlowCategory.Financing);
    }

    [Fact]
    public async Task Inflow_and_outflow_against_one_account_are_kept_apart()
    {
        // Half a million each way and nothing net is a materially different fact from
        // no activity, so the two directions are never collapsed.
        Books books = await Books.CreateAsync(_fixture);

        await books.PostAsync("CR-3", PostedOn, (books.Bank, EntrySide.Debit, 900m),
            (books.Customer, EntrySide.Credit, 900m));
        await books.PostAsync("PY-2", PostedOn, (books.Customer, EntrySide.Debit, 300m),
            (books.Bank, EntrySide.Credit, 300m));

        CashFlowMovement movement = (await books.ReadAsync()).Movements.ShouldHaveSingleItem();

        movement.Inflow.ShouldBe(900m);
        movement.Outflow.ShouldBe(300m);
    }

    [Fact]
    public async Task The_opening_position_carries_the_stored_opening_balance()
    {
        // Any firm that opened its books with money already in the bank. Omitting the
        // stored balance would make the statement fail to reconcile for all of them.
        Books books = await Books.CreateAsync(_fixture, bankOpeningBalance: 2_500m);

        await books.PostAsync("CR-4", PostedOn, (books.Bank, EntrySide.Debit, 500m),
            (books.Sales, EntrySide.Credit, 500m));

        CashFlowData data = await books.ReadAsync();

        data.OpeningBalance.ShouldBe(2_500m);
        data.ClosingBalance.ShouldBe(3_000m);
        Reconciles(data).ShouldBeTrue();
    }

    [Fact]
    public async Task Postings_before_the_period_land_in_the_opening_position()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.PostAsync("CR-5", new DateOnly(2026, 2, 1),
            (books.Bank, EntrySide.Debit, 600m), (books.Sales, EntrySide.Credit, 600m));
        await books.PostAsync("CR-6", new DateOnly(2026, 8, 1),
            (books.Bank, EntrySide.Debit, 400m), (books.Sales, EntrySide.Credit, 400m));

        CashFlowData data = await books.ReadAsync(
            new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));

        data.OpeningBalance.ShouldBe(600m);
        data.ClosingBalance.ShouldBe(1_000m);
        data.Movements.ShouldHaveSingleItem().Inflow.ShouldBe(400m);
        Reconciles(data).ShouldBeTrue();
    }

    [Fact]
    public async Task A_draft_moves_no_cash()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.PostAsync("DR-1", PostedOn, post: false,
            (books.Bank, EntrySide.Debit, 999m), (books.Sales, EntrySide.Credit, 999m));

        CashFlowData data = await books.ReadAsync();

        data.Movements.ShouldBeEmpty();
        data.ClosingBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task One_firms_cash_is_not_visible_to_another()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.PostAsync("CR-7", PostedOn, (books.Bank, EntrySide.Debit, 100m),
            (books.Sales, EntrySide.Credit, 100m));

        CashFlowData data = await books.ReadAsync(firmId: FirmId.NewId());

        data.Movements.ShouldBeEmpty();
        data.OpeningBalance.ShouldBe(0m);
        data.ClosingBalance.ShouldBe(0m);
    }

    /// <summary>Whether the classified movement accounts for the change in cash.</summary>
    private static bool Reconciles(CashFlowData data) =>
        data.OpeningBalance + data.Movements.Sum(m => m.Inflow - m.Outflow)
            == data.ClosingBalance;

    /// <summary>A tenant with one firm and a chart covering every classification.</summary>
    private sealed class Books
    {
        private static readonly DateTimeOffset PostedAt =
            new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        private readonly PostgresFixture _fixture;
        private readonly TenantId _tenantId = TenantId.NewId();
        private readonly FirmId _firmId = FirmId.NewId();

        private Books(PostgresFixture fixture) => _fixture = fixture;

        internal Ledger Cash { get; private set; } = null!;

        internal Ledger Bank { get; private set; } = null!;

        internal Ledger Sales { get; private set; } = null!;

        internal Ledger Rent { get; private set; } = null!;

        internal Ledger Customer { get; private set; } = null!;

        internal Ledger Capital { get; private set; } = null!;

        private FinancialYear Year { get; set; } = null!;

        /// <summary>Creates the chart of accounts and financial year.</summary>
        /// <param name="fixture">The database fixture.</param>
        /// <param name="bankOpeningBalance">A balance brought in on the bank account.</param>
        /// <returns>The prepared books.</returns>
        internal static async Task<Books> CreateAsync(
            PostgresFixture fixture,
            decimal bankOpeningBalance = 0m)
        {
            Books books = new(fixture);

            await using ErpDbContext context = books.CreateContext();

            AccountGroup assets = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "CA", "Current Assets",
                AccountNature.Asset).Value;
            AccountGroup income = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "IN", "Income", AccountNature.Income).Value;
            AccountGroup expenses = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "EX", "Expenses",
                AccountNature.Expense).Value;
            AccountGroup equity = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "EQ", "Capital", AccountNature.Equity).Value;

            context.AccountGroups.AddRange(assets, income, expenses, equity);

            books.Cash = Ledger.Create(
                assets, "1100", "Cash in hand", LedgerKind.Cash, CurrencyCode.Qar).Value;
            books.Bank = Ledger.Create(
                assets, "1200", "HSBC Current", LedgerKind.Bank, CurrencyCode.Qar).Value;
            books.Sales = Ledger.Create(
                income, "4000", "Sales", LedgerKind.General, CurrencyCode.Qar).Value;
            books.Rent = Ledger.Create(
                expenses, "5000", "Rent", LedgerKind.General, CurrencyCode.Qar).Value;
            books.Customer = Ledger.Create(
                assets, "2000", "Al Mansoor Trading", LedgerKind.Customer,
                CurrencyCode.Qar).Value;
            books.Capital = Ledger.Create(
                equity, "3000", "Owner capital", LedgerKind.General, CurrencyCode.Qar).Value;

            if (bankOpeningBalance != 0m)
            {
                books.Bank.SetOpeningBalance(bankOpeningBalance, EntrySide.Debit)
                    .IsSuccess.ShouldBeTrue();
            }

            context.Ledgers.AddRange(
                books.Cash, books.Bank, books.Sales, books.Rent, books.Customer,
                books.Capital);

            books.Year = FinancialYear.Create(
                books._tenantId, books._firmId, "2026", YearStart, YearEnd, []).Value;

            context.FinancialYears.Add(books.Year);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return books;
        }

        /// <summary>Posts a voucher built from the supplied lines.</summary>
        /// <param name="number">The voucher number.</param>
        /// <param name="date">The document date.</param>
        /// <param name="lines">The lines, which must balance.</param>
        /// <returns>A task representing the operation.</returns>
        internal Task PostAsync(
            string number,
            DateOnly date,
            params (Ledger Ledger, EntrySide Side, decimal Amount)[] lines) =>
            PostAsync(number, date, post: true, lines);

        /// <summary>Saves a voucher, optionally leaving it a draft.</summary>
        /// <param name="number">The voucher number.</param>
        /// <param name="date">The document date.</param>
        /// <param name="post">Whether to post it.</param>
        /// <param name="lines">The lines, which must balance to post.</param>
        /// <returns>A task representing the operation.</returns>
        internal async Task PostAsync(
            string number,
            DateOnly date,
            bool post,
            params (Ledger Ledger, EntrySide Side, decimal Amount)[] lines)
        {
            await using ErpDbContext context = CreateContext();

            Voucher voucher = Voucher.CreateDraft(
                _tenantId, _firmId, BranchId.NewId(), Year, VoucherType.Journal, number,
                date, CurrencyCode.Qar, CurrencyCode.Qar).Value;

            foreach ((Ledger ledger, EntrySide side, decimal amount) in lines)
            {
                voucher.AddLine(ledger.Id, side, amount).IsSuccess.ShouldBeTrue();
            }

            if (post)
            {
                voucher.Post(UserId.NewId(), PostedAt).IsSuccess.ShouldBeTrue();
            }

            context.Vouchers.Add(voucher);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>Runs the reader over these books.</summary>
        /// <param name="from">The first date, defaulting to the year's start.</param>
        /// <param name="to">The last date, defaulting to the year's end.</param>
        /// <param name="firmId">The firm, defaulting to these books'.</param>
        /// <returns>The cash flow data.</returns>
        internal async Task<CashFlowData> ReadAsync(
            DateOnly? from = null,
            DateOnly? to = null,
            FirmId? firmId = null)
        {
            await using ErpDbContext context = CreateContext();

            return await new CashFlowReader(context).ReadAsync(
                firmId ?? _firmId, from ?? YearStart, to ?? YearEnd,
                TestContext.Current.CancellationToken);
        }

        private ErpDbContext CreateContext() =>
            _fixture.CreateContext(PostgresFixture.ScopedTo(_tenantId));
    }
}
