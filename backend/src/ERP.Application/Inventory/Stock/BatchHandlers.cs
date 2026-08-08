using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Stock;

/// <summary>Handles <see cref="CorrectBatchDatesCommand"/>.</summary>
public sealed class CorrectBatchDatesCommandHandler : ICommandHandler<CorrectBatchDatesCommand>
{
    private readonly IBatchRepository _batches;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CorrectBatchDatesCommandHandler"/> class.</summary>
    /// <param name="batches">The batch repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CorrectBatchDatesCommandHandler(
        IBatchRepository batches,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _batches = batches;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        CorrectBatchDatesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure(Error.Forbidden(
                "Batch.NoFirmSelected", "A firm must be selected to work with batches."));
        }

        Batch? batch = await _batches.FindAsync(BatchId.From(request.BatchId), cancellationToken);

        if (batch is null || batch.FirmId != firmId)
        {
            return Result.Failure(Error.NotFound(
                "Batch.NotFound", "No such batch in the selected firm."));
        }

        Result dated = batch.SetDates(request.ManufacturedOn, request.ExpiresOn);

        if (dated.IsFailure)
        {
            return dated;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles the three batch read queries.</summary>
public sealed class BatchQueryHandler
    : IQueryHandler<ListProductBatchesQuery, IReadOnlyList<BatchStockRow>>,
      IQueryHandler<BatchStockQuery, BatchStockReport>,
      IQueryHandler<ExpiryReportQuery, IReadOnlyList<BatchStockRow>>
{
    private readonly IBatchReader _reader;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="BatchQueryHandler"/> class.</summary>
    /// <param name="reader">The batch reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock, for judging what has expired.</param>
    public BatchQueryHandler(IBatchReader reader, ITenantContext tenantContext, IClock clock)
    {
        _reader = reader;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BatchStockRow>>> Handle(
        ListProductBatchesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<BatchStockRow>>(NoFirm);
        }

        return Result.Success(await _reader.ForProductAsync(
            firmId,
            ProductId.From(request.ProductId),
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            request.IncludeEmpty,
            Today,
            cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<BatchStockReport>> Handle(
        BatchStockQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<BatchStockReport>(NoFirm);
        }

        return Result.Success(await _reader.StockAsync(
            firmId,
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            request.ProductId is { } product ? ProductId.From(product) : null,
            request.CategoryId is { } category ? CategoryId.From(category) : null,
            request.IncludeZero,
            Today,
            cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BatchStockRow>>> Handle(
        ExpiryReportQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<BatchStockRow>>(NoFirm);
        }

        if (request.WithinDays is < 0)
        {
            return Result.Failure<IReadOnlyList<BatchStockRow>>(Error.Validation(
                "Batch.HorizonNegative",
                "A number of days to look ahead cannot be negative. Leave it empty to "
                + "report only what has already expired."));
        }

        return Result.Success(await _reader.ExpiringAsync(
            firmId,
            request.AsOn == default ? Today : request.AsOn,
            request.WithinDays,
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            request.CategoryId is { } category ? CategoryId.From(category) : null,
            cancellationToken));
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

    private static Error NoFirm => Error.Forbidden(
        "Batch.NoFirmSelected", "A firm must be selected to read batches.");
}
