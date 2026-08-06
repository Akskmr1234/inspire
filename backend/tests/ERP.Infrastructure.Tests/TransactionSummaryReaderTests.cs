using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Reporting;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="TransactionSummaryReader"/> against a real PostgreSQL
/// instance.
/// </summary>
/// <remarks>
/// <para>
/// This reader is almost entirely SQL, and the part most likely to break is the one a
/// substitute could never catch: it groups by the year and month of a
/// <see cref="DateOnly"/> column, which the provider must translate into date
/// arithmetic rather than evaluating in memory. A translation failure here does not
/// produce a wrong number - it throws, or silently pulls every voucher of the period
/// into the client to group it there.
/// </para>
/// <para>
/// The other thing worth pinning is that counts and totals come from two separate
/// aggregations. They are joined back together on the same four-part key, and a
/// voucher carrying no debit lines must still be counted - which is exactly the case
/// a join written the other way round would drop.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TransactionSummaryReaderTests
{
    private static readonly DateOnly YearStart = new(2026, 1, 1);
    private static readonly DateOnly YearEnd = new(2026, 12, 31);

    private readonly PostgresFixture _fixture;

    public TransactionSummaryReaderTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Vouchers_are_aggregated_by_type_status_and_month()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync("JV-1", new DateOnly(2026, 3, 5), VoucherType.Journal, 100m);
        await books.SaveVoucherAsync("JV-2", new DateOnly(2026, 3, 20), VoucherType.Journal, 250m);

        TransactionSummaryBucket cell = (await books.ReadAsync()).ShouldHaveSingleItem();

        cell.Type.ShouldBe(VoucherType.Journal);
        cell.Status.ShouldBe(VoucherStatus.Posted);
        cell.Year.ShouldBe(2026);
        cell.Month.ShouldBe(3);
        cell.VoucherCount.ShouldBe(2);
        cell.TotalAmount.ShouldBe(350m);
    }

    [Fact]
    public async Task Different_months_fall_into_different_cells()
    {
        // The grouping that must reach SQL. Two vouchers of the same type and status,
        // separated only by the month on their date.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync("JAN", new DateOnly(2026, 1, 10), VoucherType.Journal, 100m);
        await books.SaveVoucherAsync("FEB", new DateOnly(2026, 2, 10), VoucherType.Journal, 200m);

        IReadOnlyList<TransactionSummaryBucket> cells = await books.ReadAsync();

        cells.Count.ShouldBe(2);
        cells.Single(c => c.Month == 1).TotalAmount.ShouldBe(100m);
        cells.Single(c => c.Month == 2).TotalAmount.ShouldBe(200m);
    }

    [Fact]
    public async Task Different_types_fall_into_different_cells()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync("JV", new DateOnly(2026, 4, 1), VoucherType.Journal, 100m);
        await books.SaveVoucherAsync("BR", new DateOnly(2026, 4, 2), VoucherType.BankReceipt, 200m);

        IReadOnlyList<TransactionSummaryBucket> cells = await books.ReadAsync();

        cells.Count.ShouldBe(2);
        cells.Single(c => c.Type == VoucherType.Journal).TotalAmount.ShouldBe(100m);
        cells.Single(c => c.Type == VoucherType.BankReceipt).TotalAmount.ShouldBe(200m);
    }

    [Fact]
    public async Task A_draft_with_no_lines_is_still_counted()
    {
        // The reason the merge is driven by the counts rather than the totals. A draft
        // somebody is midway through entering has no debit lines and therefore no
        // total, and the number of drafts left unposted is one of the things this
        // report is opened to find out.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveEmptyDraftAsync("EMPTY", new DateOnly(2026, 5, 1));

        TransactionSummaryBucket cell = (await books.ReadAsync()).ShouldHaveSingleItem();

        cell.Status.ShouldBe(VoucherStatus.Draft);
        cell.VoucherCount.ShouldBe(1);
        cell.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task The_status_filter_narrows_the_aggregation()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync("PO", new DateOnly(2026, 6, 1), VoucherType.Journal, 100m);
        await books.SaveEmptyDraftAsync("DR", new DateOnly(2026, 6, 2));

        (await books.ReadAsync()).Count.ShouldBe(2);

        TransactionSummaryBucket cell =
            (await books.ReadAsync(status: VoucherStatus.Draft)).ShouldHaveSingleItem();

        cell.Status.ShouldBe(VoucherStatus.Draft);
    }

    [Fact]
    public async Task A_voucher_outside_the_range_is_excluded()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync("IN", new DateOnly(2026, 3, 1), VoucherType.Journal, 100m);
        await books.SaveVoucherAsync("OUT", new DateOnly(2026, 8, 1), VoucherType.Journal, 200m);

        TransactionSummaryBucket cell = (await books.ReadAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).ShouldHaveSingleItem();

        cell.TotalAmount.ShouldBe(100m);
    }

    [Fact]
    public async Task One_firms_vouchers_are_not_visible_to_another()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync("JV", new DateOnly(2026, 3, 1), VoucherType.Journal, 100m);

        (await books.ReadAsync(firmId: FirmId.NewId())).ShouldBeEmpty();
    }

    /// <summary>A tenant with one firm and two ledgers to post between.</summary>
    private sealed class Books
    {
        private static readonly DateTimeOffset PostedAt =
            new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        private readonly PostgresFixture _fixture;
        private readonly TenantId _tenantId = TenantId.NewId();
        private readonly FirmId _firmId = FirmId.NewId();

        private Books(PostgresFixture fixture) => _fixture = fixture;

        private Ledger DebitLedger { get; set; } = null!;

        private Ledger CreditLedger { get; set; } = null!;

        private FinancialYear Year { get; set; } = null!;

        /// <summary>Creates the chart of accounts and financial year.</summary>
        /// <param name="fixture">The database fixture.</param>
        /// <returns>The prepared books.</returns>
        internal static async Task<Books> CreateAsync(PostgresFixture fixture)
        {
            Books books = new(fixture);

            await using ErpDbContext context = books.CreateContext();

            AccountGroup assets = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "CA", "Current Assets",
                AccountNature.Asset).Value;
            AccountGroup income = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "IN", "Income",
                AccountNature.Income).Value;

            context.AccountGroups.AddRange(assets, income);

            books.DebitLedger = Ledger.Create(
                assets, "1100", "Cash", LedgerKind.Cash, CurrencyCode.Qar).Value;
            books.CreditLedger = Ledger.Create(
                income, "4000", "Sales", LedgerKind.General, CurrencyCode.Qar).Value;

            context.Ledgers.AddRange(books.DebitLedger, books.CreditLedger);

            books.Year = FinancialYear.Create(
                books._tenantId, books._firmId, "2026", YearStart, YearEnd, []).Value;

            context.FinancialYears.Add(books.Year);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return books;
        }

        /// <summary>Saves a balanced, posted voucher.</summary>
        /// <param name="number">The voucher number.</param>
        /// <param name="date">The document date.</param>
        /// <param name="type">The voucher type.</param>
        /// <param name="amount">The amount, entered on both sides so it balances.</param>
        /// <returns>A task representing the operation.</returns>
        internal async Task SaveVoucherAsync(
            string number,
            DateOnly date,
            VoucherType type,
            decimal amount)
        {
            await using ErpDbContext context = CreateContext();

            Voucher voucher = Voucher.CreateDraft(
                _tenantId, _firmId, BranchId.NewId(), Year, type, number, date,
                CurrencyCode.Qar, CurrencyCode.Qar).Value;

            voucher.AddLine(DebitLedger.Id, EntrySide.Debit, amount).IsSuccess.ShouldBeTrue();
            voucher.AddLine(CreditLedger.Id, EntrySide.Credit, amount).IsSuccess.ShouldBeTrue();
            voucher.Post(UserId.NewId(), PostedAt).IsSuccess.ShouldBeTrue();

            context.Vouchers.Add(voucher);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>Saves a draft carrying no lines at all.</summary>
        /// <param name="number">The voucher number.</param>
        /// <param name="date">The document date.</param>
        /// <returns>A task representing the operation.</returns>
        internal async Task SaveEmptyDraftAsync(string number, DateOnly date)
        {
            await using ErpDbContext context = CreateContext();

            Voucher voucher = Voucher.CreateDraft(
                _tenantId, _firmId, BranchId.NewId(), Year, VoucherType.Journal, number,
                date, CurrencyCode.Qar, CurrencyCode.Qar).Value;

            context.Vouchers.Add(voucher);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>Runs the reader over these books.</summary>
        /// <param name="from">The first date of the period.</param>
        /// <param name="to">The last date of the period.</param>
        /// <param name="status">One status, or null for all.</param>
        /// <param name="firmId">The firm, defaulting to these books'.</param>
        /// <returns>The cells the reader produced.</returns>
        internal async Task<IReadOnlyList<TransactionSummaryBucket>> ReadAsync(
            DateOnly? from = null,
            DateOnly? to = null,
            VoucherStatus? status = null,
            FirmId? firmId = null)
        {
            await using ErpDbContext context = CreateContext();

            return await new TransactionSummaryReader(context).ReadAsync(
                firmId ?? _firmId, from ?? YearStart, to ?? YearEnd, status,
                TestContext.Current.CancellationToken);
        }

        private ErpDbContext CreateContext() =>
            _fixture.CreateContext(PostgresFixture.ScopedTo(_tenantId));
    }
}
