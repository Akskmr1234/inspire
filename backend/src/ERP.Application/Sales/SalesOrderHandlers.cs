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

/// <summary>Handles <see cref="CreateSalesOrderCommand"/>.</summary>
/// <remarks>
/// Everything is loaded and checked before anything is built, as an invoice's entry does:
/// a product from another firm found halfway through would leave a reserved order number
/// burnt on a document that never existed.
/// </remarks>
public sealed class CreateSalesOrderCommandHandler
    : ICommandHandler<CreateSalesOrderCommand, SalesOrderResponse>
{
    private readonly ISalesOrderRepository _orders;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IAdditionalLedgerRepository _charges;
    private readonly ILedgerRepository _ledgers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreateSalesOrderCommandHandler"/> class.</summary>
    /// <param name="orders">The sales order repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="charges">The additional-charge repository.</param>
    /// <param name="ledgers">The nominal ledger repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateSalesOrderCommandHandler(
        ISalesOrderRepository orders,
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
    public async Task<Result<SalesOrderResponse>> Handle(
        CreateSalesOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<SalesOrderResponse>(Error.Forbidden(
                "SalesOrder.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before entering an order."));
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<SalesOrderResponse>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<SalesOrderResponse>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}."));
        }

        Ledger? customer = await _ledgers.FindAsync(
            LedgerId.From(request.CustomerLedgerId), cancellationToken);

        if (customer is null || customer.FirmId != firmId)
        {
            return Result.Failure<SalesOrderResponse>(Error.NotFound(
                "SalesOrder.CustomerNotFound",
                "That customer account is not in the selected firm."));
        }

        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.WarehouseId), cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId)
        {
            return Result.Failure<SalesOrderResponse>(Error.NotFound(
                "SalesOrder.WarehouseNotFound",
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
                return Result.Failure<SalesOrderResponse>(Error.NotFound(
                    "SalesOrder.ProductNotFound",
                    $"Product {id} is not in the selected firm."));
            }

            if (!product.IsActive)
            {
                return Result.Failure<SalesOrderResponse>(Error.BusinessRule(
                    "SalesOrder.ProductWithdrawn",
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
            return Result.Failure<SalesOrderResponse>(number.Error);
        }

        TaxMode mode = request.Mode
            ?? (firm.TaxRegime == TaxRegime.None ? TaxMode.NonTax : TaxMode.Tax);

        Result<SalesOrder> draft = SalesOrder.CreateDraft(
            _tenantContext.TenantId,
            firmId,
            branchId,
            year,
            number.Value,
            request.Date,
            customer,
            warehouse,
            mode,
            firm.BaseCurrency,
            request.ExpectedOn);

        if (draft.IsFailure)
        {
            return Result.Failure<SalesOrderResponse>(draft.Error);
        }

        SalesOrder order = draft.Value;

        foreach (SalesOrderLineInput input in request.Lines)
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
                return Result.Failure<SalesOrderResponse>(stockQuantity.Error);
            }

            Result<TaxRate> rate = TaxRate.Create(input.TaxPercentage);

            if (rate.IsFailure)
            {
                return Result.Failure<SalesOrderResponse>(rate.Error);
            }

            Money taxable = Money.Of(
                (input.Quantity * input.Rate) - input.Discount, order.Currency);

            TaxAssessment assessment = TaxCalculator.Calculate(
                taxable,
                rate.Value,
                SalesTaxContext.For(firm, customer, mode));

            Result<SalesOrderLine> added = order.AddLine(
                product, entryUnit, input.Quantity, stockQuantity.Value, input.Rate,
                assessment, input.Discount);

            if (added.IsFailure)
            {
                return Result.Failure<SalesOrderResponse>(added.Error);
            }
        }

        IReadOnlyList<AdditionalLedger> mapped = await _charges.ListForDocumentAsync(
            firmId, ChargeableDocument.SalesOrder, cancellationToken);

        Dictionary<LedgerId, AdditionalLedger> byLedger =
            mapped.ToDictionary(charge => charge.LedgerId);

        foreach (SalesInvoiceChargeInput input in request.Charges ?? [])
        {
            if (!byLedger.TryGetValue(LedgerId.From(input.LedgerId), out AdditionalLedger? charge))
            {
                return Result.Failure<SalesOrderResponse>(Error.NotFound(
                    "SalesOrder.ChargeNotMapped",
                    "That charge is not one this firm carries on an order."));
            }

            Result<SalesOrderCharge> quoted = order.AddCharge(charge, input.Amount);

            if (quoted.IsFailure)
            {
                return Result.Failure<SalesOrderResponse>(quoted.Error);
            }
        }

        Result details = order.SetDetails(request.ReferenceNumber, request.Narration);

        if (details.IsFailure)
        {
            return Result.Failure<SalesOrderResponse>(details.Error);
        }

        _orders.Add(order);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(order));
    }

    /// <summary>Describes an order as it now stands.</summary>
    /// <param name="order">The order.</param>
    /// <returns>Its figures.</returns>
    internal static SalesOrderResponse Describe(SalesOrder order) =>
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
            DocumentTypes.SalesOrder, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                _tenantContext.TenantId, firmId, DocumentTypes.SalesOrder, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat("SO", null, "/", year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }
}

