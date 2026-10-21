using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Platform.Settings;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Stock;

/// <summary>Handles the transfer request's own lifecycle: asked for, agreed, refused, withdrawn.</summary>
public sealed class StockTransferRequestHandlers
    : ICommandHandler<CreateStockTransferRequestCommand, StockTransferResponse>,
      ICommandHandler<UpdateStockTransferRequestCommand, StockTransferResponse>,
      ICommandHandler<ApproveStockTransferCommand, StockTransferResponse>,
      ICommandHandler<RejectStockTransferCommand, StockTransferResponse>,
      ICommandHandler<CancelStockTransferCommand, StockTransferResponse>
{
    private readonly IStockTransferRequestRepository _requests;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IFinancialYearRepository _financialYears;
    private readonly INumberingSeriesRepository _numbering;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="StockTransferRequestHandlers"/> class.</summary>
    /// <param name="requests">The transfer request repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public StockTransferRequestHandlers(
        IStockTransferRequestRepository requests,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IFinancialYearRepository financialYears,
        INumberingSeriesRepository numbering,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _requests = requests;
        _masters = masters;
        _products = products;
        _financialYears = financialYears;
        _numbering = numbering;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<StockTransferResponse>> Handle(
        CreateStockTransferRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<StockTransferResponse>(StockTransferAccess.NoFirm());
        }

        Result<(Warehouse From, Warehouse To)> places = await WarehousesAsync(
            firmId, request.FromWarehouseId, request.ToWarehouseId, cancellationToken);

        if (places.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(places.Error);
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<StockTransferResponse>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}."));
        }

        Result<string> number = await StockDocumentWriter.ReserveAsync(
            _numbering, _tenantContext.TenantId, DocumentTypes.StockTransferRequest, firmId,
            branchId, year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(number.Error);
        }

        Result<StockTransferRequest> created = StockTransferRequest.Create(
            _tenantContext.TenantId, firmId, year, number.Value, request.Date,
            places.Value.From, places.Value.To);

        if (created.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(created.Error);
        }

        StockTransferRequest transfer = created.Value;

        Result filled = await FillAsync(
            transfer, request.Date, places.Value, request.Lines, request.Narration,
            cancellationToken);

        if (filled.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(filled.Error);
        }

        _requests.Add(transfer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(transfer));
    }

    /// <inheritdoc />
    public async Task<Result<StockTransferResponse>> Handle(
        UpdateStockTransferRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockTransferRequest> found = await StockTransferAccess.FindAsync(
            _requests, _tenantContext, request.StockTransferRequestId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(found.Error);
        }

        StockTransferRequest transfer = found.Value;

        Result<(Warehouse From, Warehouse To)> places = await WarehousesAsync(
            transfer.FirmId, request.FromWarehouseId, request.ToWarehouseId, cancellationToken);

        if (places.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(places.Error);
        }

        Result cleared = transfer.ClearLines();

        if (cleared.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(cleared.Error);
        }

        Result filled = await FillAsync(
            transfer, request.Date, places.Value, request.Lines, request.Narration,
            cancellationToken);

        if (filled.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(filled.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(transfer));
    }

    /// <inheritdoc />
    public Task<Result<StockTransferResponse>> Handle(
        ApproveStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Dictionary<ProductId, decimal>? approved = request.Lines?
            .GroupBy(line => ProductId.From(line.ProductId))
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        return ChangeAsync(
            request.StockTransferRequestId,
            transfer => transfer.Approve(_currentUser.UserId, _clock.UtcNow, approved),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<StockTransferResponse>> Handle(
        RejectStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(
            request.StockTransferRequestId,
            transfer => transfer.Reject(_currentUser.UserId, _clock.UtcNow, request.Reason),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<StockTransferResponse>> Handle(
        CancelStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(
            request.StockTransferRequestId,
            transfer => transfer.Cancel(request.Reason),
            cancellationToken);
    }

    private static StockTransferResponse Describe(StockTransferRequest transfer) =>
        new(transfer.Id.Value, transfer.Number, transfer.Status);

    private async Task<Result<StockTransferResponse>> ChangeAsync(
        Guid requestId,
        Func<StockTransferRequest, Result> change,
        CancellationToken cancellationToken)
    {
        Result<StockTransferRequest> found = await StockTransferAccess.FindAsync(
            _requests, _tenantContext, requestId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(found.Error);
        }

        Result changed = change(found.Value);

        if (changed.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(changed.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(found.Value));
    }

    private async Task<Result<(Warehouse From, Warehouse To)>> WarehousesAsync(
        FirmId firmId,
        Guid fromId,
        Guid toId,
        CancellationToken cancellationToken)
    {
        Warehouse? from = await _masters.FindWarehouseAsync(WarehouseId.From(fromId), cancellationToken);
        Warehouse? to = await _masters.FindWarehouseAsync(WarehouseId.From(toId), cancellationToken);

        return from is null || to is null || from.FirmId != firmId || to.FirmId != firmId
            ? Result.Failure<(Warehouse, Warehouse)>(Error.NotFound(
                "StockTransfer.WarehouseNotFound", "No such warehouse in the selected firm."))
            : Result.Success((from, to));
    }

    private async Task<Result> FillAsync(
        StockTransferRequest transfer,
        DateOnly date,
        (Warehouse From, Warehouse To) places,
        IReadOnlyList<StockTransferLineInput> lines,
        string? narration,
        CancellationToken cancellationToken)
    {
        Result revised = transfer.Revise(date, places.From, places.To, narration);

        if (revised.IsFailure)
        {
            return revised;
        }

        List<ProductId> productIds = [.. lines.Select(line => ProductId.From(line.ProductId)).Distinct()];

        IReadOnlyDictionary<ProductId, Product> products =
            await _products.GetManyAsync(productIds, cancellationToken);

        List<UnitOfMeasureId> unitIds =
        [
            .. lines.Where(line => line.UnitId is not null)
                .Select(line => UnitOfMeasureId.From(line.UnitId!.Value))
                .Concat(products.Values.Select(product => product.StockUnitId))
                .Distinct(),
        ];

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units =
            await _masters.GetUnitsAsync(unitIds, cancellationToken);

        foreach (StockTransferLineInput line in lines)
        {
            if (!products.TryGetValue(ProductId.From(line.ProductId), out Product? product)
                || product.FirmId != transfer.FirmId)
            {
                return Result.Failure(Error.NotFound(
                    "StockTransfer.ProductNotFound", "A product on the request is not in the firm."));
            }

            UnitOfMeasure stockUnit = units[product.StockUnitId];
            UnitOfMeasure? entryUnit = line.UnitId is { } unitId
                ? units.GetValueOrDefault(UnitOfMeasureId.From(unitId))
                : stockUnit;

            if (entryUnit is null || entryUnit.FirmId != transfer.FirmId)
            {
                return Result.Failure(Error.NotFound(
                    "StockTransfer.UnitNotFound", "A unit on the request is not in the firm."));
            }

            Result<decimal> stockQuantity = UnitOfMeasure.Convert(line.Quantity, entryUnit, stockUnit);

            if (stockQuantity.IsFailure)
            {
                return stockQuantity;
            }

            Result<StockTransferRequestLine> added = transfer.AddLine(
                product, entryUnit, line.Quantity, stockQuantity.Value, line.Remarks);

            if (added.IsFailure)
            {
                return added;
            }
        }

        return Result.Success();
    }
}

/// <summary>Handles the two movements of a transfer request: sending the goods and receiving them.</summary>
/// <remarks>
/// Each is an ordinary transfer, through the same writer the stock screen uses: out of the
/// source into goods in transit, then out of transit into the destination. The receipt
/// repeats the dispatch line for line - the same batches and the same serial numbers - so
/// what arrives is exactly what left, at the cost it left at.
/// </remarks>
public sealed class StockTransferMovementHandlers
    : ICommandHandler<DispatchStockTransferCommand, StockTransferResponse>,
      ICommandHandler<ReceiveStockTransferCommand, StockTransferResponse>
{
    private readonly IStockTransferRequestRepository _requests;
    private readonly IStockDocumentRepository _documents;
    private readonly IInventoryMasterRepository _masters;
    private readonly ISerialNumberRepository _serials;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFirmPolicyReader? _policies;
    private readonly StockDocumentWriter _writer;

    /// <summary>Initialises a new instance of the <see cref="StockTransferMovementHandlers"/> class.</summary>
    /// <param name="requests">The transfer request repository.</param>
    /// <param name="documents">The stock document repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="serials">The serial-number repository.</param>
    /// <param name="balances">The stock balance repository.</param>
    /// <param name="batchBalances">The batch position repository.</param>
    /// <param name="ledger">The stock ledger repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="accounts">The inventory account map repository.</param>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="policies">The firm's policies, which decide whether stock may go below zero.</param>
    public StockTransferMovementHandlers(
        IStockTransferRequestRepository requests,
        IStockDocumentRepository documents,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ISerialNumberRepository serials,
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IStockLedgerRepository ledger,
        IFinancialYearRepository financialYears,
        INumberingSeriesRepository numbering,
        IInventoryAccountMapRepository accounts,
        IVoucherRepository vouchers,
        IFirmRepository firms,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork,
        IFirmPolicyReader? policies = null)
    {
        _requests = requests;
        _documents = documents;
        _masters = masters;
        _serials = serials;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _policies = policies;
        _writer = new StockDocumentWriter(
            documents, masters, products, batches, serials, balances, batchBalances, ledger,
            financialYears, numbering, accounts, vouchers, firms, tenantContext, currentUser,
            clock);
    }

    /// <inheritdoc />
    public async Task<Result<StockTransferResponse>> Handle(
        DispatchStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<StockTransferResponse>(StockTransferAccess.NoFirm());
        }

        Result<StockTransferRequest> found = await StockTransferAccess.FindAsync(
            _requests, _tenantContext, request.StockTransferRequestId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(found.Error);
        }

        StockTransferRequest transfer = found.Value;

        if (transfer.Status != StockTransferStatus.Approved)
        {
            return Result.Failure<StockTransferResponse>(Error.BusinessRule(
                "StockTransfer.WrongStatus",
                $"Request '{transfer.Number}' is {transfer.Status}; only an approved request is dispatched."));
        }

        Result<Warehouse> transit = await TransitAsync(transfer.FirmId, cancellationToken);

        if (transit.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(transit.Error);
        }

        bool allowNegative = _policies is not null
            && (await _policies.GetAsync(transfer.FirmId, cancellationToken)).AllowsNegativeStock;

        Result<StockWritten> written = await _writer.WriteAsync(
            new CreateStockDocumentCommand(
                StockDocumentType.StockTransfer,
                request.Date,
                transfer.FromWarehouseId.Value,
                request.Lines,
                transit.Value.Id.Value,
                transfer.Number,
                request.Narration ?? $"Dispatch against {transfer.Number}"),
            transfer.FirmId,
            branchId,
            allowNegative,
            throughTransferRequest: true,
            cancellationToken);

        if (written.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(written.Error);
        }

        StockDocument document = written.Value.Document;

        Dictionary<ProductId, decimal> sent = document.Lines
            .GroupBy(line => line.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.StockQuantity));

        Result dispatched = transfer.Dispatch(document.Id, sent, _currentUser.UserId, _clock.UtcNow);

        if (dispatched.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(dispatched.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new StockTransferResponse(
            transfer.Id.Value, transfer.Number, transfer.Status, document.Number));
    }

    /// <inheritdoc />
    public async Task<Result<StockTransferResponse>> Handle(
        ReceiveStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<StockTransferResponse>(StockTransferAccess.NoFirm());
        }

        Result<StockTransferRequest> found = await StockTransferAccess.FindAsync(
            _requests, _tenantContext, request.StockTransferRequestId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(found.Error);
        }

        StockTransferRequest transfer = found.Value;

        if (transfer.Status != StockTransferStatus.Dispatched
            || transfer.DispatchDocumentId is not { } dispatchId)
        {
            return Result.Failure<StockTransferResponse>(Error.BusinessRule(
                "StockTransfer.WrongStatus",
                $"Request '{transfer.Number}' is {transfer.Status}; only dispatched goods are received."));
        }

        StockDocument? dispatch = await _documents.FindAsync(dispatchId, cancellationToken);

        if (dispatch is null || dispatch.DestinationWarehouseId is not { } transitId)
        {
            return Result.Failure<StockTransferResponse>(Error.NotFound(
                "StockTransfer.DispatchNotFound", "The dispatch of this request no longer exists."));
        }

        IReadOnlyDictionary<SerialNumberId, SerialNumber> units =
            await _serials.ForDocumentAsync(dispatch.Id, cancellationToken);

        // What left, line for line: the same unit, quantity, batch and serial numbers.
        List<StockDocumentLineInput> lines =
        [
            .. dispatch.Lines.OrderBy(line => line.LineNumber).Select(line => new StockDocumentLineInput(
                line.ProductId.Value,
                line.Quantity,
                line.UnitId.Value,
                Remarks: line.Remarks,
                BatchId: line.BatchId?.Value,
                SerialNumbers: line.Serials.Count == 0
                    ? null
                    : [.. line.Serials.Select(named => units[named.SerialNumberId].Number)])),
        ];

        Result<StockWritten> written = await _writer.WriteAsync(
            new CreateStockDocumentCommand(
                StockDocumentType.StockTransfer,
                request.Date,
                transitId.Value,
                lines,
                transfer.ToWarehouseId.Value,
                transfer.Number,
                $"Receipt against {transfer.Number}"),
            transfer.FirmId,
            branchId,
            allowNegative: false,
            throughTransferRequest: true,
            cancellationToken);

        if (written.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(written.Error);
        }

        Result received = transfer.Receive(written.Value.Document.Id, _currentUser.UserId, _clock.UtcNow);

        if (received.IsFailure)
        {
            return Result.Failure<StockTransferResponse>(received.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new StockTransferResponse(
            transfer.Id.Value, transfer.Number, transfer.Status, written.Value.Document.Number));
    }

    /// <summary>Finds the firm's goods-in-transit location, making it on the first dispatch.</summary>
    private async Task<Result<Warehouse>> TransitAsync(FirmId firmId, CancellationToken cancellationToken)
    {
        if (await _masters.FindTransitWarehouseAsync(firmId, cancellationToken) is { } existing)
        {
            return Result.Success(existing);
        }

        string code = "TRANSIT";

        if (await _masters.CodeExistsAsync(InventoryMasterKind.Warehouse, firmId, code, cancellationToken))
        {
            code = $"TRANSIT-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        }

        Result<Warehouse> created = Warehouse.CreateTransit(_tenantContext.TenantId, firmId, code);

        if (created.IsFailure)
        {
            return created;
        }

        _masters.Add(created.Value);

        // Saved now, inside the same transaction, so the transfer that follows finds it
        // the way it finds every other warehouse.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return created;
    }
}

/// <summary>Handles the transfer request queries.</summary>
public sealed class StockTransferQueryHandlers
    : IQueryHandler<ListStockTransferRequestsQuery, IReadOnlyList<StockTransferSummary>>,
      IQueryHandler<GetStockTransferRequestQuery, StockTransferDetail>
{
    private readonly IStockTransferReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="StockTransferQueryHandlers"/> class.</summary>
    /// <param name="reader">The transfer reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public StockTransferQueryHandlers(IStockTransferReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StockTransferSummary>>> Handle(
        ListStockTransferRequestsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<StockTransferSummary>>(StockTransferAccess.NoFirm());
        }

        return Result.Success(await _reader.ListAsync(
            firmId,
            request.From,
            request.To,
            request.Status,
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<StockTransferDetail>> Handle(
        GetStockTransferRequestQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<StockTransferDetail>(StockTransferAccess.NoFirm());
        }

        StockTransferDetail? found = await _reader.FindAsync(
            firmId, StockTransferRequestId.From(request.StockTransferRequestId), cancellationToken);

        return found is null
            ? Result.Failure<StockTransferDetail>(StockTransferAccess.NotFound())
            : Result.Success(found);
    }
}

/// <summary>Finding a request in the selected firm.</summary>
internal static class StockTransferAccess
{
    internal static Error NoFirm() => Error.Forbidden(
        "StockTransfer.NoFirmOrBranchSelected",
        "A firm and a branch must be selected to work with transfer requests.");

    internal static Error NotFound() => Error.NotFound(
        "StockTransfer.NotFound", "No such transfer request in the selected firm.");

    internal static async Task<Result<StockTransferRequest>> FindAsync(
        IStockTransferRequestRepository requests,
        ITenantContext tenantContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<StockTransferRequest>(NoFirm());
        }

        StockTransferRequest? found = await requests.FindAsync(
            StockTransferRequestId.From(id), cancellationToken);

        return found is null || found.FirmId != firmId
            ? Result.Failure<StockTransferRequest>(NotFound())
            : Result.Success(found);
    }
}
