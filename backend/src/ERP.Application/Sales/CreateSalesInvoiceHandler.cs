using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Sales;

/// <summary>Handles <see cref="CreateSalesInvoiceCommand"/>.</summary>
/// <remarks>
/// <para>
/// Everything is loaded and checked before anything is built, for the reason the stock
/// documents load the same way: a product from another firm found halfway through would
/// leave a reserved invoice number burnt on a document that never existed, and a gap in a
/// sales sequence is exactly what an auditor asks about.
/// </para>
/// <para>
/// Tax is assessed here rather than on the aggregate. The engine answers a question about
/// jurisdictions - which heads apply, at what rate, and whether a supply crossed a state
/// line - and the invoice records what it answered, so a reprint years later shows the tax
/// that was charged rather than the tax today's rates would produce.
/// </para>
/// </remarks>
public sealed class CreateSalesInvoiceCommandHandler
    : ICommandHandler<CreateSalesInvoiceCommand, SalesInvoiceResponse>
{
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
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreateSalesInvoiceCommandHandler"/> class.</summary>
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
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateSalesInvoiceCommandHandler(
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
        IUnitOfWork unitOfWork)
    {
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
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<SalesInvoiceResponse>> Handle(
        CreateSalesInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<SalesInvoiceResponse>(Error.Forbidden(
                "SalesInvoice.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before entering an invoice."));
        }

        Result<Context> loaded = await LoadAsync(request, firmId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(loaded.Error);
        }

        Context context = loaded.Value;

        Result<string> number = await ReserveAsync(
            request.Kind, firmId, branchId, context.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(number.Error);
        }

        TaxMode mode = request.Mode ?? DefaultModeFor(context.Firm);

        Result<SalesInvoice> draft = SalesInvoice.CreateDraft(
            _tenantContext.TenantId,
            firmId,
            branchId,
            context.Year,
            number.Value,
            request.Date,
            context.Customer,
            context.Warehouse,
            mode,
            context.Firm.BaseCurrency,
            request.Kind,
            request.ReturnsInvoiceId is { } returns ? SalesInvoiceId.From(returns) : null);

        if (draft.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(draft.Error);
        }

        SalesInvoice invoice = draft.Value;

        Result lined = AddLines(invoice, request, context, mode);

        if (lined.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(lined.Error);
        }

        Result charged = AddCharges(invoice, request, context);

        if (charged.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(charged.Error);
        }

        Result details = invoice.SetDetails(request.ReferenceNumber, request.Narration);

        if (details.IsFailure)
        {
            return Result.Failure<SalesInvoiceResponse>(details.Error);
        }

        _invoices.Add(invoice);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(invoice));
    }

    /// <summary>Describes an invoice as it now stands.</summary>
    /// <param name="invoice">The invoice.</param>
    /// <returns>Its figures.</returns>
    internal static SalesInvoiceResponse Describe(SalesInvoice invoice) =>
        new(
            invoice.Id.Value,
            invoice.Number,
            invoice.Status,
            invoice.Taxable.Amount,
            invoice.Tax.Amount,
            invoice.ChargeTotal.Amount,
            invoice.RoundingDifference.Amount,
            invoice.Total.Amount);

    /// <summary>The mode a firm's invoices open in, from its own regime.</summary>
    /// <remarks>
    /// The business's answer of 2026-08-10: nobody is offered a mode that does not apply
    /// where they trade, and a non-tax sale is the exception somebody selects
    /// deliberately. A firm with no regime at all can only sell without tax.
    /// </remarks>
    private static TaxMode DefaultModeFor(Firm firm) =>
        firm.TaxRegime == TaxRegime.None ? TaxMode.NonTax : TaxMode.Tax;

    /// <summary>Builds each line, converting its quantity and assessing its tax.</summary>
    private static Result AddLines(
        SalesInvoice invoice,
        CreateSalesInvoiceCommand request,
        Context context,
        TaxMode mode)
    {
        foreach (SalesInvoiceLineInput input in request.Lines)
        {
            Product product = context.Products[ProductId.From(input.ProductId)];
            UnitOfMeasure stockUnit = context.Units[product.StockUnitId];

            UnitOfMeasure entryUnit = input.UnitId is { } unitId
                ? context.Units[UnitOfMeasureId.From(unitId)]
                : stockUnit;

            // Refused rather than guessed at when the units measure different things:
            // four kilograms of something stocked in litres is not a quantity anything
            // can arrive at.
            Result<decimal> stockQuantity = UnitOfMeasure.Convert(
                input.Quantity, entryUnit, stockUnit);

            if (stockQuantity.IsFailure)
            {
                return Result.Failure(stockQuantity.Error);
            }

            Result<TaxRate> rate = TaxRate.Create(input.TaxPercentage);

            if (rate.IsFailure)
            {
                return Result.Failure(rate.Error);
            }

            Money taxable = Money.Of(
                (input.Quantity * input.Rate) - input.Discount, invoice.Currency);

            TaxAssessment assessment = TaxCalculator.Calculate(
                taxable, rate.Value, ContextFor(context, mode));

            Batch? batch = context.Batches.GetValueOrDefault(input.ProductId);

            IReadOnlyCollection<SerialNumber> units =
                context.Serials.GetValueOrDefault(input.ProductId) ?? [];

            Result<SalesInvoiceLine> added = invoice.AddLine(
                product,
                entryUnit,
                input.Quantity,
                stockQuantity.Value,
                input.Rate,
                assessment,
                batch,
                units,
                input.Discount);

            if (added.IsFailure)
            {
                return Result.Failure(added.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>The tax conditions this document is entered under.</summary>
    /// <remarks>
    /// Shared with the order, because it is one question - which heads apply to this
    /// customer - and two copies would eventually disagree. The place-of-supply comparison
    /// is what selects IGST over CGST plus SGST, and it is inert outside the GST regime.
    /// <para>
    /// Amounts are treated as tax-exclusive. The reverse-tax setting of section 9 is not
    /// built, and cannot be until the invoice's own line check tells inclusive entry from
    /// exclusive - recorded in the specification beside the rounding answer.
    /// </para>
    /// </remarks>
    private static TaxContext ContextFor(Context context, TaxMode mode) =>
        SalesTaxContext.For(context.Firm, context.Customer, mode);

    /// <summary>Adds the charges the caller entered.</summary>
    /// <remarks>
    /// Only what was entered. Section 9's <c>Default</c> flag makes a charge appear on the
    /// screen with the document; it does not put one on an invoice nobody typed an amount
    /// into, and the only seeded default - Round Off - is computed by the invoice itself
    /// rather than entered at all.
    /// </remarks>
    private static Result AddCharges(
        SalesInvoice invoice,
        CreateSalesInvoiceCommand request,
        Context context)
    {
        foreach (SalesInvoiceChargeInput input in request.Charges ?? [])
        {
            if (!context.Charges.TryGetValue(LedgerId.From(input.LedgerId), out AdditionalLedger? mapping))
            {
                return Result.Failure(Error.NotFound(
                    "SalesInvoice.ChargeNotMapped",
                    "That charge is not one this firm carries on a sales document."));
            }

            Result<SalesInvoiceCharge> added = invoice.AddCharge(mapping, input.Amount);

            if (added.IsFailure)
            {
                return Result.Failure(added.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>Loads and checks everything the lines name, before any of it is used.</summary>
    private async Task<Result<Context>> LoadAsync(
        CreateSalesInvoiceCommand request,
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
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<Context>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}. Create one before "
                + "invoicing on that date."));
        }

        Ledger? customer = await _ledgers.FindAsync(
            LedgerId.From(request.CustomerLedgerId), cancellationToken);

        if (customer is null || customer.FirmId != firmId)
        {
            return Result.Failure<Context>(Error.NotFound(
                "SalesInvoice.CustomerNotFound",
                "That customer account is not in the selected firm."));
        }

        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.WarehouseId), cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId)
        {
            return Result.Failure<Context>(Error.NotFound(
                "SalesInvoice.WarehouseNotFound",
                "That warehouse is not in the selected firm."));
        }

        List<ProductId> productIds =
            [.. request.Lines.Select(line => ProductId.From(line.ProductId)).Distinct()];

        IReadOnlyDictionary<ProductId, Product> products =
            await _products.GetManyAsync(productIds, cancellationToken);

        foreach (ProductId id in productIds)
        {
            if (!products.TryGetValue(id, out Product? product) || product.FirmId != firmId)
            {
                return Result.Failure<Context>(Error.NotFound(
                    "SalesInvoice.ProductNotFound", $"Product {id} is not in the selected firm."));
            }

            if (!product.IsActive)
            {
                return Result.Failure<Context>(Error.BusinessRule(
                    "SalesInvoice.ProductWithdrawn",
                    $"'{product.Code}' has been withdrawn from use."));
            }
        }

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units =
            await _masters.GetUnitsAsync(
                [
                    .. request.Lines
                        .Where(line => line.UnitId is not null)
                        .Select(line => UnitOfMeasureId.From(line.UnitId!.Value))
                        .Concat(products.Values.Select(product => product.StockUnitId))
                        .Distinct(),
                ],
                cancellationToken);

        foreach (SalesInvoiceLineInput line in request.Lines)
        {
            if (line.UnitId is { } unitId
                && (!units.TryGetValue(UnitOfMeasureId.From(unitId), out UnitOfMeasure? unit)
                    || unit.FirmId != firmId))
            {
                return Result.Failure<Context>(Error.NotFound(
                    "SalesInvoice.UnitNotFound", $"Unit {unitId} is not in the selected firm."));
            }
        }

        Result<Dictionary<Guid, Batch>> batches = await ResolveBatchesAsync(
            request, firmId, cancellationToken);

        if (batches.IsFailure)
        {
            return Result.Failure<Context>(batches.Error);
        }

        Result<Dictionary<Guid, IReadOnlyCollection<SerialNumber>>> serials =
            await ResolveSerialsAsync(request, firmId, cancellationToken);

        if (serials.IsFailure)
        {
            return Result.Failure<Context>(serials.Error);
        }

        IReadOnlyList<AdditionalLedger> mapped = await _charges.ListForDocumentAsync(
            firmId, ChargeableDocument.Sales, cancellationToken);

        return Result.Success(new Context(
            firm,
            year,
            customer,
            warehouse,
            products,
            units,
            batches.Value,
            serials.Value,
            mapped.ToDictionary(charge => charge.LedgerId)));
    }

    /// <summary>Finds the batch each line names, by identifier or by number.</summary>
    /// <remarks>
    /// No batch is opened here, unlike a receipt. Goods are sold out of a batch that
    /// already exists, and a sales line naming one that does not is a typo rather than a
    /// new batch of stock nobody bought.
    /// </remarks>
    private async Task<Result<Dictionary<Guid, Batch>>> ResolveBatchesAsync(
        CreateSalesInvoiceCommand request,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, Batch> resolved = [];

        List<BatchId> ids =
        [
            .. request.Lines.Where(line => line.BatchId is not null)
                .Select(line => BatchId.From(line.BatchId!.Value)).Distinct(),
        ];

        IReadOnlyDictionary<BatchId, Batch> byId = ids.Count == 0
            ? new Dictionary<BatchId, Batch>()
            : await _batches.GetManyAsync(ids, cancellationToken);

        List<(ProductId Product, string Number)> numbers =
        [
            .. request.Lines
                .Where(line => line.BatchId is null && !string.IsNullOrWhiteSpace(line.BatchNumber))
                .Select(line => (ProductId.From(line.ProductId), line.BatchNumber!.Trim()))
                .Distinct(),
        ];

        IReadOnlyDictionary<(ProductId Product, string Number), Batch> byNumber =
            numbers.Count == 0
                ? new Dictionary<(ProductId, string), Batch>()
                : await _batches.GetByNumbersAsync(firmId, numbers, cancellationToken);

        foreach (SalesInvoiceLineInput line in request.Lines)
        {
            Batch? batch = null;

            if (line.BatchId is { } id)
            {
                batch = byId.GetValueOrDefault(BatchId.From(id));
            }
            else if (!string.IsNullOrWhiteSpace(line.BatchNumber))
            {
                batch = byNumber.GetValueOrDefault(
                    (ProductId.From(line.ProductId), line.BatchNumber.Trim()));
            }

            if (batch is null && (line.BatchId is not null
                || !string.IsNullOrWhiteSpace(line.BatchNumber)))
            {
                return Result.Failure<Dictionary<Guid, Batch>>(Error.NotFound(
                    "SalesInvoice.BatchNotFound",
                    $"No batch '{line.BatchNumber ?? line.BatchId.ToString()}' exists for "
                    + "that product."));
            }

            if (batch is not null)
            {
                resolved[line.ProductId] = batch;
            }
        }

        return Result.Success(resolved);
    }

    /// <summary>Finds the units each line names, by their numbers within the product.</summary>
    private async Task<Result<Dictionary<Guid, IReadOnlyCollection<SerialNumber>>>>
        ResolveSerialsAsync(
            CreateSalesInvoiceCommand request,
            FirmId firmId,
            CancellationToken cancellationToken)
    {
        Dictionary<Guid, IReadOnlyCollection<SerialNumber>> resolved = [];

        List<(ProductId Product, string Number)> wanted =
        [
            .. request.Lines
                .SelectMany(line => (line.SerialNumbers ?? [])
                    .Select(number => (ProductId.From(line.ProductId), number.Trim())))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Item2))
                .Distinct(),
        ];

        if (wanted.Count == 0)
        {
            return Result.Success(resolved);
        }

        IReadOnlyDictionary<(ProductId Product, string Number), SerialNumber> found =
            await _serials.GetByNumbersAsync(firmId, wanted, cancellationToken);

        foreach (SalesInvoiceLineInput line in request.Lines)
        {
            List<SerialNumber> units = [];

            foreach (string number in line.SerialNumbers ?? [])
            {
                if (!found.TryGetValue(
                    (ProductId.From(line.ProductId), number.Trim()), out SerialNumber? serial))
                {
                    return Result.Failure<Dictionary<Guid, IReadOnlyCollection<SerialNumber>>>(
                        Error.NotFound(
                            "SalesInvoice.SerialNotFound",
                            $"No unit numbered '{number}' exists for that product."));
                }

                units.Add(serial);
            }

            if (units.Count > 0)
            {
                resolved[line.ProductId] = units;
            }
        }

        return Result.Success(resolved);
    }

    /// <summary>Takes the next number, creating the series if there is none.</summary>
    /// <remarks>
    /// A separate series per kind, which is what §11 describes and what a firm expects: a
    /// credit note is not a gap in the invoice sequence, and an auditor asking why
    /// SL/2026/0004 does not exist deserves a better answer than "it was a return".
    /// </remarks>
    private async Task<Result<string>> ReserveAsync(
        SalesDocumentKind kind,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        bool isReturn = kind == SalesDocumentKind.Return;
        string documentType = isReturn ? DocumentTypes.SalesReturn : DocumentTypes.SalesInvoice;

        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            documentType, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                _tenantContext.TenantId, firmId, documentType, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat(
                prefix: isReturn ? "SR" : "SL",
                suffix: null,
                separator: "/",
                financialYearLabel: year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }

    /// <summary>Everything the lines name, loaded and checked in one pass.</summary>
    private sealed record Context(
        Firm Firm,
        FinancialYear Year,
        Ledger Customer,
        Warehouse Warehouse,
        IReadOnlyDictionary<ProductId, Product> Products,
        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> Units,
        IReadOnlyDictionary<Guid, Batch> Batches,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<SerialNumber>> Serials,
        IReadOnlyDictionary<LedgerId, AdditionalLedger> Charges);
}