/// <summary>The tax conditions a sales document is entered under.</summary>
/// <remarks>
/// Shared by the invoice and the order because it is one question - which heads apply to
/// this customer - and two copies would eventually disagree about a customer whose state
/// nobody recorded.
/// </remarks>
internal static class SalesTaxContext
{
    /// <summary>Builds the context for a firm selling to a customer.</summary>
    /// <param name="firm">The firm.</param>
    /// <param name="customer">The customer.</param>
    /// <param name="mode">The tax mode the document is entered under.</param>
    /// <returns>The context the engine assesses against.</returns>
    internal static TaxContext For(Firm firm, Ledger customer, TaxMode mode) =>
        new(
            firm.TaxRegime,
            mode == TaxMode.NonTax ? DocumentTaxMode.NonTax : DocumentTaxMode.Taxable,
            AmountsIncludeTax: false,
            IsInterStateSupply:
                firm.StateCode is { } firmState
                && customer.StateCode is { } customerState
                && !string.Equals(firmState, customerState, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Handles <see cref="ConfirmSalesOrderCommand"/>.</summary>
public sealed class ConfirmSalesOrderCommandHandler
    : ICommandHandler<ConfirmSalesOrderCommand, SalesOrderResponse>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ConfirmSalesOrderCommandHandler"/> class.</summary>
    /// <param name="orders">The sales order repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ConfirmSalesOrderCommandHandler(
        ISalesOrderRepository orders,
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
    public async Task<Result<SalesOrderResponse>> Handle(
        ConfirmSalesOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<SalesOrder> found = await SalesOrderLookup.ResolveAsync(
            _orders, _tenantContext, request.SalesOrderId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<SalesOrderResponse>(found.Error);
        }

        Result confirmed = found.Value.Confirm(_currentUser.UserId, _clock.UtcNow);

        if (confirmed.IsFailure)
        {
            return Result.Failure<SalesOrderResponse>(confirmed.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateSalesOrderCommandHandler.Describe(found.Value));
    }
}

/// <summary>Handles <see cref="CloseSalesOrderCommand"/>.</summary>
public sealed class CloseSalesOrderCommandHandler : ICommandHandler<CloseSalesOrderCommand>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CloseSalesOrderCommandHandler"/> class.</summary>
    /// <param name="orders">The sales order repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CloseSalesOrderCommandHandler(
        ISalesOrderRepository orders,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        CloseSalesOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<SalesOrder> found = await SalesOrderLookup.ResolveAsync(
            _orders, _tenantContext, request.SalesOrderId, cancellationToken);

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

/// <summary>Handles <see cref="GetSalesOrderQuery"/>.</summary>
public sealed class GetSalesOrderQueryHandler : IQueryHandler<GetSalesOrderQuery, SalesOrderDetail>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetSalesOrderQueryHandler"/> class.</summary>
    /// <param name="orders">The sales order repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetSalesOrderQueryHandler(ISalesOrderRepository orders, ITenantContext tenantContext)
    {
        _orders = orders;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<SalesOrderDetail>> Handle(
        GetSalesOrderQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<SalesOrder> found = await SalesOrderLookup.ResolveAsync(
            _orders, _tenantContext, request.SalesOrderId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<SalesOrderDetail>(found.Error);
        }

        SalesOrder order = found.Value;

        return Result.Success(new SalesOrderDetail(
            CreateSalesOrderCommandHandler.Describe(order),
            order.Date,
            order.ExpectedOn,
            order.CustomerLedgerId.Value,
            order.WarehouseId.Value,
            order.Mode,
            order.Currency.Code,
            order.ReferenceNumber,
            order.Narration,
            order.ClosureReason,
            [
                .. order.Lines.OrderBy(line => line.LineNumber).Select(line =>
                    new SalesOrderLineDetail(
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
                                new SalesInvoiceLineTaxDetail(
                                    component.Type, component.Percentage, component.Amount)),
                        ])),
            ],
            [
                .. order.Charges.Select(charge =>
                    new SalesInvoiceChargeDetail(
                        charge.LedgerId.Value, charge.Amount.Amount, charge.IsAddition)),
            ]));
    }
}

/// <summary>Handles <see cref="ListSalesOrdersQuery"/>.</summary>
public sealed class ListSalesOrdersQueryHandler
    : IQueryHandler<ListSalesOrdersQuery, Abstractions.PagedResult<SalesOrderSummary>>
{
    private readonly ISalesOrderReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ListSalesOrdersQueryHandler"/> class.</summary>
    /// <param name="reader">The sales order reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public ListSalesOrdersQueryHandler(ISalesOrderReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<Abstractions.PagedResult<SalesOrderSummary>>> Handle(
        ListSalesOrdersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Abstractions.PagedResult<SalesOrderSummary>>(Error.Forbidden(
                "SalesOrder.NoFirmSelected", "A firm must be selected to list orders."));
        }

        return Result.Success(await _reader.ListAsync(
            firmId,
            new SalesOrderFilter(
                request.From,
                request.To,
                request.Status,
                request.CustomerLedgerId is { } customer ? LedgerId.From(customer) : null,
                request.Search,
                request.OutstandingOnly),
            request.Page,
            request.PageSize,
            cancellationToken));
    }
}

/// <summary>Finding an order and refusing one that belongs to another firm.</summary>
internal static class SalesOrderLookup
{
    /// <summary>Finds an order in the selected firm.</summary>
    /// <param name="orders">The sales order repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The order, or the reason it could not be reached.</returns>
    internal static async Task<Result<SalesOrder>> ResolveAsync(
        ISalesOrderRepository orders,
        ITenantContext tenantContext,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<SalesOrder>(Error.Forbidden(
                "SalesOrder.NoFirmSelected", "A firm must be selected to work with orders."));
        }

        SalesOrder? order = await orders.FindAsync(
            SalesOrderId.From(orderId), cancellationToken);

        return order is null || order.FirmId != firmId
            ? Result.Failure<SalesOrder>(Error.NotFound(
                "SalesOrder.NotFound", "That order does not exist in the selected firm."))
            : Result.Success(order);
    }
}
