using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
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

namespace ERP.Application.Sales;

/// <summary>Handles <see cref="ConvertSalesOrderCommand"/>.</summary>
/// <remarks>
/// <para>
/// §12.2's <em>Create Invoice From</em>, and the reason the order carries an invoiced
/// quantity per line. What comes out is a <b>draft</b> invoice: posting moves stock,
/// raises a debt and writes the books, and it stays a separate step so a conversion can be
/// looked at before any of that happens.
/// </para>
/// <para>
/// The tax is reassessed for the quantity actually going out rather than copied from the
/// order. A half-shipped line owes half the tax, and the engine is asked again because the
/// customer's state may have been corrected since the order was taken - the invoice has to
/// charge what is true today, not what was quoted in March.
/// </para>
/// <para>
/// The rate and the discount are the order's, because those <em>were</em> agreed. The
/// discount is apportioned: a line quoted at ten off for twenty units gives five off when
/// ten of them ship, and giving the whole ten on the first delivery would leave the second
/// invoice mysteriously dearer.
/// </para>
/// </remarks>
public sealed class ConvertSalesOrderCommandHandler
    : ICommandHandler<ConvertSalesOrderCommand, SalesInvoiceResponse>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesInvoiceRepository _invoices;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ISerialNumberRepository _serials;
    private readonly IAdditionalLedgerRepository _charges;
    private readonly ILedgerRepository _ledgers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ConvertSalesOrderCommandHandler"/> class.</summary>
    /// <param name="orders">The sales order repository.</param>
    /// <param name="invoices">The sales invoice repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="serials">The serial-number repository.</param>
    /// <param name="charges">The additional-charge repository.</param>
    /// <param name="ledgers">The nominal ledger repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ConvertSalesOrderCommandHandler(
        ISalesOrderRepository orders,
        ISalesInvoiceRepository invoices,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ISerialNumberRepository serials,
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
        _batches = batches;
        _serials = serials;
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
    public async Task<Result<SalesInvoiceResponse>> Handle(
        ConvertSalesOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.Forbidden(
                "SalesOrder.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before converting an order."));
        }

        Result<SalesOrder> found = await SalesOrderLookup.ResolveAsync(
            _orders, _tenantContext, request.SalesOrderId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(found.Error);
        }

        SalesOrder order = found.Value;

        if (!order.IsOpen)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.BusinessRule(
                "SalesOrder.NotOpen",
                $"Order '{order.Number}' is {order.Status}, so no invoice can be raised "
                + "from it."));
        }

        Result<IReadOnlyList<Conversion>> wanted = Resolve(order, request.Lines);

        if (wanted.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(wanted.Error);
        }

        DateOnly date = request.Date ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        Result<Context> loaded = await LoadAsync(order, request, firmId, date, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(loaded.Error);
        }

        Result<string> number = await ReserveAsync(
            firmId, branchId, loaded.Value.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(number.Error);
        }

        Result<SalesInvoice> draft = SalesInvoice.CreateDraft(
            order.TenantId,
            firmId,
            branchId,
            loaded.Value.Year,
            number.Value,
            date,
            loaded.Value.Customer,
            loaded.Value.Warehouse,
            order.Mode,
            order.Currency,
            SalesDocumentKind.Invoice,
            returnsInvoiceId: null,
            salesOrderId: order.Id);

        if (draft.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(draft.Error);
        }

        SalesInvoice invoice = draft.Value;

        Result lined = AddLines(invoice, order, wanted.Value, loaded.Value);

        if (lined.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(lined.Error);
        }

        // Whatever the order quoted beside the goods rides on the first invoice out of it.
        // Splitting freight across part-deliveries would be inventing an apportionment
        // nobody agreed, and charging it on every one would bill it twice.
        Result charged = order.IsPartlyInvoiced
            ? Result.Success()
            : CarryCharges(invoice, order, loaded.Value.Charges);

        if (charged.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(charged.Error);
        }

        Result details = invoice.SetDetails(order.ReferenceNumber, order.Narration);

        if (details.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(details.Error);
        }

        Result recorded = order.RecordInvoiced(
            wanted.Value.ToDictionary(line => line.Line.Id, line => line.Quantity));

        if (recorded.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(recorded.Error);
        }

        _invoices.Add(invoice);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateSalesInvoiceCommandHandler.Describe(invoice));
    }

    /// <summary>Works out how much of each line is going out.</summary>
    /// <remarks>
    /// Naming no lines means everything still outstanding, which is the common path: most
    /// orders are filled in one go, and making a caller list what it already knows would
    /// be a way to get it wrong.
    /// </remarks>
    private static Result<IReadOnlyList<Conversion>> Resolve(
        SalesOrder order,
        IReadOnlyList<SalesOrderConversionLine>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            List<Conversion> everything =
            [
                .. order.Lines
                    .Where(line => !line.IsFulfilled)
                    .Select(line => new Conversion(line, line.OutstandingQuantity, null, [])),
            ];

            return everything.Count == 0
                ? Result.Failure<IReadOnlyList<Conversion>>(Error.BusinessRule(
                    "SalesOrder.NothingOutstanding",
                    $"Order '{order.Number}' has nothing left to invoice."))
                : Result.Success<IReadOnlyList<Conversion>>(everything);
        }

        List<Conversion> chosen = [];

        foreach (SalesOrderConversionLine input in requested)
        {
            SalesOrderLine? line = order.Lines.FirstOrDefault(
                candidate => candidate.Id.Value == input.SalesOrderLineId);

            if (line is null)
            {
                return Result.Failure<IReadOnlyList<Conversion>>(Error.NotFound(
                    "SalesOrder.LineNotFound",
                    $"Order '{order.Number}' has no such line."));
            }

            decimal quantity = input.Quantity ?? line.OutstandingQuantity;

            if (quantity <= 0m)
            {
                return Result.Failure<IReadOnlyList<Conversion>>(Error.Validation(
                    "SalesOrder.InvoicedQuantityNotPositive",
                    $"Line {line.LineNumber} was asked for {quantity}."));
            }

            chosen.Add(new Conversion(
                line, quantity, input.BatchNumber, input.SerialNumbers ?? []));
        }

        return Result.Success<IReadOnlyList<Conversion>>(chosen);
    }

    /// <summary>Builds the invoice lines from the order's, at the quantities going out.</summary>
    private static Result AddLines(
        SalesInvoice invoice,
        SalesOrder order,
        IReadOnlyList<Conversion> wanted,
        Context context)
    {
        foreach (Conversion conversion in wanted)
        {
            SalesOrderLine line = conversion.Line;
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
            // what the line was quoted at between them.
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
                taxable, rate.Value, SalesTaxContext.For(context.Firm, context.Customer, order.Mode));

            Batch? batch = conversion.BatchNumber is { Length: > 0 } number
                ? context.Batches.GetValueOrDefault((product.Id, number.Trim()))
                : null;

            if (conversion.BatchNumber is { Length: > 0 } named && batch is null)
            {
                return Result.Failure(Error.NotFound(
                    "SalesOrder.BatchNotFound",
                    $"No batch '{named}' exists for '{product.Code}'."));
            }

            List<SerialNumber> units = [];

            foreach (string serial in conversion.SerialNumbers)
            {
                if (!context.Serials.TryGetValue((product.Id, serial.Trim()), out SerialNumber? unit))
                {
                    return Result.Failure(Error.NotFound(
                        "SalesOrder.SerialNotFound",
                        $"No unit numbered '{serial}' exists for '{product.Code}'."));
                }

                units.Add(unit);
            }

            Result<SalesInvoiceLine> added = invoice.AddLine(
                product,
                entryUnit,
                conversion.Quantity,
                stockQuantity.Value,
                line.Rate,
                assessment,
                batch,
                units,
                discount);

            if (added.IsFailure)
            {
                return Result.Failure(added.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>Carries the order's charges onto the invoice, where the matrix allows it.</summary>
    /// <remarks>
    /// An order's charges are mapped to <c>SalesOrder</c> and an invoice's to <c>Sales</c>,
    /// so the same ledger has to be mapped for both before a quoted charge can be billed. A
    /// charge the firm quotes on orders but has never mapped to invoices is skipped rather
    /// than refused: the goods should still go out, and the charge is somebody's to add.
    /// </remarks>
    private static Result CarryCharges(
        SalesInvoice invoice,
        SalesOrder order,
        IReadOnlyDictionary<LedgerId, AdditionalLedger> billable)
    {
        foreach (SalesOrderCharge quoted in order.Charges)
        {
            if (!billable.TryGetValue(quoted.LedgerId, out AdditionalLedger? mapping))
            {
                continue;
            }

            Result<SalesInvoiceCharge> added = invoice.AddCharge(mapping, quoted.Amount.Amount);

            if (added.IsFailure)
            {
                return Result.Failure(added.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>Loads everything the invoice will name, before any of it is built.</summary>
    private async Task<Result<Context>> LoadAsync(
        SalesOrder order,
        ConvertSalesOrderCommand request,
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

        Ledger? customer = await _ledgers.FindAsync(order.CustomerLedgerId, cancellationToken);

        if (customer is null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "SalesOrder.CustomerNotFound", "The customer account no longer exists."));
        }

        WarehouseId warehouseId = request.WarehouseId is { } chosen
            ? WarehouseId.From(chosen)
            : order.WarehouseId;

        Warehouse? warehouse = await _masters.FindWarehouseAsync(warehouseId, cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId)
        {
            return Result.Failure<Context>(Error.NotFound(
                "SalesOrder.WarehouseNotFound",
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
        SalesOrderLine? orphaned = order.Lines.FirstOrDefault(line =>
            !products.ContainsKey(line.ProductId) || !units.ContainsKey(line.UnitId));

        if (orphaned is not null)
        {
            return Result.Failure<Context>(Error.NotFound(
                "SalesOrder.LineMasterMissing",
                $"A product or unit on line {orphaned.LineNumber} no longer exists."));
        }

        Result<Dictionary<(ProductId Product, string Number), Batch>> batches = await LoadBatchesAsync(
            request, firmId, order, cancellationToken);

        if (batches.IsFailure)
        {
            return Result.Failure<Context>(batches.Error);
        }

        Result<Dictionary<(ProductId Product, string Number), SerialNumber>> serials = await LoadSerialsAsync(
            request, firmId, order, cancellationToken);

        if (serials.IsFailure)
        {
            return Result.Failure<Context>(serials.Error);
        }

        // The charges an invoice may carry, which is a different mapping from the ones an
        // order may quote - so a quoted charge is only billable where the firm has mapped
        // the same ledger to both.
        IReadOnlyList<AdditionalLedger> billable = await _charges.ListForDocumentAsync(
            firmId, ChargeableDocument.Sales, cancellationToken);

        return Result.Success(new Context(
            firm, year, customer, warehouse, products, units,
            batches.Value, serials.Value, billable.ToDictionary(charge => charge.LedgerId)));
    }

    private async Task<Result<Dictionary<(ProductId Product, string Number), Batch>>> LoadBatchesAsync(
        ConvertSalesOrderCommand request,
        FirmId firmId,
        SalesOrder order,
        CancellationToken cancellationToken)
    {
        List<(ProductId Product, string Number)> wanted =
        [
            .. (request.Lines ?? [])
                .Where(line => !string.IsNullOrWhiteSpace(line.BatchNumber))
                .Select(line => (
                    order.Lines.First(candidate => candidate.Id.Value == line.SalesOrderLineId)
                        .ProductId,
                    line.BatchNumber!.Trim()))
                .Distinct(),
        ];

        if (wanted.Count == 0)
        {
            return Result.Success(new Dictionary<(ProductId Product, string Number), Batch>());
        }

        IReadOnlyDictionary<(ProductId Product, string Number), Batch> found =
            await _batches.GetByNumbersAsync(firmId, wanted, cancellationToken);

        return Result.Success(found.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    private async Task<Result<Dictionary<(ProductId Product, string Number), SerialNumber>>> LoadSerialsAsync(
        ConvertSalesOrderCommand request,
        FirmId firmId,
        SalesOrder order,
        CancellationToken cancellationToken)
    {
        List<(ProductId Product, string Number)> wanted =
        [
            .. (request.Lines ?? [])
                .SelectMany(line => (line.SerialNumbers ?? [])
                    .Select(number => (
                        order.Lines.First(candidate => candidate.Id.Value == line.SalesOrderLineId)
                            .ProductId,
                        number.Trim())))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Item2))
                .Distinct(),
        ];

        if (wanted.Count == 0)
        {
            return Result.Success(new Dictionary<(ProductId Product, string Number), SerialNumber>());
        }

        IReadOnlyDictionary<(ProductId Product, string Number), SerialNumber> found =
            await _serials.GetByNumbersAsync(firmId, wanted, cancellationToken);

        return Result.Success(found.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    /// <summary>Takes the next invoice number, creating the series if there is none.</summary>
    private async Task<Result<string>> ReserveAsync(
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            DocumentTypes.SalesInvoice, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                _tenantContext.TenantId, firmId, DocumentTypes.SalesInvoice, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat("SL", null, "/", year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }

    /// <summary>One order line, and how much of it is going out.</summary>
    private sealed record Conversion(
        SalesOrderLine Line,
        decimal Quantity,
        string? BatchNumber,
        IReadOnlyList<string> SerialNumbers);

    /// <summary>Everything the invoice names, loaded once.</summary>
    private sealed record Context(
        Firm Firm,
        FinancialYear Year,
        Ledger Customer,
        Warehouse Warehouse,
        IReadOnlyDictionary<ProductId, Product> Products,
        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> Units,
        IReadOnlyDictionary<(ProductId Product, string Number), Batch> Batches,
        IReadOnlyDictionary<(ProductId Product, string Number), SerialNumber> Serials,
        IReadOnlyDictionary<LedgerId, AdditionalLedger> Charges);
}
