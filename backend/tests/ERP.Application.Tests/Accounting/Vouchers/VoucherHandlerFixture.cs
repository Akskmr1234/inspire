using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Vouchers;
using ERP.Domain.Accounting;
using ERP.Domain.Numbering;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Tests.Accounting.Vouchers;

/// <summary>
/// A working <see cref="CreateVoucherCommandHandler"/> with every dependency
/// substituted and wired to succeed, so each test changes only the one thing it is
/// about.
/// </summary>
/// <remarks>
/// Shared by the posting tests and the bill-wise settlement tests. Both need the
/// same firm, branch, financial year, and chart of accounts to say anything at all,
/// and two copies of that setup would drift.
/// </remarks>
internal sealed class Fixture
{
    /// <summary>The tenant every fixture posts under.</summary>
    internal static readonly TenantId Tenant = TenantId.NewId();

    /// <summary>The date the fixture's vouchers are dated.</summary>
    internal static readonly DateOnly PostingDate = new(2026, 6, 15);

    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<LedgerId, Ledger> _ledgersById;
    private readonly Dictionary<BillId, Bill> _billsById = [];
    private readonly CreateVoucherCommandHandler _handler;

    /// <summary>Initialises a new instance of the <see cref="Fixture"/> class.</summary>
    /// <param name="firmSelected">Whether a firm and branch are in scope.</param>
    internal Fixture(bool firmSelected = true)
    {
        Firm = Domain.Tenancy.Firm.Create(
            Tenant, "ACME", "Acme Trading", CurrencyCode.Qar,
            TaxRegime.GccVat, "Asia/Qatar").Value;

        BranchId = SharedKernel.Tenancy.BranchId.NewId();

        FinancialYear = Domain.Tenancy.FinancialYear.Create(
            Tenant, Firm.Id, "2026",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

        CashLedger = LedgerIn(Firm, "1000", "Cash in hand", LedgerKind.Cash);
        BankLedger = LedgerIn(Firm, "1100", "Bank current", LedgerKind.Bank);
        SalesLedger = LedgerIn(Firm, "4000", "Sales", LedgerKind.General);

        CustomerLedger = LedgerIn(Firm, "2000", "Al Mansoor Trading", LedgerKind.Customer);
        CustomerLedger.SetBillWise(true);
        CustomerLedger.SetCreditTerms(creditLimit: null, creditDays: 30);

        SupplierLedger = LedgerIn(Firm, "3000", "Gulf Distributors", LedgerKind.Supplier);
        SupplierLedger.SetBillWise(true);
        SupplierLedger.SetCreditTerms(creditLimit: null, creditDays: 45);

        _ledgersById = new Dictionary<LedgerId, Ledger>
        {
            [CashLedger.Id] = CashLedger,
            [BankLedger.Id] = BankLedger,
            [SalesLedger.Id] = SalesLedger,
            [CustomerLedger.Id] = CustomerLedger,
            [SupplierLedger.Id] = SupplierLedger,
        };

        Ledgers = Substitute.For<ILedgerRepository>();
        Ledgers
            .GetManyAsync(Arg.Any<IEnumerable<LedgerId>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _ledgersById);

        Bills = Substitute.For<IBillRepository>();
        Bills
            .GetManyAsync(Arg.Any<IEnumerable<BillId>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<BillId>>()!
                .Where(_billsById.ContainsKey)
                .ToDictionary(id => id, id => _billsById[id]));
        Bills
            .FindExistingReferencesAsync(
                Arg.Any<FirmId>(), Arg.Any<LedgerId>(), Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => ReferencesInUse(
                call.ArgAt<LedgerId>(1), call.ArgAt<IEnumerable<string>>(2)));
        Bills.When(b => b.Add(Arg.Any<Bill>())).Do(call => Raised.Add(call.Arg<Bill>()!));

        Firms = Substitute.For<IFirmRepository>();
        Firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

        FinancialYears = Substitute.For<IFinancialYearRepository>();
        FinancialYears
            .FindContainingAsync(Firm.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(FinancialYear);

        NumberingSeries series = Domain.Numbering.NumberingSeries.Create(
            Tenant, Firm.Id, DocumentTypes.CashReceipt, BranchId, FinancialYear.Id).Value;

        Numbering = Substitute.For<INumberingSeriesRepository>();
        Numbering
            .FindForUpdateAsync(
                Arg.Any<string>(), Arg.Any<FirmId>(), Arg.Any<BranchId>(),
                Arg.Any<FinancialYearId>(), Arg.Any<CancellationToken>())
            .Returns(series);

        Vouchers = Substitute.For<IVoucherRepository>();
        UnitOfWork = Substitute.For<IUnitOfWork>();

        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.IsResolved.Returns(true);
        tenant.TenantId.Returns(Tenant);
        tenant.FirmId.Returns(firmSelected ? Firm.Id : null);
        tenant.BranchId.Returns(firmSelected ? BranchId : null);

        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(UserId.NewId());

        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        _handler = new CreateVoucherCommandHandler(
            Vouchers, Ledgers, Bills, FinancialYears, Numbering, Firms,
            tenant, user, clock, UnitOfWork);
    }

    /// <summary>Gets the firm every posting lands in.</summary>
    internal Firm Firm { get; }

    /// <summary>Gets the branch every posting lands in.</summary>
    internal BranchId BranchId { get; }

    /// <summary>Gets the open financial year.</summary>
    internal FinancialYear FinancialYear { get; }

    /// <summary>Gets a cash account.</summary>
    internal Ledger CashLedger { get; }

    /// <summary>Gets a bank account.</summary>
    internal Ledger BankLedger { get; }

    /// <summary>Gets an ordinary income ledger.</summary>
    internal Ledger SalesLedger { get; }

    /// <summary>Gets a bill-wise customer on 30-day terms.</summary>
    internal Ledger CustomerLedger { get; }

    /// <summary>Gets a bill-wise supplier on 45-day terms.</summary>
    internal Ledger SupplierLedger { get; }

    /// <summary>Gets the ledger repository.</summary>
    internal ILedgerRepository Ledgers { get; }

    /// <summary>Gets the bill repository.</summary>
    internal IBillRepository Bills { get; }

    /// <summary>Gets the firm repository.</summary>
    internal IFirmRepository Firms { get; }

    /// <summary>Gets the financial-year repository.</summary>
    internal IFinancialYearRepository FinancialYears { get; }

    /// <summary>Gets the numbering-series repository.</summary>
    internal INumberingSeriesRepository Numbering { get; }

    /// <summary>Gets the voucher repository.</summary>
    internal IVoucherRepository Vouchers { get; }

    /// <summary>Gets the unit of work.</summary>
    internal IUnitOfWork UnitOfWork { get; }

    /// <summary>Gets the bills the handler raised, in the order it raised them.</summary>
    internal List<Bill> Raised { get; } = [];

    /// <summary>Creates a ledger under a fresh current-assets group of a firm.</summary>
    /// <param name="firm">The owning firm.</param>
    /// <param name="code">The ledger code.</param>
    /// <param name="name">The ledger name.</param>
    /// <param name="kind">What the ledger represents.</param>
    /// <returns>The ledger.</returns>
    internal static Ledger LedgerIn(Firm firm, string code, string name, LedgerKind kind)
    {
        AccountGroup group = AccountGroup.CreateRoot(
            firm.TenantId, firm.Id, "CA", "Current Assets", AccountNature.Asset).Value;

        return Ledger.Create(group, code, name, kind, firm.BaseCurrency).Value;
    }

    /// <summary>Makes a ledger visible to the handler.</summary>
    /// <param name="ledger">The ledger to register.</param>
    internal void RegisterLedger(Ledger ledger) => _ledgersById[ledger.Id] = ledger;

    /// <summary>Makes a bill visible to the handler, as though already persisted.</summary>
    /// <param name="bill">The bill to register.</param>
    /// <returns>The same bill, for chaining.</returns>
    internal Bill RegisterBill(Bill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);

        _billsById[bill.Id] = bill;

        return bill;
    }

    /// <summary>Raises a bill against a party and registers it.</summary>
    /// <param name="party">The party the bill is owed by or to.</param>
    /// <param name="type">Receivable or payable.</param>
    /// <param name="number">The bill reference.</param>
    /// <param name="amount">The amount, in the firm's base currency.</param>
    /// <param name="billDate">The date it was raised. Defaults to the posting date.</param>
    /// <param name="creditDays">The credit period. Defaults to none.</param>
    /// <returns>The registered bill.</returns>
    internal Bill ExistingBill(
        Ledger party,
        BillType type,
        string number,
        decimal amount,
        DateOnly? billDate = null,
        int creditDays = 0)
    {
        ArgumentNullException.ThrowIfNull(party);

        return RegisterBill(Bill.Raise(
            Tenant,
            Firm.Id,
            party.Id,
            VoucherId.NewId(),
            type,
            number,
            billDate ?? PostingDate,
            creditDays,
            Money.Of(amount, Firm.BaseCurrency)).Value);
    }

    /// <summary>Dispatches a command to the handler under test.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The handler's result.</returns>
    internal Task<Result<CreateVoucherResponse>> Handle(CreateVoucherCommand command) =>
        _handler.Handle(command, TestContext.Current.CancellationToken);

    /// <summary>
    /// Answers the repository's uniqueness check from the bills already registered.
    /// </summary>
    /// <param name="ledgerId">The party.</param>
    /// <param name="numbers">The references being checked.</param>
    /// <returns>Those already in use.</returns>
    private HashSet<string> ReferencesInUse(LedgerId ledgerId, IEnumerable<string> numbers)
    {
        HashSet<string> requested = numbers.ToHashSet(StringComparer.Ordinal);

        return _billsById.Values
            .Where(b => b.LedgerId == ledgerId && requested.Contains(b.BillNumber))
            .Select(b => b.BillNumber)
            .ToHashSet(StringComparer.Ordinal);
    }
}
