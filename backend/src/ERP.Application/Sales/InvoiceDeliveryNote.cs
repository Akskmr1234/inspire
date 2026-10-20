using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Inventory.Masters;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Sales;

/// <summary>Raises a draft invoice billing the goods of a delivery note.</summary>
/// <param name="DeliveryNoteId">The note.</param>
/// <param name="Date">The invoice date; today where none is given.</param>
public sealed record InvoiceDeliveryNoteCommand(Guid DeliveryNoteId, DateOnly? Date = null)
    : ICommand<SalesInvoiceResponse>, ITransactional;

/// <summary>Handles <see cref="InvoiceDeliveryNoteCommand"/>.</summary>
/// <remarks>
/// What comes out is a <b>draft</b>, priced at each product's retail rate and taxed at its
/// tax rate, for somebody to check and post. Posting it moves nothing: the goods left with
/// the note, and the invoice only bills them.
/// </remarks>
public sealed class InvoiceDeliveryNoteCommandHandler
    : ICommandHandler<InvoiceDeliveryNoteCommand, SalesInvoiceResponse>
{
    private readonly IStockDocumentRepository _documents;
    private readonly ISalesInvoiceRepository _invoices;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ITaxSlabRepository _taxSlabs;
    private readonly ILedgerRepository _ledgers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="InvoiceDeliveryNoteCommandHandler"/> class.</summary>
    /// <param name="documents">The stock document repository.</param>
    /// <param name="invoices">The sales invoice repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="taxSlabs">The tax rate repository.</param>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public InvoiceDeliveryNoteCommandHandler(
        IStockDocumentRepository documents,
        ISalesInvoiceRepository invoices,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ITaxSlabRepository taxSlabs,
        ILedgerRepository ledgers,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears,
        IFirmRepository firms,
        ITenantContext tenantContext,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _documents = documents;
        _invoices = invoices;
        _masters = masters;
        _products = products;
        _batches = batches;
        _taxSlabs = taxSlabs;
        _ledgers = ledgers;
        _numbering = numbering;
        _financialYears = financialYears;
        _firms = firms;
        _tenantContext = tenantContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<SalesInvoiceResponse>> Handle(
        InvoiceDeliveryNoteCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.Forbidden(
                "SalesInvoice.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before raising an invoice."));
        }

        StockDocument? note = await _documents.FindAsync(
            StockDocumentId.From(request.DeliveryNoteId), cancellationToken);

        if (note is null || note.FirmId != firmId || note.Type != StockDocumentType.DeliveryNote
            || note.PartyLedgerId is not { } customerId)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.NotFound(
                "StockDocument.NoteNotFound", "No such delivery note in the selected firm."));
        }

        if (note.Status != StockDocumentStatus.Posted)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.BusinessRule(
                "StockDocument.NoteNotPosted",
                $"Delivery note '{note.Number}' is {note.Status}; only a posted note can be billed."));
        }

        if (await _invoices.FindBillingDeliveryNoteAsync(note.Id, null, cancellationToken) is { } billed)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.BusinessRule(
                "StockDocument.NoteAlreadyBilled",
                $"Delivery note '{note.Number}' is already billed by '{billed}'."));
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);
        DateOnly date = request.Date ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        FinancialYear? year = await _financialYears.FindContainingAsync(firmId, date, cancellationToken);
        Ledger? customer = await _ledgers.FindAsync(customerId, cancellationToken);
        Warehouse? warehouse = await _masters.FindWarehouseAsync(note.WarehouseId, cancellationToken);

        if (firm is null || year is null || customer is null || warehouse is null)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.BusinessRule(
                "SalesInvoice.CannotRaise",
                $"No financial year covers {date:yyyy-MM-dd}, or the note's customer or warehouse no longer exists."));
        }

        IReadOnlyDictionary<ProductId, Product> products = await _products.GetManyAsync(
            [.. note.Lines.Select(line => line.ProductId).Distinct()], cancellationToken);

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units = await _masters.GetUnitsAsync(
            [.. note.Lines.Select(line => line.UnitId).Distinct()], cancellationToken);

        IReadOnlyDictionary<BatchId, Batch> batches = await BatchResolver.ForDocumentAsync(
            note, _batches, cancellationToken);

        Dictionary<TaxSlabId, decimal> rates = (await _taxSlabs.ListAsync(firmId, true, cancellationToken))
            .ToDictionary(slab => slab.Id, slab => slab.Percentage);

        Result<string> number = await StockDocumentWriter.ReserveAsync(
            _numbering, _tenantContext.TenantId, DocumentTypes.SalesInvoice, firmId, branchId, year,
            cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(number.Error);
        }

        TaxMode mode = firm.TaxRegime == TaxRegime.None ? TaxMode.NonTax : TaxMode.Tax;

        Result<SalesInvoice> draft = SalesInvoice.CreateDraft(
            _tenantContext.TenantId, firmId, branchId, year, number.Value, date, customer, warehouse,
            mode, firm.BaseCurrency);

        if (draft.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(draft.Error);
        }

        SalesInvoice invoice = draft.Value;

        foreach (StockDocumentLine line in note.Lines.OrderBy(line => line.LineNumber))
        {
            Product product = products[line.ProductId];

            // The retail rate is per stock unit; the line is priced in the unit it was
            // delivered in.
            decimal rate = line.Quantity == 0m
                ? product.RetailRate.Amount
                : decimal.Round(product.RetailRate.Amount * line.StockQuantity / line.Quantity, 4);

            Result<TaxRate> taxRate = TaxRate.Create(
                product.TaxSlabId is { } slab ? rates.GetValueOrDefault(slab) : 0m);

            if (taxRate.IsFailure)
            {
                return Result.Failure<SalesInvoiceResponse>(taxRate.Error);
            }

            TaxAssessment assessment = TaxCalculator.Calculate(
                Money.Of(line.Quantity * rate, invoice.Currency),
                taxRate.Value,
                SalesTaxContext.For(firm, customer, mode));

            Result<SalesInvoiceLine> added = invoice.AddLine(
                product,
                units[line.UnitId],
                line.Quantity,
                line.StockQuantity,
                rate,
                assessment,
                line.BatchId is { } batch ? batches.GetValueOrDefault(batch) : null);

            if (added.IsFailure)
            {
                return Result.Failure<SalesInvoiceResponse>(added.Error);
            }
        }

        Result linked = invoice.BillDeliveryNote(note.Id);

        if (linked.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(linked.Error);
        }

        Result details = invoice.SetDetails(note.Number, note.Narration);

        if (details.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(details.Error);
        }

        _invoices.Add(invoice);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateSalesInvoiceCommandHandler.Describe(invoice));
    }
}
