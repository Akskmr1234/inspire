using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Purchase;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Purchase;

/// <summary>Handles <see cref="ConvertPurchaseOrderCommand"/>.</summary>
/// <remarks>
/// <para>
/// The purchase side of §12.2's <em>Create Invoice From</em>, and the reason the order
/// carries an invoiced quantity per line. What comes out is a <b>draft</b> purchase: posting
/// receives the goods, raises the bill and writes the books, and it stays a separate step so
/// a conversion can be checked against the supplier's own document before any of that
/// happens.
/// </para>
/// <para>
/// <b>The batch is typed rather than chosen</b>, which is where this stops being a mirror of
/// the sales conversion. A sale picks from the batches a warehouse holds; a purchase is
/// usually the moment a batch comes into existence, so the number is read off the carton and
/// the expiry beside it, and the receipt opens the batch when the purchase posts.
/// </para>
/// <para>
/// The tax is reassessed for the quantity actually arriving rather than copied from the
/// order. A half-delivered line owes half the tax, and the engine is asked again because the
/// supplier's state may have been corrected since the order went out - the purchase has to
/// reclaim what is true today, not what was expected in March.
/// </para>
/// <para>
/// The rate and the discount are the order's, because those <em>were</em> agreed. The
/// discount is apportioned across deliveries for the reason a sale's is: giving the whole of
/// it on the first would leave the second purchase mysteriously dearer.
/// </para>
/// </remarks>
public sealed class ConvertPurchaseOrderCommandHandler
    : ICommandHandler<ConvertPurchaseOrderCommand, PurchaseInvoiceResponse>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IPurchaseInvoiceRepository _invoices;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IAdditionalLedgerRepository _charges;
    private readonly ILedgerRepository _ledgers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ConvertPurchaseOrderCommandHandler"/> class.</summary>
    /// <param name="orders">The purchase order repository.</param>
    /// <param name="invoices">The purchase invoice repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="charges">The additional-charge repository.</param>
    /// <param name="ledgers">The nominal ledger repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ConvertPurchaseOrderCommandHandler(
        IPurchaseOrderRepository orders,
        IPurchaseInvoiceRepository invoices,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IAdditionalLedgerRepository charges,
        ILedgerRepository ledgers,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears,
        IFirmRepository firms,
        ITenantContext tenantContext,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _invoices = invoices;
        _masters = masters;
        _products = products;
        _charges = charges;
        _ledgers = ledgers;
        _numbering = numbering;
        _financialYears = financialYears;
        _firms = firms;
        _tenantContext = tenantContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<PurchaseInvoiceResponse>> Handle(
        ConvertPurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<PurchaseInvoiceResponse>(Error.Forbidden(
                "PurchaseOrder.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before converting an order."));
        }

        Result<PurchaseOrder> found = await PurchaseOrderLookup.ResolveAsync(
            _orders, _tenantContext, request.PurchaseOrderId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(found.Error);
        }

        PurchaseOrder order = found.Value;

        if (!order.IsOpen)
        {
            return Result.Failure<PurchaseInvoiceResponse>(Error.BusinessRule(
                "PurchaseOrder.NotOpen",
                $"Order '{order.Number}' is {order.Status}, so no purchase can be raised "
                + "from it."));
        }

        Result<IReadOnlyList<Conversion>> wanted = Resolve(order, request.Lines);

        if (wanted.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(wanted.Error);
        }

        DateOnly date = request.Date ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        Result<Context> loaded = await LoadAsync(order, request, firmId, date, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(loaded.Error);
        }

        // The commonest mistake in a purchase ledger, asked before the document is built
        // rather than left to an index. Only where the supplier's number is stated here -
        // a conversion may leave it for later, and a purchase with no number cannot clash.
        if (request.SupplierInvoiceNumber is { Length: > 0 } supplierNumber)
        {
            bool alreadyEntered = await _invoices.IsSupplierInvoiceNumberInUseAsync(
                firmId, order.SupplierLedgerId, supplierNumber.Trim(), cancellationToken);

            if (alreadyEntered)
            {
                return Result.Failure<PurchaseInvoiceResponse>(Error.Conflict(
                    "PurchaseInvoice.SupplierInvoiceAlreadyEntered",
                    $"Invoice '{supplierNumber}' from that supplier is already on file."));
            }
        }

        Result<string> number = await ReserveAsync(
            firmId, branchId, loaded.Value.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(number.Error);
        }

        Result<PurchaseInvoice> draft = PurchaseInvoice.CreateDraft(
            order.TenantId,
            firmId,
            branchId,
            loaded.Value.Year,
            number.Value,
            date,
            loaded.Value.Supplier,
            loaded.Value.Warehouse,
            order.Mode,
            order.Currency,
            PurchaseDocumentKind.Invoice,
            returnsInvoiceId: null,
            purchaseOrderId: order.Id);

        if (draft.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(draft.Error);
        }

        PurchaseInvoice invoice = draft.Value;

        Result lined = AddLines(invoice, order, wanted.Value, loaded.Value);

        if (lined.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(lined.Error);
        }

        // Whatever the order agreed beside the goods rides on the first purchase out of it.
        // Splitting carriage across part-deliveries would be inventing an apportionment
        // nobody agreed, and charging it on every one would pay it twice.
        Result charged = order.IsPartlyInvoiced
            ? Result.Success()
            : CarryCharges(invoice, order, loaded.Value.Charges);

        if (charged.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(charged.Error);
        }

        Result details = invoice.SetSupplierDocument(
            request.SupplierInvoiceNumber, request.SupplierInvoiceDate, order.Narration);

        if (details.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(details.Error);
        }

        Result recorded = order.RecordInvoiced(
            wanted.Value.ToDictionary(line => line.Line.Id, line => line.Quantity));

        if (recorded.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(recorded.Error);
        }

        _invoices.Add(invoice);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CreatePurchaseInvoiceCommandHandler.Describe(invoice));
    }

    /// <summary>Works out how much of each line is arriving.</summary>
    /// <remarks>
    /// Naming no lines means everything still outstanding. Less often the whole order than
    /// on the sales side - suppliers part-ship as a matter of routine - but still the path
    /// worth having, because making a caller list what it already knows is a way to get it
    /// wrong.
    /// </remarks>
    private static Result<IReadOnlyList<Conversion>> Resolve(
        PurchaseOrder order,
        IReadOnlyList<PurchaseOrderConversionLine>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            List<Conversion> everything =
            [
                .. order.Lines
                    .Where(line => !line.IsFulfilled)
                    .Select(line => new Conversion(
                        line, line.OutstandingQuantity, null, null, [])),
            ];

            return everything.Count == 0
                ? Result.Failure<IReadOnlyList<Conversion>>(Error.BusinessRule(
                    "PurchaseOrder.NothingOutstanding",
                    $"Order '{order.Number}' has nothing left to invoice."))
                : Result.Success<IReadOnlyList<Conversion>>(everything);
        }

        List<Conversion> chosen = [];

        foreach (PurchaseOrderConversionLine input in requested)
        {
            PurchaseOrderLine? line = order.Lines.FirstOrDefault(
                candidate => candidate.Id.Value == input.PurchaseOrderLineId);

            if (line is null)
            {
                return Result.Failure<IReadOnlyList<Conversion>>(Error.NotFound(
                    "PurchaseOrder.LineNotFound", $"Order '{order.Number}' has no such line."));
            }

            decimal quantity = input.Quantity ?? line.OutstandingQuantity;

            if (quantity <= 0m)
            {
                return Result.Failure<IReadOnlyList<Conversion>>(Error.Validation(
                    "PurchaseOrder.InvoicedQuantityNotPositive",
                    $"Line {line.LineNumber} was asked for {quantity}."));
            }

            chosen.Add(new Conversion(
                line, quantity, input.BatchNumber, input.ExpiresOn, input.SerialNumbers ?? []));
        }

        return Result.Success<IReadOnlyList<Conversion>>(chosen);
    }

    /// <summary>Builds the purchase lines from the order's, at the quantities arriving.</summary>
    private static Result AddLines(
        PurchaseInvoice invoice,
        PurchaseOrder order,
        IReadOnlyList<Conversion> wanted,
        Context context)
    {
        foreach (Conversion conversion in wanted)
        {
            PurchaseOrderLine line = conversion.Line;
            Product product = context.Products[line.ProductId];
            UnitOfMeasure entryUnit = context.Units[line.UnitId];
            UnitOfMeasure stockUnit = context.Units[product.StockUnitId];

            Result<decimal> stockQuantity = UnitOfMeasure.Convert(
                conversion.Quantity, entryUnit, stockUnit);

            if (stockQuantity.IsFailure)
            {
                return Result.Failure(stockQuantity.Error);
            }

            // Apportioned rather than carried whole, so two deliveries of one line come to
            // what the line was ordered at between them.
            decimal discount = line.Quantity == 0m
                ? 0m
                : decimal.Round(
                    line.Discount * (conversion.Quantity / line.Quantity),
                    4,
                    MidpointRounding.AwayFromZero);

            Money taxable = Money.Of(
                (conversion.Quantity * line.Rate) - discount, invoice.Currency);

            Result<TaxRate> rate = TaxRate.Create(
                line.Components.Sum(component => component.Percentage));

            if (rate.IsFailure)
            {
                return Result.Failure(rate.Error);
            }

            TaxAssessment assessment = TaxCalculator.Calculate(
                taxable,
                rate.Value,
                PurchaseTaxContext.For(context.Firm, context.Supplier, order.Mode));

            Result<PurchaseInvoiceLine> added = invoice.AddLine(
                product,
                entryUnit,
                conversion.Quantity,
                stockQuantity.Value,
                line.Rate,
                assessment,
                conversion.BatchNumber,
                conversion.ExpiresOn,
                conversion.SerialNumbers,
                discount);

            if (added.IsFailure)
            {
                return Result.Failure(added.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>Carries the order's charges onto the purchase, where the matrix allows it.</summary>
    /// <remarks>
    /// An order's charges are mapped to <c>PurchaseOrder</c> and a purchase's to
    /// <c>Purchase</c>, so the same ledger has to be mapped for both before an agreed charge
    /// can be booked. A charge the firm records on orders but has never mapped to purchases
    /// is skipped rather than refused: the goods should still be received, and the charge is
    /// somebody's to add.
    /// </remarks>
    private static Result CarryCharges(
        PurchaseInvoice invoice,
        PurchaseOrder order,
        IReadOnlyDictionary<LedgerId, AdditionalLedger> billable)
    {
        foreach (PurchaseOrderCharge agreed in order.Charges)
        {
            if (!billable.TryGetValue(agreed.LedgerId, out AdditionalLedger? mapping))
            {
                continue;
            }

            Result<PurchaseInvoiceCharge> added = invoice.AddCharge(
                mapping, agreed.Amount.Amount);

            if (added.IsFailure)
            {
                return Result.Failure(added.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>Loads everything the purchase will name, before any of it is built.</summary>
    private async Task<Result<Context>> LoadAsync(
        PurchaseOrder order,
        ConvertPurchaseOrderCommand request,
        FirmId firmId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            firmId, date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<Context>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {date:yyyy-MM-dd}."));
        }

        Ledger? supplier = await _ledgers.FindAsync(order.SupplierLedgerId, cancellationToken);

        if (supplier is null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "PurchaseOrder.SupplierNotFound", "The supplier account no longer exists."));
        }

        WarehouseId warehouseId = request.WarehouseId is { } chosen
            ? WarehouseId.From(chosen)
            : order.WarehouseId;

        Warehouse? warehouse = await _masters.FindWarehouseAsync(warehouseId, cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId)
        {
            return Result.Failure<Context>(Error.NotFound(
                "PurchaseOrder.WarehouseNotFound",
                "That warehouse is not in the selected firm."));
        }

        IReadOnlyDictionary<ProductId, Product> products = await _products.GetManyAsync(
            [.. order.Lines.Select(line => line.ProductId).Distinct()], cancellationToken);

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units =
            await _masters.GetUnitsAsync(
                [
                    .. order.Lines.Select(line => line.UnitId)
                        .Concat(products.Values.Select(product => product.StockUnitId))
                        .Distinct(),
                ],
                cancellationToken);

        // A product withdrawn between the order and the delivery. Named by line, because
        // "a product no longer exists" on a forty-line order is not something anybody can
        // act on.
        PurchaseOrderLine? orphaned = order.Lines.FirstOrDefault(line =>
            !products.ContainsKey(line.ProductId) || !units.ContainsKey(line.UnitId));

        if (orphaned is not null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "PurchaseOrder.LineMasterMissing",
                $"A product or unit on line {orphaned.LineNumber} no longer exists."));
        }

        // The charges a purchase may carry, which is a different mapping from the ones an
        // order may record - so an agreed charge is only bookable where the firm has mapped
        // the same ledger to both.
        IReadOnlyList<AdditionalLedger> billable = await _charges.ListForDocumentAsync(
            firmId, ChargeableDocument.Purchase, cancellationToken);

        return Result.Success(new Context(
            firm, year, supplier, warehouse, products, units,
            billable.ToDictionary(charge => charge.LedgerId)));
    }

    /// <summary>Takes the next purchase number, creating the series if there is none.</summary>
    private async Task<Result<string>> ReserveAsync(
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            DocumentTypes.PurchaseInvoice, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                _tenantContext.TenantId, firmId, DocumentTypes.PurchaseInvoice, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat("PU", null, "/", year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }

    /// <summary>One order line, and how much of it is arriving.</summary>
    private sealed record Conversion(
        PurchaseOrderLine Line,
        decimal Quantity,
        string? BatchNumber,
        DateOnly? ExpiresOn,
        IReadOnlyList<string> SerialNumbers);

    /// <summary>Everything the purchase names, loaded once.</summary>
    private sealed record Context(
        Firm Firm,
        FinancialYear Year,
        Ledger Supplier,
        Warehouse Warehouse,
        IReadOnlyDictionary<ProductId, Product> Products,
        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> Units,
        IReadOnlyDictionary<LedgerId, AdditionalLedger> Charges);
}
