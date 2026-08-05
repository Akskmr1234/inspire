using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using FluentValidation.Results;

namespace ERP.Application.Tests.Accounting.Reports;

/// <summary>Tests for <see cref="GetCashBankBookQueryHandler"/> and its validator.</summary>
public sealed class GetCashBankBookTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly DateOnly From = new(2026, 4, 1);
    private static readonly DateOnly To = new(2026, 4, 30);

    // ------------------------------------------------------------ account selection

    [Fact]
    public async Task The_cash_book_covers_cash_accounts_and_ignores_every_other_kind()
    {
        // The selection rule the whole report rests on. Were it wrong, a "cash
        // book" would list customers or tax heads.
        Firm firm = CreateFirm();

        Ledger till = Account(firm, "1000", "Cash in hand", LedgerKind.Cash);
        Ledger pettyCash = Account(firm, "1001", "Petty cash", LedgerKind.Cash);
        Ledger current = Account(firm, "1100", "Current account", LedgerKind.Bank);
        Ledger customer = Account(firm, "2000", "A customer", LedgerKind.Customer);

        ILedgerStatementReader reader = ReaderReturningEmpty();

        GetCashBankBookQueryHandler handler = new(
            reader,
            LedgersOf(firm, till, pettyCash, current, customer),
            FirmsContaining(firm),
            TenantScope(firm.Id));

        CashBankBookResponse response = (await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Cash),
            TestContext.Current.CancellationToken)).Value;

        response.Accounts.Select(a => a.LedgerCode).ShouldBe(["1000", "1001"]);
        response.Kind.ShouldBe(LedgerKind.Cash);
    }

    [Fact]
    public async Task The_bank_book_covers_bank_accounts_only()
    {
        Firm firm = CreateFirm();

        Ledger till = Account(firm, "1000", "Cash in hand", LedgerKind.Cash);
        Ledger current = Account(firm, "1100", "Current account", LedgerKind.Bank);

        GetCashBankBookQueryHandler handler = new(
            ReaderReturningEmpty(),
            LedgersOf(firm, till, current),
            FirmsContaining(firm),
            TenantScope(firm.Id));

        CashBankBookResponse response = (await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Bank),
            TestContext.Current.CancellationToken)).Value;

        response.Accounts.ShouldHaveSingleItem().LedgerCode.ShouldBe("1100");
    }

    [Fact]
    public async Task Accounts_are_ordered_by_code_so_the_report_is_stable()
    {
        Firm firm = CreateFirm();

        Ledger second = Account(firm, "1002", "Second till", LedgerKind.Cash);
        Ledger first = Account(firm, "1000", "First till", LedgerKind.Cash);

        GetCashBankBookQueryHandler handler = new(
            ReaderReturningEmpty(),
            LedgersOf(firm, second, first),
            FirmsContaining(firm),
            TenantScope(firm.Id));

        CashBankBookResponse response = (await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Cash),
            TestContext.Current.CancellationToken)).Value;

        response.Accounts.Select(a => a.LedgerCode).ShouldBe(["1000", "1002"]);
    }

    [Fact]
    public async Task A_single_account_can_be_singled_out()
    {
        Firm firm = CreateFirm();

        Ledger till = Account(firm, "1000", "Cash in hand", LedgerKind.Cash);
        Ledger petty = Account(firm, "1001", "Petty cash", LedgerKind.Cash);

        GetCashBankBookQueryHandler handler = new(
            ReaderReturningEmpty(),
            LedgersOf(firm, till, petty),
            FirmsContaining(firm),
            TenantScope(firm.Id));

        CashBankBookResponse response = (await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Cash, petty.Id.Value),
            TestContext.Current.CancellationToken)).Value;

        response.Accounts.ShouldHaveSingleItem().LedgerCode.ShouldBe("1001");
    }

    [Fact]
    public async Task Naming_an_account_of_the_wrong_kind_is_a_not_found_rather_than_an_empty_report()
    {
        // Asking the cash book for a bank account is a mistake, and an empty
        // report would look like "this till had no activity" instead of saying so.
        Firm firm = CreateFirm();

        Ledger till = Account(firm, "1000", "Cash in hand", LedgerKind.Cash);
        Ledger current = Account(firm, "1100", "Current account", LedgerKind.Bank);

        GetCashBankBookQueryHandler handler = new(
            ReaderReturningEmpty(),
            LedgersOf(firm, till, current),
            FirmsContaining(firm),
            TenantScope(firm.Id));

        Result<CashBankBookResponse> result = await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Cash, current.Id.Value),
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Ledger.NotFound");
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    // ------------------------------------------------------------ arithmetic

    [Fact]
    public async Task Receipts_add_to_the_running_balance_and_payments_take_from_it()
    {
        // Cash is an asset, so a debit is money arriving. Getting the sign wrong
        // would present a till that is overdrawn as one that is flush.
        Firm firm = CreateFirm();
        Ledger till = Account(firm, "1000", "Cash in hand", LedgerKind.Cash);

        ILedgerStatementReader reader = ReaderReturning(new LedgerStatementData(
            "1000",
            "Cash in hand",
            "Current Assets",
            OpeningBalance: 250m,
            Postings:
            [
                Posting(new DateOnly(2026, 4, 2), EntrySide.Debit, 500m),
                Posting(new DateOnly(2026, 4, 3), EntrySide.Credit, 120m),
                Posting(new DateOnly(2026, 4, 4), EntrySide.Debit, 80m),
            ]));

        GetCashBankBookQueryHandler handler = new(
            reader, LedgersOf(firm, till), FirmsContaining(firm), TenantScope(firm.Id));

        CashBankBookAccount account = (await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Cash),
            TestContext.Current.CancellationToken)).Value.Accounts.ShouldHaveSingleItem();

        account.OpeningBalance.ShouldBe(250m);
        account.TotalReceipts.ShouldBe(580m);
        account.TotalPayments.ShouldBe(120m);

        // 250 + 500 - 120 + 80
        account.ClosingBalance.ShouldBe(710m);
        account.Lines.Select(l => l.RunningBalance).ShouldBe([750m, 630m, 710m]);
    }

    [Fact]
    public async Task Grand_totals_are_the_sum_across_every_account()
    {
        Firm firm = CreateFirm();

        Ledger till = Account(firm, "1000", "Cash in hand", LedgerKind.Cash);
        Ledger petty = Account(firm, "1001", "Petty cash", LedgerKind.Cash);

        ILedgerStatementReader reader = Substitute.For<ILedgerStatementReader>();

        reader.ReadAsync(till.Id, firm.Id, From, To, Arg.Any<CancellationToken>())
            .Returns(new LedgerStatementData("1000", "Cash in hand", "Current Assets", 100m,
                [Posting(From, EntrySide.Debit, 400m)]));

        reader.ReadAsync(petty.Id, firm.Id, From, To, Arg.Any<CancellationToken>())
            .Returns(new LedgerStatementData("1001", "Petty cash", "Current Assets", 50m,
                [Posting(From, EntrySide.Credit, 30m)]));

        GetCashBankBookQueryHandler handler = new(
            reader, LedgersOf(firm, till, petty), FirmsContaining(firm), TenantScope(firm.Id));

        CashBankBookResponse response = (await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Cash),
            TestContext.Current.CancellationToken)).Value;

        response.TotalOpeningBalance.ShouldBe(150m);
        response.TotalReceipts.ShouldBe(400m);
        response.TotalPayments.ShouldBe(30m);
        response.TotalClosingBalance.ShouldBe(520m);
    }

    [Fact]
    public async Task An_account_with_no_movement_still_appears_with_its_balance()
    {
        // A till that saw no activity is a fact worth reporting, not a row to hide.
        // Its opening balance is still money the firm holds.
        Firm firm = CreateFirm();
        Ledger till = Account(firm, "1000", "Cash in hand", LedgerKind.Cash);

        ILedgerStatementReader reader = ReaderReturning(new LedgerStatementData(
            "1000", "Cash in hand", "Current Assets", 425m, []));

        GetCashBankBookQueryHandler handler = new(
            reader, LedgersOf(firm, till), FirmsContaining(firm), TenantScope(firm.Id));

        CashBankBookAccount account = (await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Cash),
            TestContext.Current.CancellationToken)).Value.Accounts.ShouldHaveSingleItem();

        account.Lines.ShouldBeEmpty();
        account.OpeningBalance.ShouldBe(425m);
        account.ClosingBalance.ShouldBe(425m);
    }

    [Fact]
    public async Task A_firm_with_no_accounts_of_the_kind_reports_an_empty_book()
    {
        Firm firm = CreateFirm();

        GetCashBankBookQueryHandler handler = new(
            ReaderReturningEmpty(),
            LedgersOf(firm),
            FirmsContaining(firm),
            TenantScope(firm.Id));

        CashBankBookResponse response = (await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Bank),
            TestContext.Current.CancellationToken)).Value;

        response.Accounts.ShouldBeEmpty();
        response.TotalClosingBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task A_report_cannot_be_run_without_a_firm_selected()
    {
        GetCashBankBookQueryHandler handler = new(
            ReaderReturningEmpty(),
            Substitute.For<ILedgerRepository>(),
            Substitute.For<IFirmRepository>(),
            TenantScope(firmId: null));

        Result<CashBankBookResponse> result = await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Cash),
            TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("Report.NoFirmSelected");
    }

    [Fact]
    public async Task Deactivated_accounts_are_included()
    {
        // A till closed mid-period still moved money during it. Excluding it would
        // silently drop those movements from the book and from its totals.
        Firm firm = CreateFirm();
        Ledger till = Account(firm, "1000", "Old till", LedgerKind.Cash);

        ILedgerRepository ledgers = Substitute.For<ILedgerRepository>();
        ledgers.ListWithGroupAsync(firm.Id, false, Arg.Any<CancellationToken>())
            .Returns([(till, GroupFor(firm))]);

        GetCashBankBookQueryHandler handler = new(
            ReaderReturningEmpty(), ledgers, FirmsContaining(firm), TenantScope(firm.Id));

        CashBankBookResponse response = (await handler.Handle(
            new GetCashBankBookQuery(From, To, LedgerKind.Cash),
            TestContext.Current.CancellationToken)).Value;

        response.Accounts.ShouldHaveSingleItem();

        // activeOnly: false is the contract this report depends on.
        await ledgers.Received(1).ListWithGroupAsync(
            firm.Id, false, Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------ validation

    [Theory]
    [InlineData(LedgerKind.Cash, true)]
    [InlineData(LedgerKind.Bank, true)]
    [InlineData(LedgerKind.Customer, false)]
    [InlineData(LedgerKind.Supplier, false)]
    [InlineData(LedgerKind.General, false)]
    [InlineData(LedgerKind.Tax, false)]
    public void Only_cash_and_bank_accounts_have_a_book(LedgerKind kind, bool expected) =>
        Validate(new GetCashBankBookQuery(From, To, kind)).IsValid.ShouldBe(expected);

    [Fact]
    public void A_range_ending_before_it_starts_is_rejected()
    {
        DateOnly laterDate = To;
        DateOnly earlierDate = From;

        Validate(new GetCashBankBookQuery(laterDate, earlierDate, LedgerKind.Cash))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_range_longer_than_a_year_is_rejected()
    {
        GetCashBankBookQuery tooLong = new(
            From,
            From.AddDays(GetCashBankBookQueryValidator.MaximumRangeDays),
            LedgerKind.Cash);

        Validate(tooLong).IsValid.ShouldBeFalse();
    }

    // ------------------------------------------------------------ helpers

    private static ValidationResult Validate(GetCashBankBookQuery query) =>
        new GetCashBankBookQueryValidator().Validate(query);

    private static Firm CreateFirm() => Firm.Create(
        Tenant, "ACME", "Acme Trading", CurrencyCode.Qar,
        TaxRegime.GccVat, "Asia/Qatar").Value;

    private static AccountGroup GroupFor(Firm firm) => AccountGroup.CreateRoot(
        firm.TenantId, firm.Id, "CA", "Current Assets", AccountNature.Asset).Value;

    private static Ledger Account(Firm firm, string code, string name, LedgerKind kind) =>
        Ledger.Create(GroupFor(firm), code, name, kind, firm.BaseCurrency).Value;

    private static IFirmRepository FirmsContaining(Firm firm)
    {
        IFirmRepository firms = Substitute.For<IFirmRepository>();
        firms.FindAsync(firm.Id, Arg.Any<CancellationToken>()).Returns(firm);
        return firms;
    }

    private static ILedgerRepository LedgersOf(Firm firm, params Ledger[] ledgers)
    {
        ILedgerRepository repository = Substitute.For<ILedgerRepository>();
        AccountGroup group = GroupFor(firm);

        repository.ListWithGroupAsync(firm.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([.. ledgers.Select(l => (l, group))]);

        return repository;
    }

    private static ILedgerStatementReader ReaderReturningEmpty() =>
        ReaderReturning(new LedgerStatementData("-", "-", "-", 0m, []));

    private static ILedgerStatementReader ReaderReturning(LedgerStatementData data)
    {
        ILedgerStatementReader reader = Substitute.For<ILedgerStatementReader>();

        reader.ReadAsync(
                Arg.Any<LedgerId>(), Arg.Any<FirmId>(),
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(data);

        return reader;
    }

    private static LedgerPosting Posting(DateOnly date, EntrySide side, decimal amount) =>
        new(date, Guid.CreateVersion7(), "CR/2026/0001", VoucherType.CashReceipt,
            null, null, ["Sales Account"], side, amount);

    private static ITenantContext TenantScope(FirmId? firmId)
    {
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.IsResolved.Returns(true);
        tenant.TenantId.Returns(Tenant);
        tenant.FirmId.Returns(firmId);
        return tenant;
    }
}
