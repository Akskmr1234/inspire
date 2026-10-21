using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Sales;

/// <summary>What was made on the goods sold in a period, product by product and batch by batch.</summary>
/// <param name="From">The first invoice date.</param>
/// <param name="To">The last.</param>
/// <param name="ProductId">One product, or every one.</param>
/// <remarks>
/// The cost of each sale is what the stock ledger took the goods out at - the batch's own
/// cost where the product is batched, the average otherwise - so a product bought in one
/// batch at 10 and another at 15, and sold out of both at 20, shows 10 on the first and 5
/// on the second rather than 7.50 on each.
/// </remarks>
public sealed record SalesProfitQuery(DateOnly From, DateOnly To, Guid? ProductId = null)
    : IQuery<SalesProfitReport>;

/// <summary>What one product, or one batch of it, made in the period.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductName">What it is called.</param>
/// <param name="BatchId">The batch, where the product is batched.</param>
/// <param name="BatchNumber">Its number.</param>
/// <param name="Quantity">How much went out, net of what came back, in the stock unit.</param>
/// <param name="Sales">What it sold for before tax, net of returns.</param>
/// <param name="Cost">What it cost.</param>
/// <param name="Profit">The difference.</param>
/// <param name="MarginPercent">The profit as a share of the sales, where there were any.</param>
/// <param name="PurchaseRate">What the batch was bought at.</param>
/// <param name="SellingRate">What the batch is priced at, where it is.</param>
public sealed record SalesProfitRow(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid? BatchId,
    string? BatchNumber,
    decimal Quantity,
    decimal Sales,
    decimal Cost,
    decimal Profit,
    decimal? MarginPercent,
    decimal? PurchaseRate,
    decimal? SellingRate);

/// <summary>The period's profit on goods sold.</summary>
/// <param name="From">The first invoice date.</param>
/// <param name="To">The last.</param>
/// <param name="Currency">The currency it is stated in.</param>
/// <param name="Rows">Product by product, batch by batch.</param>
/// <param name="TotalSales">What everything sold for.</param>
/// <param name="TotalCost">What it cost.</param>
/// <param name="TotalProfit">The difference.</param>
public sealed record SalesProfitReport(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<SalesProfitRow> Rows,
    decimal TotalSales,
    decimal TotalCost,
    decimal TotalProfit);

/// <summary>Reads what goods sold for and what they cost.</summary>
public interface ISalesProfitReader
{
    /// <summary>Reads the period's profit on goods sold.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="query">The period, and the product if one.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<SalesProfitReport> ReadAsync(
        FirmId firmId,
        SalesProfitQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="SalesProfitQuery"/>.</summary>
public sealed class SalesProfitQueryHandler : IQueryHandler<SalesProfitQuery, SalesProfitReport>
{
    private readonly ISalesProfitReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="SalesProfitQueryHandler"/> class.</summary>
    /// <param name="reader">The profit reader.</param>
    /// <param name="tenantContext">The caller's tenant and firm.</param>
    public SalesProfitQueryHandler(ISalesProfitReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<SalesProfitReport>> Handle(
        SalesProfitQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is not { } firmId
            ? Result.Failure<SalesProfitReport>(Error.Forbidden(
                "SalesProfit.NoFirmSelected", "A firm must be selected to read its profit."))
            : Result.Success(await _reader.ReadAsync(firmId, request, cancellationToken));
    }
}

/// <summary>Validates <see cref="SalesProfitQuery"/>.</summary>
public sealed class SalesProfitQueryValidator : AbstractValidator<SalesProfitQuery>
{
    /// <summary>Initialises a new instance of the <see cref="SalesProfitQueryValidator"/> class.</summary>
    public SalesProfitQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly)).WithMessage("Give the start of the period.");
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the period cannot fall before its start.");
    }
}
