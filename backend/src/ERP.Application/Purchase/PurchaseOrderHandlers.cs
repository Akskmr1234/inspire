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

/// <summary>Handles <see cref="CreatePurchaseOrderCommand"/>.</summary>
/// <remarks>
/// Everything is loaded and checked before anything is built, as a purchase's entry does: a
/// product from another firm found halfway through would leave a reserved order number burnt
/// on a document that never existed.
/// </remarks>
public sealed class CreatePurchaseOrderCommandHandler
    : ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderResponse>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IAdditionalLedgerRepository _charges;
    private readonly ILedgerRepository _ledgers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreatePurchaseOrderCommandHandler"/> class.</summary>
    /// <param name="orders">The purchase order repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="charges">The additional-charge repository.</param>
    /// <param name="ledgers">The nominal ledger repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreatePurchaseOrderCommandHandler(
        IPurchaseOrderRepository orders,
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
        _orders = orders;
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
    public async Task<Result<PurchaseOrderResponse>> Handle(
        CreatePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<PurchaseOrderResponse>(Error.Forbidden(
                "PurchaseOrder.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before entering an order."));
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<PurchaseOrderResponse>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}."));
        }

        Ledger? supplier = await _ledgers.FindAsync(
            LedgerId.From(request.SupplierLedgerId), cancellationToken);

        if (supplier is null || supplier.FirmId != firmId)
        {
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound(
                "PurchaseOrder.SupplierNotFound",
                "That supplier account is not in the selected firm."));
        }

        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.WarehouseId), cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId)
        {
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound(
                "PurchaseOrder.WarehouseNotFound",
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
                return Result.Failure<PurchaseOrderResponse>(Error.NotFound(
                    "PurchaseOrder.ProductNotFound",
                    $"Product {id} is not in the selected firm."));
            }

            if (!product.IsActive)
            {
                return Result.Failure<PurchaseOrderResponse>(Error.BusinessRule(
                    "PurchaseOrder.ProductWithdrawn",
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

        Result<string> number = await ReserveAsync(firmId, branchId, year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<PurchaseOrderResponse>(number.Error);
        }

        TaxMode mode = request.Mode
            ?? (firm.TaxRegime == TaxRegime.None ? TaxMode.NonTax : TaxMode.Tax);

        Result<PurchaseOrder> draft = PurchaseOrder.CreateDraft(
            _tenantContext.TenantId,
            firmId,
            branchId,
            year,
            number.Value,
            request.Date,
            supplier,
            warehouse,
            mode,
            firm.BaseCurrency,
            request.ExpectedOn);

        if (draft.IsFailure)
        {
            return Result.Failure<PurchaseOrderResponse>(draft.Error);
        }

        PurchaseOrder order = draft.Value;

        foreach (PurchaseOrderLineInput input in request.Lines)
        {
            Product product = products[ProductId.From(input.ProductId)];
            UnitOfMeasure stockUnit = units[product.StockUnitId];

            UnitOfMeasure entryUnit = input.UnitId is { } unitId
                ? units[UnitOfMeasureId.From(unitId)]
                : stockUnit;

            Result<decimal> stockQuantity = UnitOfMeasure.Convert(
                input.Quantity, entryUnit, stockUnit);

            if (stockQuantity.IsFailure)
            {
                return Result.Failure<PurchaseOrderResponse>(stockQuantity.Error);
            }

            Result<TaxRate> rate = TaxRate.Create(input.TaxPercentage);

            if (rate.IsFailure)
            {
                return Result.Failure<PurchaseOrderResponse>(rate.Error);
            }

            Money taxable = Money.Of(
                (input.Quantity * input.Rate) - input.Discount, order.Currency);

            TaxAssessment assessment = TaxCalculator.Calculate(
                taxable,
                rate.Value,
                PurchaseTaxContext.For(firm, supplier, mode));

            Result<PurchaseOrderLine> added = order.AddLine(
                product, entryUnit, input.Quantity, stockQuantity.Value, input.Rate,
                assessment, input.Discount);

            if (added.IsFailure)
            {
                return Result.Failure<PurchaseOrderResponse>(added.Error);
            }
        }

        IReadOnlyList<AdditionalLedger> mapped = await _charges.ListForDocumentAsync(
            firmId, ChargeableDocument.PurchaseOrder, cancellationToken);

        Dictionary<LedgerId, AdditionalLedger> byLedger =
            mapped.ToDictionary(charge => charge.LedgerId);

        foreach (PurchaseInvoiceChargeInput input in request.Charges ?? [])
        {
            if (!byLedger.TryGetValue(LedgerId.From(input.LedgerId), out AdditionalLedger? charge))
            {
                return Result.Failure<PurchaseOrderResponse>(Error.NotFound(
                    "PurchaseOrder.ChargeNotMapped",
                    "That charge is not one this firm carries on an order."));
            }

            Result<PurchaseOrderCharge> agreed = order.AddCharge(charge, input.Amount);

            if (agreed.IsFailure)
            {
                return Result.Failure<PurchaseOrderResponse>(agreed.Error);
            }
        }

        Result details = order.SetDetails(request.ReferenceNumber, request.Narration);

        if (details.IsFailure)
        {
            return Result.Failure<PurchaseOrderResponse>(details.Error);
        }

        _orders.Add(order);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(order));
    }

    /// <summary>Describes an order as it now stands.</summary>
    /// <param name="order">The order.</param>
    /// <returns>Its figures.</returns>
    internal static PurchaseOrderResponse Describe(PurchaseOrder order) =>
        new(
            order.Id.Value,
            order.Number,
            order.Status,
            order.Taxable.Amount,
            order.Tax.Amount,
            order.ChargeTotal.Amount,
            order.RoundingDifference.Amount,
            order.Total.Amount);

    /// <summary>Takes the next order number, creating the series if there is none.</summary>
    private async Task<Result<string>> ReserveAsync(
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            DocumentTypes.PurchaseOrder, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                _tenantContext.TenantId, firmId, DocumentTypes.PurchaseOrder, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat("PO", null, "/", year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }
}

/// <summary>The tax conditions a purchase document is entered under.</summary>
/// <remarks>
/// Shared by the purchase and the order because it is one question - which heads apply to
/// goods coming from this supplier - and two copies would eventually disagree about a
/// supplier whose state nobody recorded. It reads the <em>supplier's</em> state, which is
/// the only thing that differs from the sales side: a purchase from another state is an
/// inter-state supply in the direction the firm is receiving it.
/// </remarks>
internal static class PurchaseTaxContext
{
    /// <summary>Builds the context for a firm buying from a supplier.</summary>
    /// <param name="firm">The firm.</param>
    /// <param name="supplier">The supplier.</param>
    /// <param name="mode">The tax mode the document is entered under.</param>
    /// <returns>The context the engine assesses against.</returns>
    internal static TaxContext For(Firm firm, Ledger supplier, TaxMode mode) =>
        new(
            firm.TaxRegime,
            mode == TaxMode.NonTax ? DocumentTaxMode.NonTax : DocumentTaxMode.Taxable,
            AmountsIncludeTax: false,
            IsInterStateSupply:
                firm.StateCode is { } firmState
                && supplier.StateCode is { } supplierState
                && !string.Equals(firmState, supplierState, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Handles <see cref="ConfirmPurchaseOrderCommand"/>.</summary>
public sealed class ConfirmPurchaseOrderCommandHandler
    : ICommandHandler<ConfirmPurchaseOrderCommand, PurchaseOrderResponse>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ConfirmPurchaseOrderCommandHandler"/> class.</summary>
    /// <param name="orders">The purchase order repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ConfirmPurchaseOrderCommandHandler(
        IPurchaseOrderRepository orders,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<PurchaseOrderResponse>> Handle(
        ConfirmPurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<PurchaseOrder> found = await PurchaseOrderLookup.ResolveAsync(
            _orders, _tenantContext, request.PurchaseOrderId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<PurchaseOrderResponse>(found.Error);
        }

        Result confirmed = found.Value.Confirm(_currentUser.UserId, _clock.UtcNow);

        if (confirmed.IsFailure)
        {
            return Result.Failure<PurchaseOrderResponse>(confirmed.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CreatePurchaseOrderCommandHandler.Describe(found.Value));
    }
}

/// <summary>Handles <see cref="ClosePurchaseOrderCommand"/>.</summary>
public sealed class ClosePurchaseOrderCommandHandler : ICommandHandler<ClosePurchaseOrderCommand>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ClosePurchaseOrderCommandHandler"/> class.</summary>
    /// <param name="orders">The purchase order repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ClosePurchaseOrderCommandHandler(
        IPurchaseOrderRepository orders,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        ClosePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<PurchaseOrder> found = await PurchaseOrderLookup.ResolveAsync(
            _orders, _tenantContext, request.PurchaseOrderId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Result closed = found.Value.Close(request.Reason);

        if (closed.IsFailure)
        {
            return closed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="GetPurchaseOrderQuery"/>.</summary>
public sealed class GetPurchaseOrderQueryHandler
    : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDetail>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetPurchaseOrderQueryHandler"/> class.</summary>
    /// <param name="orders">The purchase order repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetPurchaseOrderQueryHandler(
        IPurchaseOrderRepository orders,
        ITenantContext tenantContext)
    {
        _orders = orders;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<PurchaseOrderDetail>> Handle(
        GetPurchaseOrderQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<PurchaseOrder> found = await PurchaseOrderLookup.ResolveAsync(
            _orders, _tenantContext, request.PurchaseOrderId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<PurchaseOrderDetail>(found.Error);
        }

        PurchaseOrder order = found.Value;

        return Result.Success(new PurchaseOrderDetail(
            CreatePurchaseOrderCommandHandler.Describe(order),
            order.Date,
            order.ExpectedOn,
            order.SupplierLedgerId.Value,
            order.WarehouseId.Value,
            order.Mode,
            order.Currency.Code,
            order.ReferenceNumber,
            order.Narration,
            order.ClosureReason,
            [
                .. order.Lines.OrderBy(line => line.LineNumber).Select(line =>
                    new PurchaseOrderLineDetail(
                        line.Id.Value,
                        line.LineNumber,
                        line.ProductId.Value,
                        line.UnitId.Value,
                        line.Quantity,
                        line.InvoicedQuantity,
                        line.OutstandingQuantity,
                        line.Rate,
                        line.Discount,
                        line.TaxableAmount.Amount,
                        line.TaxAmount.Amount,
                        [
                            .. line.Components.Select(component =>
                                new PurchaseInvoiceLineTaxDetail(
                                    component.Type, component.Percentage, component.Amount)),
                        ])),
            ],
            [
                .. order.Charges.Select(charge =>
                    new PurchaseInvoiceChargeDetail(
                        charge.LedgerId.Value, charge.Amount.Amount, charge.IsAddition)),
            ]));
    }
}

/// <summary>Handles <see cref="ListPurchaseOrdersQuery"/>.</summary>
public sealed class ListPurchaseOrdersQueryHandler
    : IQueryHandler<ListPurchaseOrdersQuery, Abstractions.PagedResult<PurchaseOrderSummary>>
{
    private readonly IPurchaseOrderReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ListPurchaseOrdersQueryHandler"/> class.</summary>
    /// <param name="reader">The purchase order reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public ListPurchaseOrdersQueryHandler(
        IPurchaseOrderReader reader,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<Abstractions.PagedResult<PurchaseOrderSummary>>> Handle(
        ListPurchaseOrdersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Abstractions.PagedResult<PurchaseOrderSummary>>(Error.Forbidden(
                "PurchaseOrder.NoFirmSelected", "A firm must be selected to list orders."));
        }

        return Result.Success(await _reader.ListAsync(
            firmId,
            new PurchaseOrderFilter(
                request.From,
                request.To,
                request.Status,
                request.SupplierLedgerId is { } supplier ? LedgerId.From(supplier) : null,
                request.Search,
                request.OutstandingOnly),
            request.Page,
            request.PageSize,
            cancellationToken));
    }
}

/// <summary>Finding an order and refusing one that belongs to another firm.</summary>
internal static class PurchaseOrderLookup
{
    /// <summary>Finds an order in the selected firm.</summary>
    /// <param name="orders">The purchase order repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The order, or the reason it could not be reached.</returns>
    internal static async Task<Result<PurchaseOrder>> ResolveAsync(
        IPurchaseOrderRepository orders,
        ITenantContext tenantContext,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<PurchaseOrder>(Error.Forbidden(
                "PurchaseOrder.NoFirmSelected",
                "A firm must be selected to work with orders."));
        }

        PurchaseOrder? order = await orders.FindAsync(
            PurchaseOrderId.From(orderId), cancellationToken);

        return order is null || order.FirmId != firmId
            ? Result.Failure<PurchaseOrder>(Error.NotFound(
                "PurchaseOrder.NotFound", "That order does not exist in the selected firm."))
            : Result.Success(order);
    }
}
