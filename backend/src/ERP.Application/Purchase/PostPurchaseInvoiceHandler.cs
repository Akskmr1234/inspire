using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Vouchers;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Purchase;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Purchase;

/// <summary>Handles <see cref="PostPurchaseInvoiceCommand"/>.</summary>
/// <remarks>
/// <para>
/// The mirror of the sales posting, and the place the goods-received model finally
/// closes: a receipt puts the stock on the shelf and credits the clearing account, the
/// journal debits it back and credits the supplier, and a bill puts the debt into the
/// creditors. Four aggregates, one transaction, because any subset of them is a
/// discrepancy somebody would have to find by hand.
/// </para>
/// <para>
/// The stock comes in through an ordinary receipt rather than by reaching into the
/// positions - and, unlike the sales side, through the ordinary stock <em>command</em>
/// as well. A purchase is where batches are opened and serial numbers are written down
/// for the first time, and section 10's machinery for that already exists behind
/// <see cref="CreateStockDocumentCommand"/>. Assembling the document by hand here would
/// be a second implementation of it to keep in step with the first.
/// </para>
/// </remarks>
public sealed class PostPurchaseInvoiceCommandHandler
    : ICommandHandler<PostPurchaseInvoiceCommand, PostPurchaseInvoiceResponse>
{
    private readonly IPurchaseInvoiceRepository _invoices;
    private readonly IStockDocumentRepository _documents;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ISerialNumberRepository _serials;
    private readonly IInventoryAccountMapRepository _accounts;
    private readonly ITaxAccountMapRepository _taxAccounts;
    private readonly ILedgerRepository _ledgers;
    private readonly IBillRepository _bills;
    private readonly IVoucherRepository _vouchers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly StockPoster _poster;
    private readonly StockJournalPoster _stockJournal;

    /// <summary>Initialises a new instance of the <see cref="PostPurchaseInvoiceCommandHandler"/> class.</summary>
    /// <param name="invoices">The purchase invoice repository.</param>
    /// <param name="documents">The stock document repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="serials">The serial-number repository.</param>
    /// <param name="balances">The stock balance repository.</param>
    /// <param name="batchBalances">The batch position repository.</param>
    /// <param name="ledger">The stock ledger repository.</param>
    /// <param name="accounts">The inventory account map repository.</param>
    /// <param name="taxAccounts">The tax account map repository.</param>
    /// <param name="ledgers">The nominal ledger repository.</param>
    /// <param name="bills">The bill repository.</param>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public PostPurchaseInvoiceCommandHandler(
        IPurchaseInvoiceRepository invoices,
        IStockDocumentRepository documents,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ISerialNumberRepository serials,
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IStockLedgerRepository ledger,
        IInventoryAccountMapRepository accounts,
        ITaxAccountMapRepository taxAccounts,
        ILedgerRepository ledgers,
        IBillRepository bills,
        IVoucherRepository vouchers,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears,
        IFirmRepository firms,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _documents = documents;
        _masters = masters;
        _products = products;
        _batches = batches;
        _serials = serials;
        _accounts = accounts;
        _taxAccounts = taxAccounts;
        _ledgers = ledgers;
        _bills = bills;
        _vouchers = vouchers;
        _numbering = numbering;
        _financialYears = financialYears;
        _firms = firms;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _poster = new StockPoster(balances, batchBalances, ledger);
        _stockJournal = new StockJournalPoster(accounts, vouchers, numbering, financialYears);
    }

    /// <inheritdoc />
    public async Task<Result<PostPurchaseInvoiceResponse>> Handle(
        PostPurchaseInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<PostPurchaseInvoiceResponse>(Error.Forbidden(
                "PurchaseInvoice.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before posting a purchase."));
        }

        PurchaseInvoice? found = await _invoices.FindAsync(
            PurchaseInvoiceId.From(request.PurchaseInvoiceId), cancellationToken);

        // The firm is checked rather than trusted to the query filter. A firm is a
        // division within a tenant, and nothing in the database stops one firm's user
        // naming another's document.
        if (found is null || found.FirmId != firmId)
        {
            return Result.Failure<PostPurchaseInvoiceResponse>(Error.NotFound(
                "PurchaseInvoice.NotFound",
                "That purchase does not exist in the selected firm."));
        }

        PurchaseInvoice invoice = found;

        Result<Context> loaded = await LoadAsync(invoice, firmId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<PostPurchaseInvoiceResponse>(loaded.Error);
        }

        // The document's own gate first. It is the cheapest of the checks and the one
        // whose failure means nothing should have been attempted at all.
        Result posted = invoice.Post(_currentUser.UserId, _clock.UtcNow);

        if (posted.IsFailure)
        {
            return Result.Failure<PostPurchaseInvoiceResponse>(posted.Error);
        }

        Result<StockDocument> received = await ReceiveAsync(
            invoice, loaded.Value, firmId, branchId, cancellationToken);

        if (received.IsFailure)
        {
            return Result.Failure<PostPurchaseInvoiceResponse>(received.Error);
        }

        Result<Voucher> journalled = await JournalAsync(
            invoice, loaded.Value, branchId, cancellationToken);

        if (journalled.IsFailure)
        {
            return Result.Failure<PostPurchaseInvoiceResponse>(journalled.Error);
        }

        Result<Bill?> billed = await SettleAsync(
            invoice, loaded.Value, journalled.Value, request.CreditDays, cancellationToken);

        if (billed.IsFailure)
        {
            return Result.Failure<PostPurchaseInvoiceResponse>(billed.Error);
        }

        Result recorded = invoice.RecordPosting(
            received.Value.Id, billed.Value?.Id, journalled.Value.Id);

        if (recorded.IsFailure)
        {
            return Result.Failure<PostPurchaseInvoiceResponse>(recorded.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PostPurchaseInvoiceResponse(
            invoice.Id.Value,
            invoice.Number,
            received.Value.Id.Value,
            received.Value.Number,
            billed.Value?.Id.Value,
            journalled.Value.Id.Value,
            invoice.Total.Amount));
    }

    /// <summary>Loads what the journal and the bill will need, before either of them runs.</summary>
    /// <remarks>
    /// The stock side loads its own, through the same loader every stock document uses.
    /// Duplicating that here would be two lists of products to keep in agreement.
    /// </remarks>
    private async Task<Result<Context>> LoadAsync(
        PurchaseInvoice invoice,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            firmId, invoice.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<Context>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {invoice.Date:yyyy-MM-dd}."));
        }

        Ledger? supplier = await _ledgers.FindAsync(invoice.SupplierLedgerId, cancellationToken);

        if (supplier is null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "PurchaseInvoice.SupplierNotFound", "The supplier account no longer exists."));
        }

        InventoryAccountMap? accounts = await _accounts.FindAsync(firmId, cancellationToken);

        if (accounts is null)
        {
            return Result.Failure<Context>(Error.BusinessRule(
                "InventoryAccounts.NotConfigured",
                "This firm has not chosen which accounts stock and purchases post to."));
        }

        TaxAccountMap? taxAccounts = await _taxAccounts.FindAsync(firmId, cancellationToken);

        return taxAccounts is null
            ? Result.Failure<Context>(Error.BusinessRule(
                "TaxAccounts.NotConfigured",
                "This firm has not chosen which accounts its tax heads post to."))
            : Result.Success(new Context(firm, year, supplier, accounts, taxAccounts));
    }

    /// <summary>Raises the receipt that puts the goods on the shelf, and posts it.</summary>
    /// <remarks>
    /// Numbered from a series of its own so a stock ledger distinguishes goods that came
    /// from a supplier from goods that came back from a department. Its journal -
    /// inventory debited, goods received credited - is raised by the same poster every
    /// other stock document uses, which is why this handler never mentions inventory.
    /// </remarks>
    private async Task<Result<StockDocument>> ReceiveAsync(
        PurchaseInvoice invoice,
        Context context,
        FirmId firmId,
        BranchId branchId,
        CancellationToken cancellationToken)
    {
        CreateStockDocumentCommand movement = PurchaseReceipt.Describe(invoice);

        Result<StockContext> stock = await StockLoader.LoadAsync(
            movement, firmId, _firms, _financialYears, _masters, _products, cancellationToken);

        if (stock.IsFailure)
        {
            return Result.Failure<StockDocument>(stock.Error);
        }

        Result<string> number = await ReserveReceiptNumberAsync(
            invoice, movement.Type, branchId, context.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<StockDocument>(number.Error);
        }

        Result<StockAssembly> built = await StockLoader.BuildAsync(
            movement, invoice.TenantId, firmId, number.Value, stock.Value, _batches, _serials,
            cancellationToken);

        if (built.IsFailure)
        {
            return Result.Failure<StockDocument>(built.Error);
        }

        StockDocument document = built.Value.Document;

        Result opened = document.Post(_currentUser.UserId, _clock.UtcNow);

        if (opened.IsFailure)
        {
            return Result.Failure<StockDocument>(opened.Error);
        }

        Result<IReadOnlyList<StockLedgerEntry>> applied = await _poster.ApplyAsync(
            document, stock.Value.Products, built.Value.Batches, built.Value.Serials,
            context.Firm.BaseCurrency, _clock.UtcNow, cancellationToken);

        if (applied.IsFailure)
        {
            return Result.Failure<StockDocument>(applied.Error);
        }

        Result journalled = await _stockJournal.RaiseAsync(
            document, applied.Value, context.Firm, branchId, _currentUser.UserId,
            _clock.UtcNow, cancellationToken);

        if (journalled.IsFailure)
        {
            return Result.Failure<StockDocument>(journalled.Error);
        }

        _documents.Add(document);

        return Result.Success(document);
    }

    /// <summary>Raises and posts the journal the purchase owes the nominal ledger.</summary>
    private async Task<Result<Voucher>> JournalAsync(
        PurchaseInvoice invoice,
        Context context,
        BranchId branchId,
        CancellationToken cancellationToken)
    {
        Result<string> number = await JournalNumbering.ReserveAsync(
            _numbering, invoice.TenantId, invoice.FirmId, branchId, context.Year,
            cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<Voucher>(number.Error);
        }

        Result<Voucher> raised = PurchaseJournal.Raise(
            invoice, context.Accounts, context.TaxAccounts, context.Firm, context.Year,
            number.Value);

        if (raised.IsFailure)
        {
            return raised;
        }

        Result posted = raised.Value.Post(_currentUser.UserId, _clock.UtcNow);

        if (posted.IsFailure)
        {
            return Result.Failure<Voucher>(posted.Error);
        }

        _vouchers.Add(raised.Value);

        return raised;
    }

    /// <summary>Puts what the firm owes into the supplier's outstanding.</summary>
    /// <remarks>
    /// Against the journal rather than the document, because that is what every other
    /// bill in this system points at and what the settlement machinery already
    /// understands: a payment allocates against bills, and a bill names the voucher that
    /// raised it.
    /// </remarks>
    private async Task<Result<Bill?>> SettleAsync(
        PurchaseInvoice invoice,
        Context context,
        Voucher journal,
        int? creditDays,
        CancellationToken cancellationToken)
    {
        if (invoice.IsReturn)
        {
            Result debited = await DebitAsync(invoice, journal, cancellationToken);

            return debited.IsFailure
                ? Result.Failure<Bill?>(debited.Error)
                : Result.Success<Bill?>(null);
        }

        Result<Bill> raised = Bill.Raise(
            invoice.TenantId,
            invoice.FirmId,
            invoice.SupplierLedgerId,
            journal.Id,
            BillType.Payable,
            invoice.SupplierInvoiceNumber ?? invoice.Number,
            invoice.Date,
            creditDays ?? context.Supplier.CreditDays ?? 0,
            invoice.Total);

        if (raised.IsFailure)
        {
            return Result.Failure<Bill?>(raised.Error);
        }

        _bills.Add(raised.Value);

        return Result.Success<Bill?>(raised.Value);
    }

    /// <summary>Sets a return's debit against the bill the purchase raised.</summary>
    /// <remarks>
    /// Where the return names a purchase, its debit is allocated against that purchase's
    /// bill exactly as a payment would be - which is what makes the bill-wise reports show
    /// the purchase as settled by the goods going back rather than as still owing.
    /// <para>
    /// Whatever cannot be matched is left as a debit on the supplier's account: a return
    /// naming no purchase, or one worth more than the bill still owes because part of it
    /// was already paid. The journal has debited the ledger either way, so the supplier's
    /// balance is right immediately; what is missing is only the link to a document, and
    /// refusing the return over that would turn a bookkeeping detail into a lorry that
    /// cannot leave the yard.
    /// </para>
    /// </remarks>
    private async Task<Result> DebitAsync(
        PurchaseInvoice invoice,
        Voucher journal,
        CancellationToken cancellationToken)
    {
        if (invoice.ReturnsInvoiceId is not { } originalId)
        {
            return Result.Success();
        }

        PurchaseInvoice? original = await _invoices.FindAsync(originalId, cancellationToken);

        if (original?.BillId is not { } billId)
        {
            return Result.Success();
        }

        IReadOnlyDictionary<BillId, Bill> found = await _bills.GetManyAsync(
            [billId], cancellationToken);

        if (!found.TryGetValue(billId, out Bill? bill) || bill.Status == BillStatus.Cancelled)
        {
            return Result.Success();
        }

        Money allocatable = invoice.Total < bill.OutstandingAmount
            ? invoice.Total
            : bill.OutstandingAmount;

        return allocatable.IsPositive
            ? bill.Allocate(journal.Id, allocatable, invoice.Date)
            : Result.Success();
    }

    /// <summary>Takes the next number for the receipt, creating the series if there is none.</summary>
    private async Task<Result<string>> ReserveReceiptNumberAsync(
        PurchaseInvoice invoice,
        StockDocumentType kind,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        string documentType = DocumentTypes.ForStockDocument(kind);

        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            documentType, invoice.FirmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                invoice.TenantId, invoice.FirmId, documentType, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat(
                prefix: invoice.IsReturn ? "PRN" : "PR",
                suffix: null,
                separator: "/",
                financialYearLabel: year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }

    /// <summary>What the journal and the bill need, loaded once before either moves.</summary>
    private sealed record Context(
        Firm Firm,
        FinancialYear Year,
        Ledger Supplier,
        InventoryAccountMap Accounts,
        TaxAccountMap TaxAccounts);
}
