using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Tests.Sales;

/// <summary>
/// A working <see cref="PostSalesInvoiceCommandHandler"/> with every dependency
/// substituted and wired to succeed, so each test changes only the one thing it is about.
/// </summary>
/// <remarks>
/// More setup than any other handler in this suite needs, and unavoidably so: posting a
/// sale touches four aggregates and reads a firm's chart, its two account maps, its stock
/// positions and its numbering. A fixture that stubbed any of them into always-succeeding
/// would be testing the handler's control flow rather than the sale.
/// </remarks>
internal sealed class SalesPostingFixture
{
    /// <summary>The tenant every fixture posts under.</summary>
    internal static readonly TenantId Tenant = TenantId.NewId();

    /// <summary>The date the fixture's invoices are dated.</summary>
    internal static readonly DateOnly InvoiceDate = new(2026, 6, 15);

    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<LedgerId, Ledger> _ledgers = [];
    private readonly Dictionary<ProductId, Product> _products = [];
    private readonly Dictionary<UnitOfMeasureId, UnitOfMeasure> _units = [];
    private readonly Dictionary<ProductId, StockBalance> _positions = [];
    private readonly Dictionary<string, NumberingSeries> _series = [];
    private readonly PostSalesInvoiceCommandHandler _handler;
    private SalesInvoice? _invoice;

