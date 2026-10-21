using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Sales;

/// <summary>Reads what a customer was last quoted, and last charged, for a product.</summary>
/// <param name="CustomerLedgerId">The customer.</param>
/// <param name="ProductId">The product.</param>
/// <remarks>
/// What the sales screen offers in place of the master's rate when the counter is set to
/// sell at the customer's last quotation rate or at their own last rate.
/// </remarks>
public sealed record GetSalesRateHintsQuery(Guid CustomerLedgerId, Guid ProductId) : IQuery<SalesRateHints>;

/// <summary>One rate a customer has seen for a product, and where it came from.</summary>
/// <param name="Rate">The rate, before tax and before any line discount.</param>
/// <param name="UnitId">The unit it is a rate for.</param>
/// <param name="Number">The quotation or the invoice it was on.</param>
/// <param name="Date">That document's date.</param>
public sealed record SalesRateHint(decimal Rate, Guid UnitId, string Number, DateOnly Date);

/// <summary>The rates a customer has seen for a product.</summary>
/// <param name="LastQuotation">From their most recent quotation carrying it, if any.</param>
/// <param name="LastSale">From their most recent posted sale of it, if any.</param>
public sealed record SalesRateHints(SalesRateHint? LastQuotation, SalesRateHint? LastSale);

/// <summary>Reads the rates a customer has been quoted and charged.</summary>
public interface ISalesRateReader
{
    /// <summary>Reads a customer's last quoted and last charged rate for a product.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="customerLedgerId">The customer.</param>
    /// <param name="productId">The product.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The rates; either may be missing.</returns>
    Task<SalesRateHints> ReadAsync(
        FirmId firmId,
        LedgerId customerLedgerId,
        ProductId productId,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="GetSalesRateHintsQuery"/>.</summary>
public sealed class GetSalesRateHintsQueryHandler : IQueryHandler<GetSalesRateHintsQuery, SalesRateHints>
{
    private readonly ISalesRateReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetSalesRateHintsQueryHandler"/> class.</summary>
    /// <param name="reader">The rate reader.</param>
    /// <param name="tenantContext">The caller's tenant and firm.</param>
    public GetSalesRateHintsQueryHandler(ISalesRateReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<SalesRateHints>> Handle(
        GetSalesRateHintsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<SalesRateHints>(Error.Forbidden(
                "SalesRates.NoFirmSelected", "A firm must be selected to read rates."));
        }

        return Result.Success(await _reader.ReadAsync(
            firmId,
            LedgerId.From(request.CustomerLedgerId),
            ProductId.From(request.ProductId),
            cancellationToken));
    }
}

/// <summary>Validates <see cref="GetSalesRateHintsQuery"/>.</summary>
public sealed class GetSalesRateHintsQueryValidator : AbstractValidator<GetSalesRateHintsQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetSalesRateHintsQueryValidator"/> class.</summary>
    public GetSalesRateHintsQueryValidator()
    {
        RuleFor(q => q.CustomerLedgerId).NotEqual(Guid.Empty).WithMessage("Name the customer.");
        RuleFor(q => q.ProductId).NotEqual(Guid.Empty).WithMessage("Name the product.");
    }
}
