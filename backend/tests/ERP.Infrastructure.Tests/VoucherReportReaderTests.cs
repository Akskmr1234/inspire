using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Reporting;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="VoucherReportReader"/> against a real PostgreSQL instance.
/// </summary>
/// <remarks>
/// <para>
/// The register's whole reason to exist beside the day book is what these pin down:
/// that it returns drafts and cancelled vouchers, not the posted ones alone, and that
/// it still leaves out a soft-deleted row - a discarded draft that handed its number
/// back. Both are decisions that live in the reader's SQL and nowhere a substitute
/// could reach.
/// </para>
/// <para>
/// The value of each voucher is summed from its debit lines in the database. A
/// multi-currency voucher is the test that matters there: its document amount stays in
/// the currency it was entered in while its base amount is the converted figure, and
/// getting the two columns crossed would misstate every foreign voucher on the report.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class VoucherReportReaderTests
{
    private static readonly DateOnly YearStart = new(2026, 1, 1);
    private static readonly DateOnly YearEnd = new(2026, 12, 31);
    private static readonly DateOnly PostedOn = new(2026, 6, 1);

    private readonly PostgresFixture _fixture;

    public VoucherReportReaderTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_posted_voucher_comes_back_with_its_value_summed_from_its_debit_lines()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync(
            "JV-001", PostedOn, VoucherType.Journal, VoucherStatus.Posted, 500m);

        VoucherReportLine row =
            (await books.ReadAsync(YearStart, YearEnd)).ShouldHaveSingleItem();

        row.VoucherNumber.ShouldBe("JV-001");
        row.Type.ShouldBe(VoucherType.Journal);
        row.Status.ShouldBe(VoucherStatus.Posted);
        row.Currency.ShouldBe("QAR");
        row.ExchangeRate.ShouldBe(1m);
        row.DocumentAmount.ShouldBe(500m);
        row.BaseAmount.ShouldBe(500m);
    }

    [Fact]
    public async Task Drafts_and_cancelled_vouchers_appear_which_the_day_book_would_never_show()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync(
            "DR-1", PostedOn, VoucherType.Journal, VoucherStatus.Draft, 100m);
        await books.SaveVoucherAsync(
            "PO-1", PostedOn, VoucherType.Journal, VoucherStatus.Posted, 200m);
        await books.SaveVoucherAsync(
            "CA-1", PostedOn, VoucherType.Journal, VoucherStatus.Cancelled, 300m);

        IReadOnlyList<VoucherReportLine> rows = await books.ReadAsync(YearStart, YearEnd);

        rows.Select(r => r.Status).OrderBy(s => s).ShouldBe(
            [VoucherStatus.Draft, VoucherStatus.Posted, VoucherStatus.Cancelled]);
    }

    [Fact]
    public async Task The_status_filter_narrows_to_one_state()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync(
            "DR-1", PostedOn, VoucherType.Journal, VoucherStatus.Draft, 100m);
        await books.SaveVoucherAsync(
            "PO-1", PostedOn, VoucherType.Journal, VoucherStatus.Posted, 200m);

        VoucherReportLine row = (await books.ReadAsync(
            YearStart, YearEnd, status: VoucherStatus.Draft)).ShouldHaveSingleItem();

        row.VoucherNumber.ShouldBe("DR-1");
    }

    [Fact]
    public async Task The_type_filter_narrows_to_one_kind()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync(
            "JV-1", PostedOn, VoucherType.Journal, VoucherStatus.Posted, 100m);
        await books.SaveVoucherAsync(
            "BR-1", PostedOn, VoucherType.BankReceipt, VoucherStatus.Posted, 200m);

        VoucherReportLine row = (await books.ReadAsync(
            YearStart, YearEnd, type: VoucherType.BankReceipt)).ShouldHaveSingleItem();

        row.VoucherNumber.ShouldBe("BR-1");
    }

    [Fact]
    public async Task A_voucher_dated_outside_the_range_is_excluded()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync(
            "MAR", new DateOnly(2026, 3, 1), VoucherType.Journal, VoucherStatus.Posted, 100m);
        await books.SaveVoucherAsync(
            "AUG", new DateOnly(2026, 8, 1), VoucherType.Journal, VoucherStatus.Posted, 200m);

        VoucherReportLine row = (await books.ReadAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).ShouldHaveSingleItem();

        row.VoucherNumber.ShouldBe("MAR");
    }

    [Fact]
    public async Task A_soft_deleted_voucher_is_excluded()
    {
        // No domain path sets this today, but the reader filters it deliberately: a
        // discarded draft that handed its number back has no place on any report, and
        // the filter must already be right for the day soft delete is wired up.
        Books books = await Books.CreateAsync(_fixture);

        Guid id = await books.SaveVoucherAsync(
            "GONE", PostedOn, VoucherType.Journal, VoucherStatus.Posted, 100m);

        (await books.ReadAsync(YearStart, YearEnd)).ShouldHaveSingleItem();

        await books.SoftDeleteAsync(id);

        (await books.ReadAsync(YearStart, YearEnd)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_multi_currency_voucher_shows_its_document_amount_and_the_base_conversion()
    {
        // Entered in dollars at 3.65; the document amount stays 100 USD and the base
        // amount is the 365 QAR the posting actually moved through the ledgers.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync(
            "FX-1", PostedOn, VoucherType.Journal, VoucherStatus.Posted, 100m,
            currency: "USD", exchangeRate: 3.65m);

        VoucherReportLine row =
            (await books.ReadAsync(YearStart, YearEnd)).ShouldHaveSingleItem();

        row.Currency.ShouldBe("USD");
        row.ExchangeRate.ShouldBe(3.65m);
        row.DocumentAmount.ShouldBe(100m);
        row.BaseAmount.ShouldBe(365m);
    }

    [Fact]
    public async Task One_firms_vouchers_are_not_visible_to_another()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveVoucherAsync(
            "JV-1", PostedOn, VoucherType.Journal, VoucherStatus.Posted, 100m);

        (await books.ReadAsync(YearStart, YearEnd, firmId: FirmId.NewId())).ShouldBeEmpty();
    }

    /// <summary>
    /// A tenant with one firm, two ledgers to post between, and the financial year the
    /// vouchers hang off.
    /// </summary>
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

        /// <summary>Creates the chart of accounts and financial year the tests hang off.</summary>
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
                books._tenantId, books._firmId, "2026",
                YearStart, YearEnd, []).Value;

            context.FinancialYears.Add(books.Year);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return books;
        }

        /// <summary>Builds a two-line voucher, drives it to a status, and saves it.</summary>
        /// <param name="number">The voucher number.</param>
        /// <param name="date">The document date.</param>
        /// <param name="type">The voucher type.</param>
        /// <param name="status">The status to leave it in.</param>
        /// <param name="amount">The amount, entered on both sides so it balances.</param>
        /// <param name="currency">The entry currency, QAR or USD.</param>
        /// <param name="exchangeRate">The rate to the base currency.</param>
        /// <returns>The voucher's id.</returns>
        /// <remarks>
        /// Reached by the same transitions the application uses, so a cancelled voucher
        /// really was posted and then cancelled, and a posted one really carries the
        /// base amounts that <see cref="Voucher.Post"/> assigns.
        /// </remarks>
        internal async Task<Guid> SaveVoucherAsync(
            string number,
            DateOnly date,
            VoucherType type,
            VoucherStatus status,
            decimal amount,
            string currency = "QAR",
            decimal exchangeRate = 1m)
        {
            await using ErpDbContext context = CreateContext();

            CurrencyCode entryCurrency =
                currency == "USD" ? CurrencyCode.Usd : CurrencyCode.Qar;

            Voucher voucher = Voucher.CreateDraft(
                _tenantId, _firmId, BranchId.NewId(), Year, type, number, date,
                entryCurrency, CurrencyCode.Qar, exchangeRate).Value;

            voucher.AddLine(DebitLedger.Id, EntrySide.Debit, amount).IsSuccess.ShouldBeTrue();
            voucher.AddLine(CreditLedger.Id, EntrySide.Credit, amount).IsSuccess.ShouldBeTrue();

            if (status is VoucherStatus.Posted or VoucherStatus.Cancelled)
            {
                voucher.Post(UserId.NewId(), PostedAt).IsSuccess.ShouldBeTrue();
            }

            if (status == VoucherStatus.Cancelled)
            {
                voucher.Cancel("Superseded").IsSuccess.ShouldBeTrue();
            }

            context.Vouchers.Add(voucher);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return voucher.Id.Value;
        }

        /// <summary>Marks a voucher soft-deleted directly, there being no domain path.</summary>
        /// <param name="voucherId">The voucher to mark deleted.</param>
        /// <returns>A task representing the operation.</returns>
        internal async Task SoftDeleteAsync(Guid voucherId)
        {
            await using ErpDbContext context = CreateContext();

            await context.Database.ExecuteSqlAsync(
                $"UPDATE vouchers SET is_deleted = true WHERE id = {voucherId}",
                TestContext.Current.CancellationToken);
        }

        /// <summary>Runs the reader over these books.</summary>
        /// <param name="from">The first date of the period.</param>
        /// <param name="to">The last date of the period.</param>
        /// <param name="type">One type, or null for all.</param>
        /// <param name="status">One status, or null for all.</param>
        /// <param name="firmId">The firm, defaulting to these books'.</param>
        /// <returns>The rows the reader produced.</returns>
        internal async Task<IReadOnlyList<VoucherReportLine>> ReadAsync(
            DateOnly from,
            DateOnly to,
            VoucherType? type = null,
            VoucherStatus? status = null,
            FirmId? firmId = null)
        {
            await using ErpDbContext context = CreateContext();

            return await new VoucherReportReader(context).ReadAsync(
                firmId ?? _firmId, from, to, type, status,
                TestContext.Current.CancellationToken);
        }

        private ErpDbContext CreateContext() =>
            _fixture.CreateContext(PostgresFixture.ScopedTo(_tenantId));
    }
}
