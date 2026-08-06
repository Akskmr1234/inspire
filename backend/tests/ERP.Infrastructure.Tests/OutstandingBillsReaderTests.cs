using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Reporting;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="OutstandingBillsReader"/> against a real PostgreSQL
/// instance.
/// </summary>
/// <remarks>
/// <para>
/// The handlers above this reader are tested with a substitute, which proves how
/// the figures are presented but not that the query producing them can run at all.
/// Everything specific to this reader lives in the SQL: the correlated subquery that
/// keeps a bill settled later in the list, the aggregate over allocations bounded by
/// the reporting date, and the complex-property columns holding money and currency.
/// None of that is exercised by a substitute.
/// </para>
/// <para>
/// The as-at behaviour especially. It is the one rule here an operator would notice
/// being wrong only months later, when a report re-run for a closed period no longer
/// matches the copy filed at the time.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class OutstandingBillsReaderTests
{
    private static readonly DateOnly JuneEnd = new(2026, 6, 30);

    private readonly PostgresFixture _fixture;

    public OutstandingBillsReaderTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_open_bill_is_returned_with_its_party_and_its_currency()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveBillAsync(
            "INV-001", 1_000m, billDate: new DateOnly(2026, 6, 1), creditDays: 30);

        IReadOnlyList<OutstandingBillRow> rows = await books.ReadAsync(JuneEnd);

        OutstandingBillRow row = rows.ShouldHaveSingleItem();
        row.BillNumber.ShouldBe("INV-001");
        row.LedgerCode.ShouldBe("2000");
        row.LedgerName.ShouldBe("Al Mansoor Trading");
        row.OriginalAmount.ShouldBe(1_000m);
        row.SettledAmount.ShouldBe(0m);
        row.OutstandingAmount.ShouldBe(1_000m);
        row.DueDate.ShouldBe(new DateOnly(2026, 7, 1));
        row.Currency.ShouldBe("QAR");
    }

    [Fact]
    public async Task A_part_settled_bill_reports_what_the_allocations_left()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveBillAsync(
            "INV-002", 1_000m, billDate: new DateOnly(2026, 6, 1), creditDays: 30,
            allocations: [(400m, new DateOnly(2026, 6, 10))]);

        IReadOnlyList<OutstandingBillRow> rows = await books.ReadAsync(JuneEnd);

        OutstandingBillRow row = rows.ShouldHaveSingleItem();
        row.SettledAmount.ShouldBe(400m);
        row.OutstandingAmount.ShouldBe(600m);
    }

    [Fact]
    public async Task A_bill_settled_before_the_reporting_date_is_gone()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveBillAsync(
            "INV-003", 500m, billDate: new DateOnly(2026, 6, 1), creditDays: 30,
            allocations: [(500m, new DateOnly(2026, 6, 20))]);

        (await books.ReadAsync(JuneEnd)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bill_settled_after_the_reporting_date_was_still_open_on_it()
    {
        // The reason the query cannot simply take unsettled bills, and the reason
        // the settled figure is recomputed rather than read off the row. This bill
        // is fully settled today; on 30 June it was outstanding in full, and a
        // report for June must say so.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveBillAsync(
            "INV-004", 750m, billDate: new DateOnly(2026, 6, 1), creditDays: 30,
            allocations: [(750m, new DateOnly(2026, 7, 15))]);

        OutstandingBillRow row = (await books.ReadAsync(JuneEnd)).ShouldHaveSingleItem();
        row.SettledAmount.ShouldBe(0m);
        row.OutstandingAmount.ShouldBe(750m);

        // And by the middle of July it is gone.
        (await books.ReadAsync(new DateOnly(2026, 7, 31))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_allocations_up_to_the_reporting_date_are_counted()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveBillAsync(
            "INV-005", 900m, billDate: new DateOnly(2026, 6, 1), creditDays: 30,
            allocations:
            [
                (300m, new DateOnly(2026, 6, 10)),
                (200m, new DateOnly(2026, 6, 25)),
                (400m, new DateOnly(2026, 7, 5)),
            ]);

        OutstandingBillRow row = (await books.ReadAsync(JuneEnd)).ShouldHaveSingleItem();
        row.SettledAmount.ShouldBe(500m);
        row.OutstandingAmount.ShouldBe(400m);
    }

    [Fact]
    public async Task A_bill_raised_after_the_reporting_date_has_not_happened_yet()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveBillAsync(
            "INV-006", 100m, billDate: new DateOnly(2026, 7, 3), creditDays: 30);

        (await books.ReadAsync(JuneEnd)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Payables_are_not_returned_by_a_receivables_report()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveBillAsync(
            "PO-001", 400m, billDate: new DateOnly(2026, 6, 1), creditDays: 30,
            type: BillType.Payable);

        (await books.ReadAsync(JuneEnd)).ShouldBeEmpty();
        (await books.ReadAsync(JuneEnd, BillType.Payable)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_report_can_be_narrowed_to_one_party()
    {
        Books books = await Books.CreateAsync(_fixture);
        Ledger other = await books.AddPartyAsync("2500", "Zenith Stores");

        await books.SaveBillAsync(
            "INV-007", 100m, billDate: new DateOnly(2026, 6, 1), creditDays: 30);
        await books.SaveBillAsync(
            "INV-008", 200m, billDate: new DateOnly(2026, 6, 1), creditDays: 30,
            party: other);

        (await books.ReadAsync(JuneEnd)).Count.ShouldBe(2);

        OutstandingBillRow row = (await books.ReadAsync(JuneEnd, party: other))
            .ShouldHaveSingleItem();

        row.BillNumber.ShouldBe("INV-008");
    }

    [Fact]
    public async Task One_firms_bills_are_not_visible_to_another()
    {
        // Both firms belong to the same tenant, so no query filter and no
        // row-level-security policy separates them. Only the reader's own predicate
        // does, and a report that leaked across firms would show a customer's debts
        // in a sister company's books.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveBillAsync(
            "INV-009", 100m, billDate: new DateOnly(2026, 6, 1), creditDays: 30);

        (await books.ReadAsync(JuneEnd, firmId: FirmId.NewId())).ShouldBeEmpty();
    }

    /// <summary>
    /// A tenant with one firm, one customer, and a voucher to hang settlements on.
    /// </summary>
    private sealed class Books
    {
        private readonly PostgresFixture _fixture;
        private readonly TenantId _tenantId = TenantId.NewId();
        private readonly FirmId _firmId = FirmId.NewId();

        private Books(PostgresFixture fixture) => _fixture = fixture;

        private Ledger Customer { get; set; } = null!;

        private VoucherId SettlingVoucherId { get; set; }

        /// <summary>Creates the chart of accounts and voucher the tests hang off.</summary>
        /// <param name="fixture">The database fixture.</param>
        /// <returns>The prepared books.</returns>
        internal static async Task<Books> CreateAsync(PostgresFixture fixture)
        {
            Books books = new(fixture);

            await using ErpDbContext context = books.CreateContext();

            AccountGroup debtors = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "SD", "Sundry Debtors",
                AccountNature.Asset).Value;

            context.AccountGroups.Add(debtors);

            books.Customer = Ledger.Create(
                debtors, "2000", "Al Mansoor Trading", LedgerKind.Customer,
                CurrencyCode.Qar).Value;

            context.Ledgers.Add(books.Customer);

            // A bill allocation carries a foreign key to the voucher that made it,
            // so the settlements below need a real one to point at.
            FinancialYear year = FinancialYear.Create(
                books._tenantId, books._firmId, "2026",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

            Voucher receipt = Voucher.CreateDraft(
                books._tenantId, books._firmId, BranchId.NewId(), year,
                VoucherType.BankReceipt, "BR/2026/0001", new DateOnly(2026, 6, 10),
                CurrencyCode.Qar, CurrencyCode.Qar, 1m).Value;

            context.Vouchers.Add(receipt);
            books.SettlingVoucherId = receipt.Id;

            await context.SaveChangesAsync();

            return books;
        }

        /// <summary>Adds a second party to the same firm.</summary>
        /// <param name="code">The ledger code.</param>
        /// <param name="name">The ledger name.</param>
        /// <returns>The new party.</returns>
        internal async Task<Ledger> AddPartyAsync(string code, string name)
        {
            await using ErpDbContext context = CreateContext();

            AccountGroup group = AccountGroup.CreateRoot(
                _tenantId, _firmId, $"SD-{code}", $"Debtors {code}",
                AccountNature.Asset).Value;

            context.AccountGroups.Add(group);

            Ledger party = Ledger.Create(
                group, code, name, LedgerKind.Customer, CurrencyCode.Qar).Value;

            context.Ledgers.Add(party);

            await context.SaveChangesAsync();

            return party;
        }

        /// <summary>Raises a bill, allocates against it, and saves both.</summary>
        /// <param name="number">The bill reference.</param>
        /// <param name="amount">The amount raised.</param>
        /// <param name="billDate">The date it was raised.</param>
        /// <param name="creditDays">The credit period.</param>
        /// <param name="allocations">Settlements, each an amount and a date.</param>
        /// <param name="type">Receivable or payable.</param>
        /// <param name="party">The party, defaulting to the standard customer.</param>
        /// <returns>A task representing the operation.</returns>
        internal async Task SaveBillAsync(
            string number,
            decimal amount,
            DateOnly billDate,
            int creditDays,
            IReadOnlyList<(decimal Amount, DateOnly On)>? allocations = null,
            BillType type = BillType.Receivable,
            Ledger? party = null)
        {
            await using ErpDbContext context = CreateContext();

            Bill bill = Bill.Raise(
                _tenantId, _firmId, (party ?? Customer).Id, SettlingVoucherId, type,
                number, billDate, creditDays, Money.Of(amount, CurrencyCode.Qar)).Value;

            foreach ((decimal allocated, DateOnly on) in allocations ?? [])
            {
                bill.Allocate(SettlingVoucherId, Money.Of(allocated, CurrencyCode.Qar), on)
                    .IsSuccess.ShouldBeTrue();
            }

            context.Bills.Add(bill);

            await context.SaveChangesAsync();
        }

        /// <summary>Runs the reader over these books.</summary>
        /// <param name="asAt">The reporting date.</param>
        /// <param name="type">Receivable or payable.</param>
        /// <param name="party">One party, or null for all of them.</param>
        /// <param name="firmId">The firm, defaulting to these books'.</param>
        /// <returns>The rows the reader produced.</returns>
        internal async Task<IReadOnlyList<OutstandingBillRow>> ReadAsync(
            DateOnly asAt,
            BillType type = BillType.Receivable,
            Ledger? party = null,
            FirmId? firmId = null)
        {
            await using ErpDbContext context = CreateContext();

            return await new OutstandingBillsReader(context)
                .ReadAsync(firmId ?? _firmId, type, asAt, party?.Id, TestContext.Current.CancellationToken);
        }

        private ErpDbContext CreateContext() =>
            _fixture.CreateContext(PostgresFixture.ScopedTo(_tenantId));
    }
}
