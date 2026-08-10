using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Stock;

/// <summary>One serialised unit, as a screen shows it.</summary>
/// <param name="SerialNumberId">The unit.</param>
/// <param name="Number">The number on its case.</param>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductDescription">What it is called.</param>
/// <param name="BatchNumber">The batch it arrived in, if the product is batched.</param>
/// <param name="Status">Where the unit stands.</param>
/// <param name="WarehouseId">The warehouse holding it, if one does.</param>
/// <param name="WarehouseName">That warehouse's name.</param>
/// <param name="UnitCost">What this unit cost when it came in.</param>
/// <param name="ReceivedOn">When it was taken into stock.</param>
/// <param name="IssuedOn">When it last left.</param>
/// <param name="WarrantyUntil">The date its warranty runs to.</param>
/// <param name="IsUnderWarranty">Whether that date is still ahead.</param>
public sealed record SerialNumberView(
    Guid SerialNumberId,
    string Number,
    Guid ProductId,
    string ProductCode,
    string ProductDescription,
    string? BatchNumber,
    SerialStatus Status,
    Guid? WarehouseId,
    string? WarehouseName,
    decimal UnitCost,
    DateOnly? ReceivedOn,
    DateOnly? IssuedOn,
    DateOnly? WarrantyUntil,
    bool IsUnderWarranty);

/// <summary>The units of one product that can be picked from.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="WarehouseId">One warehouse, or null for every one.</param>
/// <param name="IncludeGone">
/// Whether to include units that have left. Off by default: this answers "which unit do
/// I send", and a unit in a customer's hands is not an answer.
/// </param>
/// <remarks>
/// Section 12.7's selection on sale. A sold unit never reappears here, which is the
/// promise the aggregate keeps by refusing a second issue and this keeps by not
/// offering one.
/// </remarks>
public sealed record ListProductSerialsQuery(
    Guid ProductId,
    Guid? WarehouseId = null,
    bool IncludeGone = false) : IQuery<IReadOnlyList<SerialNumberView>>;

/// <summary>One unit, found by the number on its case.</summary>
/// <param name="Number">The serial number, as read off the machine.</param>
/// <remarks>
/// What a service desk asks, with the machine in front of them and no idea which
/// product record it belongs to. The number is unique within a product rather than
/// within the firm, so two products can share one - and both come back, because
/// telling somebody there are two is more use than picking one.
/// </remarks>
public sealed record FindSerialQuery(string Number) : IQuery<IReadOnlyList<SerialNumberView>>;

/// <summary>Reads serialised units.</summary>
public interface ISerialReader
{
    /// <summary>Reads the units of one product.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="productId">The product.</param>
    /// <param name="warehouseId">One warehouse, or null for all.</param>
    /// <param name="includeGone">Whether to include units that have left.</param>
    /// <param name="asOn">The date warranty cover is judged on.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The units, by number.</returns>
    Task<IReadOnlyList<SerialNumberView>> ForProductAsync(
        FirmId firmId,
        ProductId productId,
        WarehouseId? warehouseId,
        bool includeGone,
        DateOnly asOn,
        CancellationToken cancellationToken = default);

    /// <summary>Finds units by the number on the case.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="number">The serial number.</param>
    /// <param name="asOn">The date warranty cover is judged on.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Every unit of any product carrying that number.</returns>
    Task<IReadOnlyList<SerialNumberView>> FindAsync(
        FirmId firmId,
        string number,
        DateOnly asOn,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles the two serial read queries.</summary>
public sealed class SerialQueryHandler
    : IQueryHandler<ListProductSerialsQuery, IReadOnlyList<SerialNumberView>>,
      IQueryHandler<FindSerialQuery, IReadOnlyList<SerialNumberView>>
{
    private readonly ISerialReader _reader;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="SerialQueryHandler"/> class.</summary>
    /// <param name="reader">The serial reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock, for judging warranty cover.</param>
    public SerialQueryHandler(ISerialReader reader, ITenantContext tenantContext, IClock clock)
    {
        _reader = reader;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SerialNumberView>>> Handle(
        ListProductSerialsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<SerialNumberView>>(NoFirm);
        }

        return Result.Success(await _reader.ForProductAsync(
            firmId,
            ProductId.From(request.ProductId),
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            request.IncludeGone,
            Today,
            cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SerialNumberView>>> Handle(
        FindSerialQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<SerialNumberView>>(NoFirm);
        }

        if (string.IsNullOrWhiteSpace(request.Number))
        {
            return Result.Failure<IReadOnlyList<SerialNumberView>>(Error.Validation(
                "Serial.NumberRequired", "A serial number is required to look one up."));
        }

        return Result.Success(await _reader.FindAsync(
            firmId, request.Number.Trim().ToUpperInvariant(), Today, cancellationToken));
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

    private static Error NoFirm => Error.Forbidden(
        "Serial.NoFirmSelected", "A firm must be selected to read serial numbers.");
}
