using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Inventory.Stock;

/// <summary>What came in and went out of each product over a period, and where it left it.</summary>
/// <param name="From">The first date.</param>
/// <param name="To">The last date.</param>
/// <param name="WarehouseId">One godown, or every one.</param>
/// <param name="CategoryId">One category.</param>
/// <param name="BrandId">One brand.</param>
/// <param name="ProductId">One product.</param>
/// <remarks>
/// The detailed register: opening, purchases, sales, other receipts and issues, and the
/// closing quantity, read off the stock ledger. Purchases are net of purchase returns and
/// sales net of sales returns; everything else that moved - opening stock entered in the
/// period, transfers, adjustments, damage, production - is other inward or outward.
/// </remarks>
public sealed record StockRegisterQuery(
    DateOnly From,
    DateOnly To,
    Guid? WarehouseId = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    Guid? ProductId = null) : IQuery<StockRegisterReport>;

/// <summary>One product's line of the register.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductDescription">What it is called.</param>
/// <param name="CategoryName">Its category.</param>
/// <param name="StockUnitCode">The unit the quantities are in.</param>
/// <param name="Opening">What was on hand when the period opened.</param>
/// <param name="Purchase">What was bought, less what went back.</param>
/// <param name="Sales">What was sold, less what came back.</param>
/// <param name="OtherIn">Everything else that came in.</param>
/// <param name="OtherOut">Everything else that went out.</param>
/// <param name="Closing">What was on hand when it closed.</param>
public sealed record StockRegisterRow(
    Guid ProductId,
    string ProductCode,
    string ProductDescription,
    string CategoryName,
    string StockUnitCode,
    decimal Opening,
    decimal Purchase,
    decimal Sales,
    decimal OtherIn,
    decimal OtherOut,
    decimal Closing);

/// <summary>The detailed stock register.</summary>
/// <param name="From">The first date.</param>
/// <param name="To">The last date.</param>
/// <param name="Rows">One row per product that held or moved stock, by code.</param>
public sealed record StockRegisterReport(DateOnly From, DateOnly To, IReadOnlyList<StockRegisterRow> Rows);

/// <summary>Reads the stock register.</summary>
public interface IStockRegisterReader
{
    /// <summary>Reads it.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="query">The period and filters.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The register.</returns>
    Task<StockRegisterReport> ReadAsync(
        FirmId firmId,
        StockRegisterQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="StockRegisterQuery"/>.</summary>
public sealed class StockRegisterQueryHandler : IQueryHandler<StockRegisterQuery, StockRegisterReport>
{
    private readonly IStockRegisterReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="StockRegisterQueryHandler"/> class.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="tenantContext">The caller's firm.</param>
    public StockRegisterQueryHandler(IStockRegisterReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<StockRegisterReport>> Handle(
        StockRegisterQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is not { } firmId
            ? Result.Failure<StockRegisterReport>(Error.Forbidden(
                "StockRegister.NoFirmSelected", "A firm must be selected to read its stock."))
            : Result.Success(await _reader.ReadAsync(firmId, request, cancellationToken));
    }
}

/// <summary>Validates <see cref="StockRegisterQuery"/>.</summary>
public sealed class StockRegisterQueryValidator : AbstractValidator<StockRegisterQuery>
{
    /// <summary>Initialises a new instance of the <see cref="StockRegisterQueryValidator"/> class.</summary>
    public StockRegisterQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly)).WithMessage("Give the start of the period.");
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the period cannot fall before its start.");
    }
}
