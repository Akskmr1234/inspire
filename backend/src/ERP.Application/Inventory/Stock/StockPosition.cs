using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Inventory.Stock;

/// <summary>What is on hand as at a date, at its finest grain: product, godown and batch.</summary>
/// <param name="AsOn">The date; today when omitted.</param>
/// <param name="BranchId">One branch's godowns.</param>
/// <param name="WarehouseId">One godown.</param>
/// <param name="CategoryId">One category.</param>
/// <param name="BrandId">One brand.</param>
/// <param name="ProductId">One product.</param>
/// <param name="ExpiresFrom">Batches expiring on or after this date.</param>
/// <param name="ExpiresTo">Batches expiring on or before this date.</param>
/// <param name="ByReceipt">
/// Split each position into the receipts it is still made of - the stock aging report's
/// view - rather than one row per position, the register's.
/// </param>
/// <remarks>
/// <para>
/// The stock register and the stock aging report both read this. Rows come at the finest
/// grain and the screen adds them up to whichever columns are showing: hide Batch and a
/// product's batches in a godown become one row, hide Godown and Branch as well and the
/// product is one row across the firm. Because every row is a disjoint part of the
/// whole, any such total is exact - nothing is counted twice and nothing is left out.
/// </para>
/// <para>
/// Read off the stock ledger, so a past date reads as it stood then, and valued at the
/// product's average cost in its godown - the same cost the stock valuation and the
/// books carry, so the register's total is theirs.
/// </para>
/// <para>
/// By receipt, a position is matched to its newest receipts first: what is left on the
/// shelf is what came in last. Each part carries the date it came in - the purchase,
/// opening entry, transfer in or return that brought it - and its age in days.
/// Stock below zero has no receipts to age and is left out of that view.
/// </para>
/// </remarks>
public sealed record StockPositionQuery(
    DateOnly? AsOn = null,
    Guid? BranchId = null,
    Guid? WarehouseId = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    Guid? ProductId = null,
    DateOnly? ExpiresFrom = null,
    DateOnly? ExpiresTo = null,
    bool ByReceipt = false) : IQuery<StockPositionReport>;

/// <summary>One part of what is on hand.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductName">What it is called.</param>
/// <param name="BatchNumber">The batch; empty for a product not kept in batches.</param>
/// <param name="ExpiresOn">When the batch expires, where it does.</param>
/// <param name="CategoryName">The product's category.</param>
/// <param name="BrandName">Its brand; empty where it has none.</param>
/// <param name="WarehouseId">The godown.</param>
/// <param name="WarehouseName">Its name.</param>
/// <param name="BranchId">The branch the godown belongs to, where it belongs to one.</param>
/// <param name="BranchName">Its name; empty where there is none.</param>
/// <param name="StockUnitCode">The unit the quantity is in.</param>
/// <param name="Quantity">How much.</param>
/// <param name="Rate">What one unit is valued at.</param>
/// <param name="Value">What it is worth, at that rate.</param>
/// <param name="ReceivedOn">By receipt: the date this part came in.</param>
/// <param name="AgeDays">By receipt: how many days it has been held.</param>
public sealed record StockPositionRow(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string BatchNumber,
    DateOnly? ExpiresOn,
    string CategoryName,
    string BrandName,
    Guid WarehouseId,
    string WarehouseName,
    Guid? BranchId,
    string BranchName,
    string StockUnitCode,
    decimal Quantity,
    decimal Rate,
    decimal Value,
    DateOnly? ReceivedOn,
    int? AgeDays);

/// <summary>What is on hand.</summary>
/// <param name="AsOn">The date it is as at.</param>
/// <param name="Currency">The currency values are in.</param>
/// <param name="Rows">Every part, by product code.</param>
/// <param name="TotalQuantity">The rows' quantities, added up.</param>
/// <param name="TotalValue">Their values, added up.</param>
public sealed record StockPositionReport(
    DateOnly AsOn,
    string Currency,
    IReadOnlyList<StockPositionRow> Rows,
    decimal TotalQuantity,
    decimal TotalValue);

/// <summary>Reads what is on hand.</summary>
public interface IStockPositionReader
{
    /// <summary>Reads it.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="asOn">The date it is read as at.</param>
    /// <param name="query">The filters, and whether to split by receipt.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The positions.</returns>
    Task<StockPositionReport> ReadAsync(
        FirmId firmId,
        DateOnly asOn,
        StockPositionQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="StockPositionQuery"/>.</summary>
public sealed class StockPositionQueryHandler : IQueryHandler<StockPositionQuery, StockPositionReport>
{
    private readonly IStockPositionReader _reader;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="StockPositionQueryHandler"/> class.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="tenantContext">The caller's firm.</param>
    /// <param name="clock">The clock, for today.</param>
    public StockPositionQueryHandler(
        IStockPositionReader reader,
        ITenantContext tenantContext,
        IClock clock)
    {
        _reader = reader;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<StockPositionReport>> Handle(
        StockPositionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<StockPositionReport>(Error.Forbidden(
                "StockPosition.NoFirmSelected", "A firm must be selected to read its stock."));
        }

        DateOnly asOn = request.AsOn ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        return Result.Success(await _reader.ReadAsync(firmId, asOn, request, cancellationToken));
    }
}

/// <summary>Validates <see cref="StockPositionQuery"/>.</summary>
public sealed class StockPositionQueryValidator : AbstractValidator<StockPositionQuery>
{
    /// <summary>Initialises a new instance of the <see cref="StockPositionQueryValidator"/> class.</summary>
    public StockPositionQueryValidator()
    {
        RuleFor(q => q.ExpiresTo)
            .GreaterThanOrEqualTo(q => q.ExpiresFrom)
            .When(q => q.ExpiresFrom is not null && q.ExpiresTo is not null)
            .WithMessage("The expiry range cannot end before it starts.");
    }
}
