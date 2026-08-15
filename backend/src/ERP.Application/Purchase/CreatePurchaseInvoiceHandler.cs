using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Purchase;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Purchase;

/// <summary>Handles <see cref="CreatePurchaseInvoiceCommand"/>.</summary>
/// <remarks>
/// <para>
/// Everything is loaded and checked before anything is built, for the reason the sales
/// side loads the same way: a product from another firm found halfway through would leave
/// a reserved number burnt on a document that never existed, and a gap in a sequence is
/// exactly what an auditor asks about.
/// </para>
/// <para>
/// Shorter than its sales counterpart by the whole of the batch and serial resolution,
/// which is the difference the two documents were split over: a sale has to find goods
/// that exist, and a purchase only has to write down what the supplier's document says.
/// The numbers become real when the receipt posts, and until then they are text.
/// </para>
/// </remarks>
public sealed class CreatePurchaseInvoiceCommandHandler
    : ICommandHandler<CreatePurchaseInvoiceCommand, PurchaseInvoiceResponse>
{
    private readonly IPurchaseInvoiceRepository _invoices;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IAdditionalLedgerRepository _charges;
    private readonly ILedgerRepository _ledgers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreatePurchaseInvoiceCommandHandler"/> class.</summary>
    /// <param name="invoices">The purchase invoice repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="charges">The additional-charge repository.</param>
    /// <param name="ledgers">The nominal ledger repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreatePurchaseInvoiceCommandHandler(
        IPurchaseInvoiceRepository invoices,
        IInventoryMasterRepository masters,
        IProductRepository products,
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
        _charges = charges;
        _ledgers = ledgers;
        _numbering = numbering;
        _financialYears = financialYears;
        _firms = firms;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<PurchaseInvoiceResponse>> Handle(
        CreatePurchaseInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<PurchaseInvoiceResponse>(Error.Forbidden(
                "PurchaseInvoice.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before entering a purchase."));
        }

        Result<Context> loaded = await LoadAsync(request, firmId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(loaded.Error);
        }

        Context context = loaded.Value;

        // Before a number is reserved, so the commonest mistake in a purchase ledger -
        // keying the same supplier invoice twice, and reclaiming its input tax twice -
        // is reported as itself rather than burning a number on a document that is about
        // to be refused.
        if (request.SupplierInvoiceNumber?.Trim() is { Length: > 0 } reference
            && await _invoices.IsSupplierInvoiceNumberInUseAsync(
                firmId, context.Supplier.Id, reference, cancellationToken))
        {
            return Result.Failure<PurchaseInvoiceResponse>(Error.Conflict(
                "PurchaseInvoice.SupplierInvoiceAlreadyEntered",
                $"'{context.Supplier.Name}' invoice '{reference}' is already on file."));
        }

        Result<string> number = await ReserveAsync(
            request.Kind, firmId, branchId, context.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(number.Error);
        }

        TaxMode mode = request.Mode ?? DefaultModeFor(context.Firm);

        Result<PurchaseInvoice> draft = PurchaseInvoice.CreateDraft(
            _tenantContext.TenantId,
            firmId,
            branchId,
            context.Year,
            number.Value,
            request.Date,
            context.Supplier,
            context.Warehouse,
            mode,
            context.Firm.BaseCurrency,
            request.Kind,
            request.ReturnsInvoiceId is { } returns ? PurchaseInvoiceId.From(returns) : null);

        if (draft.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(draft.Error);
        }

        PurchaseInvoice invoice = draft.Value;

        Result lined = AddLines(invoice, request, context, mode);

        if (lined.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(lined.Error);
        }

        Result charged = AddCharges(invoice, request, context);

        if (charged.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(charged.Error);
        }

        Result details = invoice.SetSupplierDocument(
            request.SupplierInvoiceNumber, request.SupplierInvoiceDate, request.Narration);

        if (details.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceResponse>(details.Error);
        }

        _invoices.Add(invoice);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(invoice));
    }

    /// <summary>Describes a purchase as it now stands.</summary>
    /// <param name="invoice">The document.</param>
    /// <returns>Its figures.</returns>
    internal static PurchaseInvoiceResponse Describe(PurchaseInvoice invoice) =>
        new(
            invoice.Id.Value,
            invoice.Number,
            invoice.Status,
            invoice.Taxable.Amount,
            invoice.Tax.Amount,
            invoice.ChargeTotal.Amount,
            invoice.RoundingDifference.Amount,
            invoice.Total.Amount);

    /// <summary>The mode a firm's purchases open in, from its own regime.</summary>
    private static TaxMode DefaultModeFor(Firm firm) =>
        firm.TaxRegime == TaxRegime.None ? TaxMode.NonTax : TaxMode.Tax;

    /// <summary>Builds each line, converting its quantity and assessing its tax.</summary>
    private static Result AddLines(
        PurchaseInvoice invoice,
        CreatePurchaseInvoiceCommand request,
        Context context,
        TaxMode mode)
    {
        foreach (PurchaseInvoiceLineInput input in request.Lines)
        {
            Product product = context.Products[ProductId.From(input.ProductId)];
            UnitOfMeasure stockUnit = context.Units[product.StockUnitId];

            UnitOfMeasure entryUnit = input.UnitId is { } unitId
                ? context.Units[UnitOfMeasureId.From(unitId)]
                : stockUnit;

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

            Result<PurchaseInvoiceLine> added = invoice.AddLine(
                product,
                entryUnit,
                input.Quantity,
                stockQuantity.Value,
                input.Rate,
                assessment,
                input.BatchNumber,
                input.ExpiresOn,
                input.SerialNumbers,
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
    /// The place-of-supply comparison selects IGST over CGST plus SGST, and it is inert
    /// outside the GST regime. It reads the supplier's state rather than the customer's,
    /// which is the only thing that changes from the sales side: a purchase from another
    /// state is an inter-state supply in the direction the firm is receiving it. Shared
    /// with the order, because it is one question and two copies would eventually disagree.
    /// </remarks>
    private static TaxContext ContextFor(Context context, TaxMode mode) =>
        PurchaseTaxContext.For(context.Firm, context.Supplier, mode);

    /// <summary>Adds the charges the caller entered.</summary>
    private static Result AddCharges(
        PurchaseInvoice invoice,
        CreatePurchaseInvoiceCommand request,
        Context context)
    {
        foreach (PurchaseInvoiceChargeInput input in request.Charges ?? [])
        {
            if (!context.Charges.TryGetValue(
                LedgerId.From(input.LedgerId), out AdditionalLedger? mapping))
            {
                return Result.Failure(Error.NotFound(
                    "PurchaseInvoice.ChargeNotMapped",
                    "That charge is not one this firm carries on a purchase document."));
            }

            Result<PurchaseInvoiceCharge> added = invoice.AddCharge(mapping, input.Amount);

            if (added.IsFailure)
            {
                return Result.Failure(added.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>Loads and checks everything the lines name, before any of it is used.</summary>
    private async Task<Result<Context>> LoadAsync(
        CreatePurchaseInvoiceCommand request,
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
                + "recording a purchase on that date."));
        }

        Ledger? supplier = await _ledgers.FindAsync(
            LedgerId.From(request.SupplierLedgerId), cancellationToken);

        if (supplier is null || supplier.FirmId != firmId)
        {
            return Result.Failure<Context>(Error.NotFound(
                "PurchaseInvoice.SupplierNotFound",
                "That supplier account is not in the selected firm."));
        }

        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.WarehouseId), cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId)
        {
            return Result.Failure<Context>(Error.NotFound(
                "PurchaseInvoice.WarehouseNotFound",
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
                    "PurchaseInvoice.ProductNotFound",
                    $"Product {id} is not in the selected firm."));
            }

            if (!product.IsActive)
            {
                return Result.Failure<Context>(Error.BusinessRule(
                    "PurchaseInvoice.ProductWithdrawn",
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

        foreach (PurchaseInvoiceLineInput line in request.Lines)
        {
            if (line.UnitId is { } unitId
                && (!units.TryGetValue(UnitOfMeasureId.From(unitId), out UnitOfMeasure? unit)
                    || unit.FirmId != firmId))
            {
                return Result.Failure<Context>(Error.NotFound(
                    "PurchaseInvoice.UnitNotFound",
                    $"Unit {unitId} is not in the selected firm."));
            }
        }

        IReadOnlyList<AdditionalLedger> mapped = await _charges.ListForDocumentAsync(
            firmId, ChargeableDocument.Purchase, cancellationToken);

        return Result.Success(new Context(
            firm,
            year,
            supplier,
            warehouse,
            products,
            units,
            mapped.ToDictionary(charge => charge.LedgerId)));
    }

    /// <summary>Takes the next number, creating the series if there is none.</summary>
    /// <remarks>
    /// A separate series per kind, as the sales side has: a debit note is not a gap in the
    /// purchase sequence, and an auditor asking why PU/2026/0004 does not exist deserves a
    /// better answer than "it was a return".
    /// </remarks>
    private async Task<Result<string>> ReserveAsync(
        PurchaseDocumentKind kind,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        bool isReturn = kind == PurchaseDocumentKind.Return;

        string documentType = isReturn
            ? DocumentTypes.PurchaseReturn
            : DocumentTypes.PurchaseInvoice;

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
                prefix: isReturn ? "PR" : "PU",
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
        Ledger Supplier,
        Warehouse Warehouse,
        IReadOnlyDictionary<ProductId, Product> Products,
        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> Units,
        IReadOnlyDictionary<LedgerId, AdditionalLedger> Charges);
}
