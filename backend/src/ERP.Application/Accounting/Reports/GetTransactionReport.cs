using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>Which transactions a transaction report reads.</summary>
public enum TransactionReportDocument
{
    /// <summary>Sales invoices, less the returns against them.</summary>
    Sales = 1,

    /// <summary>Purchase invoices, less the returns against them.</summary>
    Purchase = 2,
}

/// <summary>What a transaction report adds its figures up by.</summary>
public enum TransactionReportGrouping
{
    /// <summary>One row per calendar month.</summary>
    Month = 1,

    /// <summary>One row per day.</summary>
    Day = 2,

    /// <summary>One row per bill.</summary>
    Bill = 3,

    /// <summary>One row per product.</summary>
    Item = 4,

    /// <summary>One row per brand.</summary>
    Brand = 5,

    /// <summary>One row per category.</summary>
    Category = 6,

    /// <summary>One row per salesman, on sales.</summary>
    Salesman = 7,

    /// <summary>One row per customer or supplier.</summary>
    Party = 8,
}

/// <summary>Sales or purchases over a period, added up by month, day, bill, item and more.</summary>
/// <param name="Document">Sales or purchases.</param>
/// <param name="From">The first date.</param>
/// <param name="To">The last date.</param>
/// <param name="GroupBy">What the rows are.</param>
/// <param name="PartyLedgerId">One customer or supplier.</param>
/// <param name="SalesmanLedgerId">One salesman, on sales.</param>
/// <param name="ProductId">One product.</param>
/// <param name="Mode">One tax mode.</param>
/// <param name="WarehouseId">One godown.</param>
/// <param name="BrandId">One brand.</param>
/// <param name="CategoryId">One category.</param>
/// <param name="AreaId">Customers or suppliers in one area.</param>
/// <param name="RouteId">Customers or suppliers on one route.</param>
/// <param name="TaxPercentage">Lines taxed at one rate.</param>
/// <param name="SerialNumber">Lines that carried one serial number.</param>
/// <remarks>
/// Posted documents only, with returns taken off. On sales, each row also says what the
/// goods cost - from the stock ledger, at the batch or average cost they went out at -
/// and so what was made on them: the profit reports are this report with that column
/// read. Additional ledgers belong to a bill rather than to any one of its lines, so the
/// rows by item, brand and category, and any report narrowed to some lines of a bill,
/// carry the goods and their tax and leave the bill's additions and deductions out.
/// </remarks>
public sealed record GetTransactionReportQuery(
    TransactionReportDocument Document,
    DateOnly From,
    DateOnly To,
    TransactionReportGrouping GroupBy = TransactionReportGrouping.Month,
    Guid? PartyLedgerId = null,
    Guid? SalesmanLedgerId = null,
    Guid? ProductId = null,
    TaxMode? Mode = null,
    Guid? WarehouseId = null,
    Guid? BrandId = null,
    Guid? CategoryId = null,
    Guid? AreaId = null,
    Guid? RouteId = null,
    decimal? TaxPercentage = null,
    string? SerialNumber = null) : IQuery<TransactionReport>
{
    /// <summary>Gets a value indicating whether the report is narrowed to some lines of a bill.</summary>
    public bool NarrowsLines =>
        ProductId is not null
        || BrandId is not null
        || CategoryId is not null
        || TaxPercentage is not null
        || !string.IsNullOrWhiteSpace(SerialNumber);
}

/// <summary>One row of a transaction report.</summary>
/// <param name="Key">What the row is, as the screen sorts and drills down on it.</param>
/// <param name="Label">What the row is called.</param>
/// <param name="Detail">More about it: a bill's date and party, a product's code.</param>
/// <param name="Bills">How many bills it covers.</param>
/// <param name="Quantity">The quantity, in stock units, net of returns.</param>
/// <param name="Amount">The goods before tax.</param>
/// <param name="Tax">The tax on them.</param>
/// <param name="Others">The bills' additional ledgers: freight added, discounts taken off.</param>
/// <param name="Total">What it all comes to.</param>
/// <param name="Cost">What the goods cost, on sales.</param>
/// <param name="Profit">The goods less their cost, on sales.</param>
/// <param name="MarginPercent">The profit as a share of the goods, on sales.</param>
public sealed record TransactionReportRow(
    string Key,
    string Label,
    string? Detail,
    int Bills,
    decimal Quantity,
    decimal Amount,
    decimal Tax,
    decimal Others,
    decimal Total,
    decimal? Cost,
    decimal? Profit,
    decimal? MarginPercent);

/// <summary>A transaction report.</summary>
/// <param name="From">The first date.</param>
/// <param name="To">The last date.</param>
/// <param name="Currency">The currency the figures are in.</param>
/// <param name="GroupBy">What the rows are.</param>
/// <param name="Rows">The rows, in order.</param>
/// <param name="Totals">Every row added up.</param>
public sealed record TransactionReport(
    DateOnly From,
    DateOnly To,
    string Currency,
    TransactionReportGrouping GroupBy,
    IReadOnlyList<TransactionReportRow> Rows,
    TransactionReportRow Totals);

/// <summary>Reads transaction reports.</summary>
public interface ITransactionReportReader
{
    /// <summary>Reads one.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="query">What to read.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<TransactionReport> ReadAsync(
        FirmId firmId,
        GetTransactionReportQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="GetTransactionReportQuery"/>.</summary>
public sealed class GetTransactionReportQueryHandler
    : IQueryHandler<GetTransactionReportQuery, TransactionReport>
{
    private readonly ITransactionReportReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetTransactionReportQueryHandler"/> class.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="tenantContext">The caller's firm.</param>
    public GetTransactionReportQueryHandler(ITransactionReportReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<TransactionReport>> Handle(
        GetTransactionReportQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is not { } firmId
            ? Result.Failure<TransactionReport>(Error.Forbidden(
                "TransactionReport.NoFirmSelected", "A firm must be selected to read its transactions."))
            : Result.Success(await _reader.ReadAsync(firmId, request, cancellationToken));
    }
}

/// <summary>Validates <see cref="GetTransactionReportQuery"/>.</summary>
public sealed class GetTransactionReportQueryValidator : AbstractValidator<GetTransactionReportQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetTransactionReportQueryValidator"/> class.</summary>
    public GetTransactionReportQueryValidator()
    {
        RuleFor(q => q.Document).IsInEnum();
        RuleFor(q => q.GroupBy).IsInEnum();
        RuleFor(q => q.From).NotEqual(default(DateOnly)).WithMessage("Give the start of the period.");
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the period cannot fall before its start.");
        RuleFor(q => q.To)
            .Must((q, to) => to.DayNumber - q.From.DayNumber <= 3660)
            .WithMessage("Read at most ten years at a time.");
        RuleFor(q => q.SalesmanLedgerId)
            .Null()
            .When(q => q.Document == TransactionReportDocument.Purchase)
            .WithMessage("Purchases have no salesman.");
        RuleFor(q => q.GroupBy)
            .NotEqual(TransactionReportGrouping.Salesman)
            .When(q => q.Document == TransactionReportDocument.Purchase)
            .WithMessage("Purchases have no salesman.");
    }
}