    /// <summary>Initialises a new instance of the <see cref="SalesPostingFixture"/> class.</summary>
    /// <param name="firmSelected">Whether a firm and branch are in scope.</param>
    /// <param name="onHand">How much of the product the warehouse holds.</param>
    /// <param name="unitCost">What one unit of it cost.</param>
    /// <param name="taxAccountAssigned">Whether output VAT has an account.</param>
    internal SalesPostingFixture(
        bool firmSelected = true,
        decimal onHand = 100m,
        decimal unitCost = 60m,
        bool taxAccountAssigned = true)
    {
        Firm = Domain.Tenancy.Firm.Create(
            Tenant, "ACME", "Acme Trading", CurrencyCode.Qar,
            TaxRegime.GccVat, "Asia/Qatar").Value;

        BranchId = SharedKernel.Tenancy.BranchId.NewId();

        FinancialYear = Domain.Tenancy.FinancialYear.Create(
            Tenant, Firm.Id, "2026",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

        Warehouse = Domain.Inventory.Warehouse.Create(Tenant, Firm.Id, "MAIN", "Main store").Value;

        Customer = LedgerIn("1200", "Al Mansoor Trading", LedgerKind.Customer);
        Customer.SetBillWise(true);
        Customer.SetCreditTerms(creditLimit: null, creditDays: 30);

        Accounts = InventoryAccountMap.Create(Tenant, Firm.Id);

        foreach (StockAccount account in Enum.GetValues<StockAccount>())
        {
            Ledger ledger = LedgerIn($"{(int)account}000", $"{account}", LedgerKind.General);
            AccountLedgers[account] = ledger;
            Accounts.Assign(account, ledger).IsSuccess.ShouldBeTrue();
        }

        TaxAccounts = TaxAccountMap.Create(Tenant, Firm.Id);
        OutputVat = LedgerIn("2300", "Output VAT", LedgerKind.Tax);

        if (taxAccountAssigned)
        {
            TaxAccounts.Assign(TaxComponentType.Vat, TaxDirection.Output, OutputVat)
                .IsSuccess.ShouldBeTrue();
        }

        Unit = UnitOfMeasure.CreateBase(Tenant, Firm.Id, "EACH", "Each").Value;
        _units[Unit.Id] = Unit;

        Product = Domain.Inventory.Product.Create(
            Category.CreateRoot(Tenant, Firm.Id, "GEN", "General").Value,
            Unit, "PRO-0001", "A thing", ItemType.Stock, CurrencyCode.Qar).Value;
        _products[Product.Id] = Product;

        if (onHand > 0m)
        {
            StockBalance position = StockBalance.Open(
                Tenant, Firm.Id, Product.Id, Warehouse.Id, CurrencyCode.Qar);

            position.Receive(onHand, unitCost, Now.AddDays(-1)).IsSuccess.ShouldBeTrue();
            _positions[Product.Id] = position;
        }

        Invoices = Substitute.For<ISalesInvoiceRepository>();
        Invoices
            .FindAsync(Arg.Any<SalesInvoiceId>(), Arg.Any<CancellationToken>())
            .Returns(_ => _invoice);

        Documents = Substitute.For<IStockDocumentRepository>();
        Documents.When(d => d.Add(Arg.Any<StockDocument>()))
            .Do(call => Issued.Add(call.Arg<StockDocument>()!));

        Masters = Substitute.For<IInventoryMasterRepository>();
        Masters.FindWarehouseAsync(Warehouse.Id, Arg.Any<CancellationToken>()).Returns(Warehouse);
        Masters
            .GetUnitsAsync(
                Arg.Any<IReadOnlyCollection<UnitOfMeasureId>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _units);

        Products = Substitute.For<IProductRepository>();
        Products
            .GetManyAsync(Arg.Any<IReadOnlyCollection<ProductId>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _products);

        Batches = Substitute.For<IBatchRepository>();
        Batches
            .GetManyAsync(Arg.Any<IReadOnlyCollection<BatchId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<BatchId, Batch>());

        Serials = Substitute.For<ISerialNumberRepository>();
        Serials
            .GetManyAsync(
                Arg.Any<IReadOnlyCollection<SerialNumberId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<SerialNumberId, SerialNumber>());

        Balances = Substitute.For<IStockBalanceRepository>();
        Balances
            .GetPositionsAsync(
                Arg.Any<FirmId>(), Arg.Any<WarehouseId>(),
                Arg.Any<IReadOnlyCollection<ProductId>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _positions);

        BatchBalances = Substitute.For<IBatchBalanceRepository>();
        BatchBalances
            .GetPositionsAsync(
                Arg.Any<FirmId>(), Arg.Any<WarehouseId>(),
                Arg.Any<IReadOnlyCollection<BatchId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<BatchId, BatchBalance>());

        StockLedger = Substitute.For<IStockLedgerRepository>();
        StockLedger.When(l => l.Add(Arg.Any<StockLedgerEntry>()))
            .Do(call => Movements.Add(call.Arg<StockLedgerEntry>()!));

        AccountMaps = Substitute.For<IInventoryAccountMapRepository>();
        AccountMaps.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Accounts);

        TaxAccountMaps = Substitute.For<ITaxAccountMapRepository>();
        TaxAccountMaps.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(TaxAccounts);

        Ledgers = Substitute.For<ILedgerRepository>();
        Ledgers
            .FindAsync(Arg.Any<LedgerId>(), Arg.Any<CancellationToken>())
            .Returns(call => _ledgers.GetValueOrDefault(call.ArgAt<LedgerId>(0)));

        Bills = Substitute.For<IBillRepository>();
        Bills.When(b => b.Add(Arg.Any<Bill>())).Do(call => Raised.Add(call.Arg<Bill>()!));

        Vouchers = Substitute.For<IVoucherRepository>();
        Vouchers.When(v => v.Add(Arg.Any<Voucher>()))
            .Do(call => Journals.Add(call.Arg<Voucher>()!));

        // Nothing until the handler creates it, which is what a firm's first sale
        // actually meets - and the only path on which the prefix is ever set.
        Numbering = Substitute.For<INumberingSeriesRepository>();
        Numbering
            .FindForUpdateAsync(
                Arg.Any<string>(), Arg.Any<FirmId>(), Arg.Any<BranchId>(),
                Arg.Any<FinancialYearId>(), Arg.Any<CancellationToken>())
            .Returns(call => _series.GetValueOrDefault(call.ArgAt<string>(0)));
        Numbering.When(n => n.Add(Arg.Any<NumberingSeries>()))
            .Do(call =>
            {
                NumberingSeries added = call.Arg<NumberingSeries>()!;
                _series[added.DocumentType] = added;
            });

        Firms = Substitute.For<IFirmRepository>();
        Firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

        FinancialYears = Substitute.For<IFinancialYearRepository>();
        FinancialYears
            .FindContainingAsync(Firm.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(FinancialYear);

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

        _handler = new PostSalesInvoiceCommandHandler(
            Invoices, Documents, Masters, Products, Batches, Serials, Balances, BatchBalances,
            StockLedger, AccountMaps, TaxAccountMaps, Ledgers, Bills, Vouchers, Numbering,
            FinancialYears, Firms, tenant, user, clock, UnitOfWork);
    }

    /// <summary>Gets the firm every posting lands in.</summary>
    internal Firm Firm { get; }

    /// <summary>Gets the branch every posting lands in.</summary>
    internal BranchId BranchId { get; }

    /// <summary>Gets the open financial year.</summary>
    internal FinancialYear FinancialYear { get; }

    /// <summary>Gets the warehouse goods leave.</summary>
    internal Warehouse Warehouse { get; }

    /// <summary>Gets the customer billed, on 30-day terms.</summary>
    internal Ledger Customer { get; }

    /// <summary>Gets the product sold.</summary>
    internal Product Product { get; }

    /// <summary>Gets the unit it is counted in.</summary>
    internal UnitOfMeasure Unit { get; }

    /// <summary>Gets the firm's account map.</summary>
    internal InventoryAccountMap Accounts { get; }

    /// <summary>Gets the firm's tax account map.</summary>
    internal TaxAccountMap TaxAccounts { get; }

    /// <summary>Gets the ledger each kind of posting lands in.</summary>
    internal Dictionary<StockAccount, Ledger> AccountLedgers { get; } = [];

    /// <summary>Gets the output VAT account.</summary>
    internal Ledger OutputVat { get; }

    /// <summary>Gets the sales invoice repository.</summary>
    internal ISalesInvoiceRepository Invoices { get; }

    /// <summary>Gets the stock document repository.</summary>
    internal IStockDocumentRepository Documents { get; }

    /// <summary>Gets the inventory master repository.</summary>
    internal IInventoryMasterRepository Masters { get; }

    /// <summary>Gets the product repository.</summary>
    internal IProductRepository Products { get; }

    /// <summary>Gets the batch repository.</summary>
    internal IBatchRepository Batches { get; }

    /// <summary>Gets the serial-number repository.</summary>
    internal ISerialNumberRepository Serials { get; }

    /// <summary>Gets the stock balance repository.</summary>
    internal IStockBalanceRepository Balances { get; }

    /// <summary>Gets the batch position repository.</summary>
    internal IBatchBalanceRepository BatchBalances { get; }

    /// <summary>Gets the stock ledger repository.</summary>
    internal IStockLedgerRepository StockLedger { get; }

    /// <summary>Gets the inventory account map repository.</summary>
    internal IInventoryAccountMapRepository AccountMaps { get; }

    /// <summary>Gets the tax account map repository.</summary>
    internal ITaxAccountMapRepository TaxAccountMaps { get; }

    /// <summary>Gets the nominal ledger repository.</summary>
    internal ILedgerRepository Ledgers { get; }

    /// <summary>Gets the bill repository.</summary>
    internal IBillRepository Bills { get; }

    /// <summary>Gets the voucher repository.</summary>
    internal IVoucherRepository Vouchers { get; }

    /// <summary>Gets the numbering-series repository.</summary>
    internal INumberingSeriesRepository Numbering { get; }

    /// <summary>Gets the firm repository.</summary>
    internal IFirmRepository Firms { get; }

    /// <summary>Gets the financial-year repository.</summary>
    internal IFinancialYearRepository FinancialYears { get; }

    /// <summary>Gets the unit of work.</summary>
    internal IUnitOfWork UnitOfWork { get; }

    /// <summary>Gets the issues the handler raised.</summary>
    internal List<StockDocument> Issued { get; } = [];

    /// <summary>Gets the bills the handler raised.</summary>
    internal List<Bill> Raised { get; } = [];

    /// <summary>Gets the vouchers the handler posted, sale and stock alike.</summary>
    internal List<Voucher> Journals { get; } = [];

    /// <summary>Gets the stock ledger entries the posting wrote.</summary>
    internal List<StockLedgerEntry> Movements { get; } = [];

    /// <summary>Gets the position of the product this fixture sells.</summary>
    internal StockBalance? Position => _positions.GetValueOrDefault(Product.Id);

    /// <summary>Puts a draft invoice in front of the handler.</summary>
    /// <param name="quantity">How many are sold.</param>
    /// <param name="rate">What one goes for.</param>
    /// <param name="tax">The tax on the line.</param>
    /// <param name="post">Whether to post it before the handler sees it.</param>
    /// <returns>The draft.</returns>
    internal SalesInvoice Draft(
        decimal quantity = 2m,
        decimal rate = 100m,
        decimal tax = 10m,
        bool post = false)
    {
        SalesInvoice invoice = SalesInvoice.CreateDraft(
            Tenant, Firm.Id, BranchId, FinancialYear, "SL/2026/0001", InvoiceDate,
            Customer, Warehouse, TaxMode.Tax, CurrencyCode.Qar).Value;

        decimal taxable = quantity * rate;

        invoice.AddLine(
            Product, Unit, quantity, quantity, rate,
            TaxCalculator.Calculate(
                Money.Of(taxable, CurrencyCode.Qar),
                TaxRate.FromTrusted(taxable == 0m ? 0m : decimal.Round(tax / taxable * 100m, 6)),
                new TaxContext(TaxRegime.GccVat, DocumentTaxMode.Taxable, false, false)))
            .IsSuccess.ShouldBeTrue();

        if (post)
        {
            invoice.Post(UserId.NewId(), Now).IsSuccess.ShouldBeTrue();
        }

        _invoice = invoice;

        return invoice;
    }

    /// <summary>Runs the handler against whatever draft is in place.</summary>
    /// <param name="creditDays">Terms stated on the command, if any.</param>
    /// <returns>What the handler made of it.</returns>
    internal Task<Result<PostSalesInvoiceResponse>> Post(int? creditDays = null) =>
        _handler.Handle(
            new PostSalesInvoiceCommand(
                (_invoice?.Id ?? SalesInvoiceId.NewId()).Value, creditDays),
            CancellationToken.None);

    /// <summary>Replaces the invoice the repository will return.</summary>
    /// <param name="invoice">The invoice, or nothing at all.</param>
    internal void Holds(SalesInvoice? invoice) => _invoice = invoice;

    /// <summary>What one ledger was debited, net of what it was credited, across all journals.</summary>
    /// <param name="ledgerId">The ledger.</param>
    /// <returns>Debits less credits.</returns>
    internal decimal Net(LedgerId ledgerId) =>
        Journals
            .SelectMany(journal => journal.Lines)
            .Where(line => line.LedgerId == ledgerId)
            .Sum(line => line.Side == EntrySide.Debit ? line.Amount.Amount : -line.Amount.Amount);

    private Ledger LedgerIn(string code, string name, LedgerKind kind)
    {
        AccountGroup group = AccountGroup.CreateRoot(
            Tenant, Firm.Id, $"G{code}", $"Group {code}", AccountNature.Asset).Value;

        Ledger ledger = Ledger.Create(group, code, name, kind, CurrencyCode.Qar).Value;
        _ledgers[ledger.Id] = ledger;

        return ledger;
    }
}
