using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Reporting;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="ChequeReportReader"/> against a real PostgreSQL instance.
/// </summary>
/// <remarks>
/// <para>
/// The handlers above this reader are tested with a substitute, which proves how the
/// figures are presented but not that the query producing them can run. Everything
/// specific to the reader lives in the SQL: the switch between reading by the date on
/// a cheque's face and the date it changed hands, the open-only filter that must line
/// up with the partial index, the left-hand resolution of the firm's own account that
/// a received cheque in hand does not yet have, and the complex-property columns
/// holding money and currency. None of that is exercised by a substitute.
/// </para>
/// <para>
/// The instrument-date-versus-recorded-date distinction especially. Reading the
/// register by the wrong one would file every cheque under the day it falls due
/// rather than the day it was taken in, and a bookkeeper looking for last Tuesday's
/// takings would not find them where they left them.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ChequeReportReaderTests
{
    private static readonly DateOnly September = new(2026, 9, 15);

    private readonly PostgresFixture _fixture;

    public ChequeReportReaderTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_cheque_comes_back_with_its_party_its_amount_and_its_currency()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync(
            "CHQ-001", ChequeDirection.Received, September, 1_500m,
            drawnOnBank: "Doha Bank");

        IReadOnlyList<ChequeReportRow> rows = await books.ByFaceDateAsync(September, September);

        ChequeReportRow row = rows.ShouldHaveSingleItem();
        row.ChequeNumber.ShouldBe("CHQ-001");
        row.Direction.ShouldBe(ChequeDirection.Received);
        row.Status.ShouldBe(ChequeStatus.Pending);
        row.PartyCode.ShouldBe("2000");
        row.PartyName.ShouldBe("Al Mansoor Trading");
        row.Amount.ShouldBe(1_500m);
        row.Currency.ShouldBe("QAR");
        row.DrawnOnBank.ShouldBe("Doha Bank");
        row.InstrumentDate.ShouldBe(September);
    }

    [Fact]
    public async Task An_issued_cheque_shows_the_firms_own_account()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync("CHQ-100", ChequeDirection.Issued, September, 800m);

        ChequeReportRow row =
            (await books.ByFaceDateAsync(September, September)).ShouldHaveSingleItem();

        row.BankAccountName.ShouldBe("HSBC Current");
        row.DrawnOnBank.ShouldBeNull();
    }

    [Fact]
    public async Task A_received_cheque_in_hand_has_no_account_yet_only_the_payers_bank()
    {
        // The reason the account is resolved with a left hand rather than an inner
        // join: a received cheque has no firm account until it is banked, and an inner
        // join would drop every pending one - which is exactly what the PDC report is
        // for.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync(
            "CHQ-101", ChequeDirection.Received, September, 600m, drawnOnBank: "Doha Bank");

        ChequeReportRow row =
            (await books.ByFaceDateAsync(September, September)).ShouldHaveSingleItem();

        row.BankAccountName.ShouldBeNull();
        row.DrawnOnBank.ShouldBe("Doha Bank");
    }

    [Fact]
    public async Task The_calendar_reads_by_the_date_on_the_cheques_face()
    {
        // Taken in on 15 July, dated 20 September. By its face date it belongs to
        // September and to no earlier month.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync(
            "CHQ-200", ChequeDirection.Received, new DateOnly(2026, 9, 20), 400m,
            recordedOn: new DateOnly(2026, 7, 15));

        (await books.ByFaceDateAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)))
            .ShouldHaveSingleItem();
        (await books.ByFaceDateAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task The_register_reads_by_the_date_the_cheque_changed_hands()
    {
        // The same cheque, read the register's way: it was taken in in July and falls
        // under July, not under the September it is dated.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync(
            "CHQ-201", ChequeDirection.Received, new DateOnly(2026, 9, 20), 400m,
            recordedOn: new DateOnly(2026, 7, 15));

        (await books.ByRecordedDateAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)))
            .ShouldHaveSingleItem();
        (await books.ByRecordedDateAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Open_only_drops_the_cheques_that_have_already_resolved()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync("PEND", ChequeDirection.Received, September, 100m);
        await books.SaveChequeAsync(
            "DEP", ChequeDirection.Received, September, 100m, status: ChequeStatus.Deposited);
        await books.SaveChequeAsync(
            "CLR", ChequeDirection.Received, September, 100m, status: ChequeStatus.Cleared);
        await books.SaveChequeAsync(
            "BNC", ChequeDirection.Received, September, 100m, status: ChequeStatus.Bounced);

        (await books.ByFaceDateAsync(September, September)).Count.ShouldBe(4);

        IReadOnlyList<ChequeReportRow> open =
            await books.ByFaceDateAsync(September, September, openOnly: true);

        open.Select(r => r.ChequeNumber).OrderBy(n => n).ShouldBe(["DEP", "PEND"]);
    }

    [Fact]
    public async Task The_status_filter_narrows_to_one_state()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync("PEND", ChequeDirection.Received, September, 100m);
        await books.SaveChequeAsync(
            "CLR", ChequeDirection.Received, September, 100m, status: ChequeStatus.Cleared);

        ChequeReportRow row = (await books.ByFaceDateAsync(
            September, September, status: ChequeStatus.Cleared)).ShouldHaveSingleItem();

        row.ChequeNumber.ShouldBe("CLR");
    }

    [Fact]
    public async Task The_direction_filter_separates_received_from_issued()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync("IN", ChequeDirection.Received, September, 100m);
        await books.SaveChequeAsync("OUT", ChequeDirection.Issued, September, 200m);

        ChequeReportRow received = (await books.ByFaceDateAsync(
            September, September, direction: ChequeDirection.Received)).ShouldHaveSingleItem();
        received.ChequeNumber.ShouldBe("IN");

        ChequeReportRow issued = (await books.ByFaceDateAsync(
            September, September, direction: ChequeDirection.Issued)).ShouldHaveSingleItem();
        issued.ChequeNumber.ShouldBe("OUT");
    }

    [Fact]
    public async Task The_report_can_be_narrowed_to_one_party()
    {
        Books books = await Books.CreateAsync(_fixture);
        Ledger other = await books.AddCustomerAsync("2500", "Zenith Stores");

        await books.SaveChequeAsync("CHQ-A", ChequeDirection.Received, September, 100m);
        await books.SaveChequeAsync(
            "CHQ-B", ChequeDirection.Received, September, 200m, party: other);

        (await books.ByFaceDateAsync(September, September)).Count.ShouldBe(2);

        ChequeReportRow row = (await books.ByFaceDateAsync(September, September, party: other))
            .ShouldHaveSingleItem();

        row.ChequeNumber.ShouldBe("CHQ-B");
    }

    [Fact]
    public async Task A_max_value_upper_bound_returns_the_future_dated_cheques()
    {
        // The PDC report runs from the day after the reporting date to DateOnly.MaxValue,
        // so an open-ended upper bound has to translate and behave. A cheque dated well
        // into next year is exactly what it is meant to surface.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync(
            "CHQ-FUT", ChequeDirection.Received, new DateOnly(2027, 3, 1), 900m);

        ChequeReportRow row = (await books.ByFaceDateAsync(
            new DateOnly(2026, 8, 7), DateOnly.MaxValue, openOnly: true))
            .ShouldHaveSingleItem();

        row.ChequeNumber.ShouldBe("CHQ-FUT");
    }

    [Fact]
    public async Task One_firms_cheques_are_not_visible_to_another()
    {
        // Both firms belong to the same tenant, so no query filter and no row-level
        // security policy separates them. Only the reader's own predicate does, and a
        // report that leaked across firms would show one company's instruments in
        // another's books.
        Books books = await Books.CreateAsync(_fixture);

        await books.SaveChequeAsync("CHQ-1", ChequeDirection.Received, September, 100m);

        (await books.ByFaceDateAsync(September, September, firmId: FirmId.NewId()))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A tenant with one firm, a customer, a supplier, a bank account, and a voucher
    /// to hang cheques on.
    /// </summary>
    private sealed class Books
    {
        private readonly PostgresFixture _fixture;
        private readonly TenantId _tenantId = TenantId.NewId();
        private readonly FirmId _firmId = FirmId.NewId();

        private Books(PostgresFixture fixture) => _fixture = fixture;

        private Ledger Customer { get; set; } = null!;

        private Ledger Supplier { get; set; } = null!;

        private Ledger BankAccount { get; set; } = null!;

        private VoucherId VoucherId { get; set; }

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
            AccountGroup creditors = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "SC", "Sundry Creditors",
                AccountNature.Liability).Value;
            AccountGroup banks = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "BK", "Bank Accounts",
                AccountNature.Asset).Value;

            context.AccountGroups.AddRange(debtors, creditors, banks);

            books.Customer = Ledger.Create(
                debtors, "2000", "Al Mansoor Trading", LedgerKind.Customer,
                CurrencyCode.Qar).Value;
            books.Supplier = Ledger.Create(
                creditors, "3000", "Gulf Supplies", LedgerKind.Supplier,
                CurrencyCode.Qar).Value;
            books.BankAccount = Ledger.Create(
                banks, "1200", "HSBC Current", LedgerKind.Bank, CurrencyCode.Qar).Value;

            context.Ledgers.AddRange(books.Customer, books.Supplier, books.BankAccount);

            // A cheque carries a foreign key to the voucher that recorded it, and a
            // cleared one to the voucher that posted its clearance. One real voucher
            // satisfies both.
            FinancialYear year = FinancialYear.Create(
                books._tenantId, books._firmId, "2026",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

            Voucher voucher = Voucher.CreateDraft(
                books._tenantId, books._firmId, BranchId.NewId(), year,
                VoucherType.BankReceipt, "BR/2026/0001", new DateOnly(2026, 6, 10),
                CurrencyCode.Qar, CurrencyCode.Qar, 1m).Value;

            context.Vouchers.Add(voucher);
            books.VoucherId = voucher.Id;

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return books;
        }

        /// <summary>Adds a second customer to the same firm.</summary>
        /// <param name="code">The ledger code.</param>
        /// <param name="name">The ledger name.</param>
        /// <returns>The new party.</returns>
        internal async Task<Ledger> AddCustomerAsync(string code, string name)
        {
            await using ErpDbContext context = CreateContext();

            AccountGroup group = AccountGroup.CreateRoot(
                _tenantId, _firmId, $"SD-{code}", $"Debtors {code}",
                AccountNature.Asset).Value;

            context.AccountGroups.Add(group);

            Ledger party = Ledger.Create(
                group, code, name, LedgerKind.Customer, CurrencyCode.Qar).Value;

            context.Ledgers.Add(party);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return party;
        }

        /// <summary>Records a cheque, drives it to a status, and saves it.</summary>
        /// <param name="number">The cheque number.</param>
        /// <param name="direction">Received or issued.</param>
        /// <param name="instrumentDate">The date on its face.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="status">The status to leave it in.</param>
        /// <param name="recordedOn">The date it changed hands, defaulting to its face date.</param>
        /// <param name="party">The party, defaulting to the customer or supplier by direction.</param>
        /// <param name="drawnOnBank">The payer's bank named on a received cheque.</param>
        /// <returns>A task representing the operation.</returns>
        /// <remarks>
        /// The status is reached by the same transitions the application uses, not by
        /// setting a column, so a cheque left <see cref="ChequeStatus.Cleared"/> really
        /// has been banked and cleared. Only the states the reader distinguishes are
        /// supported.
        /// </remarks>
        internal async Task SaveChequeAsync(
            string number,
            ChequeDirection direction,
            DateOnly instrumentDate,
            decimal amount,
            ChequeStatus status = ChequeStatus.Pending,
            DateOnly? recordedOn = null,
            Ledger? party = null,
            string? drawnOnBank = null)
        {
            await using ErpDbContext context = CreateContext();

            LedgerId partyId = (party
                ?? (direction == ChequeDirection.Received ? Customer : Supplier)).Id;

            // An issued cheque is drawn on a known account from the start; a received
            // one names no firm account until it is banked.
            LedgerId? bankLedgerId =
                direction == ChequeDirection.Issued ? BankAccount.Id : null;

            Cheque cheque = Cheque.Record(
                _tenantId, _firmId, direction, partyId, VoucherId, number,
                instrumentDate, recordedOn ?? instrumentDate,
                Money.Of(amount, CurrencyCode.Qar), bankLedgerId, drawnOnBank).Value;

            DriveTo(cheque, status, instrumentDate);

            context.Cheques.Add(cheque);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>Walks a freshly recorded cheque to a target status.</summary>
        /// <param name="cheque">The cheque.</param>
        /// <param name="status">Where it should end up.</param>
        /// <param name="on">The date each step takes effect.</param>
        private void DriveTo(Cheque cheque, ChequeStatus status, DateOnly on)
        {
            if (status == ChequeStatus.Pending)
            {
                return;
            }

            cheque.Deposit(BankAccount.Id, on).IsSuccess.ShouldBeTrue();

            switch (status)
            {
                case ChequeStatus.Deposited:
                    break;
                case ChequeStatus.Cleared:
                    cheque.Clear(on, VoucherId).IsSuccess.ShouldBeTrue();
                    break;
                case ChequeStatus.Bounced:
                    cheque.Bounce("Insufficient funds", on).IsSuccess.ShouldBeTrue();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(status), status, "Unsupported cheque status for these tests.");
            }
        }

        /// <summary>Reads by the date on the cheque's face: the PDC report and calendar.</summary>
        internal Task<IReadOnlyList<ChequeReportRow>> ByFaceDateAsync(
            DateOnly from,
            DateOnly to,
            ChequeDirection? direction = null,
            ChequeStatus? status = null,
            bool openOnly = false,
            Ledger? party = null,
            FirmId? firmId = null) =>
            ReadAsync(from, to, true, direction, status, openOnly, party, firmId);

        /// <summary>Reads by the date the cheque changed hands: the register.</summary>
        internal Task<IReadOnlyList<ChequeReportRow>> ByRecordedDateAsync(
            DateOnly from,
            DateOnly to,
            ChequeDirection? direction = null,
            ChequeStatus? status = null,
            bool openOnly = false,
            Ledger? party = null,
            FirmId? firmId = null) =>
            ReadAsync(from, to, false, direction, status, openOnly, party, firmId);

        private async Task<IReadOnlyList<ChequeReportRow>> ReadAsync(
            DateOnly from,
            DateOnly to,
            bool byInstrumentDate,
            ChequeDirection? direction,
            ChequeStatus? status,
            bool openOnly,
            Ledger? party,
            FirmId? firmId)
        {
            await using ErpDbContext context = CreateContext();

            return await new ChequeReportReader(context).ReadAsync(
                new ChequeReportCriteria(
                    firmId ?? _firmId, from, to, byInstrumentDate,
                    direction, status, openOnly, party?.Id),
                TestContext.Current.CancellationToken);
        }

        private ErpDbContext CreateContext() =>
            _fixture.CreateContext(PostgresFixture.ScopedTo(_tenantId));
    }
}
