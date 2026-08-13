using ERP.Application.Abstractions;
using ERP.Application.Abstractions.Messaging;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;
using FluentValidation;

namespace ERP.Application.Sales;

/// <summary>One product on an order being entered.</summary>
/// <param name="ProductId">The product ordered.</param>
/// <param name="Quantity">How many, in the unit named.</param>
/// <param name="Rate">What one of them is quoted at.</param>
/// <param name="TaxPercentage">The rate tax is quoted at, supplied per line.</param>
/// <param name="UnitId">The unit the quantity is in. Defaults to the product's stock unit.</param>
/// <param name="Discount">What comes off the line before tax.</param>
/// <remarks>
/// No batch and no serial numbers, unlike an invoice line. An order is for goods nobody
/// has picked yet, and the invoice chooses both when they actually leave.
/// </remarks>
public sealed record SalesOrderLineInput(
    Guid ProductId,
    decimal Quantity,
    decimal Rate,
    decimal TaxPercentage = 0m,
    Guid? UnitId = null,
    decimal Discount = 0m);

/// <summary>Enters a sales order as a draft.</summary>
/// <param name="Date">The date it was taken.</param>
/// <param name="CustomerLedgerId">The customer placing it.</param>
/// <param name="WarehouseId">The warehouse it is expected to ship from.</param>
/// <param name="Lines">What was ordered.</param>
/// <param name="Charges">Freight, packing, a discount - whatever was quoted beside it.</param>
/// <param name="Mode">The tax mode. Defaults from the firm's regime.</param>
/// <param name="ExpectedOn">When the customer was told to expect the goods.</param>
/// <param name="ReferenceNumber">The customer's own reference.</param>
/// <param name="Narration">What is recorded against the order.</param>
public sealed record CreateSalesOrderCommand(
    DateOnly Date,
    Guid CustomerLedgerId,
    Guid WarehouseId,
    IReadOnlyList<SalesOrderLineInput> Lines,
    IReadOnlyList<SalesInvoiceChargeInput>? Charges = null,
    TaxMode? Mode = null,
    DateOnly? ExpectedOn = null,
    string? ReferenceNumber = null,
    string? Narration = null) : ICommand<SalesOrderResponse>, ITransactional;

/// <summary>Confirms a draft order, so invoices may be raised from it.</summary>
/// <param name="SalesOrderId">The order.</param>
public sealed record ConfirmSalesOrderCommand(Guid SalesOrderId)
    : ICommand<SalesOrderResponse>, ITransactional;

/// <summary>Closes an order short, or cancels one nothing has gone out against.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="Reason">Why. Required, and kept on the order.</param>
public sealed record CloseSalesOrderCommand(Guid SalesOrderId, string Reason)
    : ICommand, ITransactional;

/// <summary>How much of one order line is going out on this invoice.</summary>
/// <param name="SalesOrderLineId">The line.</param>
/// <param name="Quantity">
/// How much, in the unit the order was entered in. Omit to invoice whatever is left.
/// </param>
/// <param name="BatchNumber">The batch it ships from, where the product is batched.</param>
/// <param name="SerialNumbers">The units shipped, where the product is serialised.</param>
public sealed record SalesOrderConversionLine(
    Guid SalesOrderLineId,
    decimal? Quantity = null,
    string? BatchNumber = null,
    IReadOnlyList<string>? SerialNumbers = null);

/// <summary>Raises an invoice from a confirmed order.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="Date">The invoice date. Defaults to today.</param>
/// <param name="Lines">
/// Which lines are going out and how much of each. Omit for everything still outstanding,
/// which is the whole order on the common path.
/// </param>
/// <param name="WarehouseId">
/// Where the goods actually ship from, if not the warehouse the order expected. Nothing
/// was held there, so this is a free choice rather than an override.
/// </param>
/// <remarks>
/// §12.2's <em>Create Invoice From</em>. It produces a <b>draft</b> invoice: posting moves
/// stock, raises a debt and writes the books, and that stays its own step here as it is
/// everywhere else - so a conversion can be corrected before anything moves.
/// </remarks>
public sealed record ConvertSalesOrderCommand(
    Guid SalesOrderId,
    DateOnly? Date = null,
    IReadOnlyList<SalesOrderConversionLine>? Lines = null,
    Guid? WarehouseId = null) : ICommand<SalesInvoiceResponse>, ITransactional;

/// <summary>Lists sales orders, newest first.</summary>
/// <param name="From">The earliest order date. Omit for no lower bound.</param>
/// <param name="To">The latest. Omit for no upper bound.</param>
/// <param name="Status">One lifecycle state. Omit for all.</param>
/// <param name="CustomerLedgerId">One customer. Omit for all.</param>
/// <param name="Search">Matched against the order number and the customer's reference.</param>
/// <param name="OutstandingOnly">
/// Only orders with goods still owed. What a warehouse asks for every morning.
/// </param>
/// <param name="Page">Which page, from one.</param>
/// <param name="PageSize">How many rows a page holds.</param>
public sealed record ListSalesOrdersQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    SalesOrderStatus? Status = null,
    Guid? CustomerLedgerId = null,
    string? Search = null,
    bool OutstandingOnly = false,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<SalesOrderSummary>>;

/// <summary>Reads one order, with its lines and what is still owed on each.</summary>
/// <param name="SalesOrderId">The order.</param>
public sealed record GetSalesOrderQuery(Guid SalesOrderId) : IQuery<SalesOrderDetail>;

/// <summary>An order's header figures.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="Number">The number its series issued.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Taxable">The goods, net of line discounts and before tax.</param>
/// <param name="Tax">The tax quoted on them.</param>
/// <param name="ChargeTotal">What the charges add, net of what they deduct.</param>
/// <param name="RoundingDifference">What rounding the total to the currency moved it by.</param>
/// <param name="Total">What the customer was quoted.</param>
public sealed record SalesOrderResponse(
    Guid SalesOrderId,
    string Number,
    SalesOrderStatus Status,
    decimal Taxable,
    decimal Tax,
    decimal ChargeTotal,
    decimal RoundingDifference,
    decimal Total);

/// <summary>An order as a list shows it.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="Number">Its number.</param>
/// <param name="Date">The date it was taken.</param>
/// <param name="ExpectedOn">When the goods were promised.</param>
/// <param name="CustomerLedgerId">The customer.</param>
/// <param name="CustomerCode">Their account code.</param>
/// <param name="CustomerName">Their name.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Currency">The currency it is stated in.</param>
/// <param name="ReferenceNumber">The customer's own reference.</param>
/// <param name="LineCount">How many products it carries.</param>
/// <param name="OutstandingLines">How many of those still owe something.</param>
/// <param name="Taxable">The goods, before tax.</param>
/// <param name="Tax">The tax quoted on them.</param>
/// <param name="Total">What it comes to.</param>
public sealed record SalesOrderSummary(
    Guid SalesOrderId,
    string Number,
    DateOnly Date,
    DateOnly? ExpectedOn,
    Guid CustomerLedgerId,
    string CustomerCode,
    string CustomerName,
    SalesOrderStatus Status,
    string Currency,
    string? ReferenceNumber,
    int LineCount,
    int OutstandingLines,
    decimal Taxable,
    decimal Tax,
    decimal Total);

/// <summary>One line of an order, and how much of it has gone out.</summary>
/// <param name="SalesOrderLineId">The line, which a conversion names.</param>
/// <param name="LineNumber">Its position, from one.</param>
/// <param name="ProductId">The product ordered.</param>
/// <param name="UnitId">The unit the quantity was entered in.</param>
/// <param name="Quantity">How much was ordered.</param>
/// <param name="InvoicedQuantity">How much has been invoiced.</param>
/// <param name="OutstandingQuantity">How much is still owed.</param>
/// <param name="Rate">What one was quoted at.</param>
/// <param name="Discount">What came off before tax.</param>
/// <param name="Taxable">What the line comes to before tax.</param>
/// <param name="Tax">The tax quoted on it.</param>
/// <param name="Components">That tax, head by head.</param>
public sealed record SalesOrderLineDetail(
    Guid SalesOrderLineId,
    int LineNumber,
    Guid ProductId,
    Guid UnitId,
    decimal Quantity,
    decimal InvoicedQuantity,
    decimal OutstandingQuantity,
    decimal Rate,
    decimal Discount,
    decimal Taxable,
    decimal Tax,
    IReadOnlyList<SalesInvoiceLineTaxDetail> Components);

/// <summary>An order in full.</summary>
/// <param name="Header">Its number, status and figures.</param>
/// <param name="Date">The date it was taken.</param>
/// <param name="ExpectedOn">When the goods were promised.</param>
/// <param name="CustomerLedgerId">The customer.</param>
/// <param name="WarehouseId">The warehouse it is expected to ship from.</param>
/// <param name="Mode">The tax mode it was quoted under.</param>
/// <param name="Currency">The currency it is stated in.</param>
/// <param name="ReferenceNumber">The customer's own reference.</param>
/// <param name="Narration">What is recorded against it.</param>
/// <param name="ClosureReason">Why it was closed, where it was.</param>
/// <param name="Lines">What was ordered, and what is left.</param>
/// <param name="Charges">What was quoted beside it.</param>
public sealed record SalesOrderDetail(
    SalesOrderResponse Header,
    DateOnly Date,
    DateOnly? ExpectedOn,
    Guid CustomerLedgerId,
    Guid WarehouseId,
    TaxMode Mode,
    string Currency,
    string? ReferenceNumber,
    string? Narration,
    string? ClosureReason,
    IReadOnlyList<SalesOrderLineDetail> Lines,
    IReadOnlyList<SalesInvoiceChargeDetail> Charges);

/// <summary>Validates <see cref="CreateSalesOrderCommand"/>.</summary>
public sealed class CreateSalesOrderCommandValidator : AbstractValidator<CreateSalesOrderCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateSalesOrderCommandValidator"/> class.</summary>
    public CreateSalesOrderCommandValidator()
    {
        RuleFor(c => c.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("An order date is required.");

        RuleFor(c => c.CustomerLedgerId)
            .NotEqual(Guid.Empty)
            .WithMessage("An order must name the customer that placed it.");

        RuleFor(c => c.WarehouseId)
            .NotEqual(Guid.Empty)
            .WithMessage("An order must name the warehouse it is expected to ship from.");

        RuleFor(c => c.Mode!.Value).IsInEnum().When(c => c.Mode is not null);

        RuleFor(c => c.Lines)
            .NotEmpty()
            .WithMessage("An order needs at least one line.");

        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId)
                .NotEqual(Guid.Empty)
                .WithMessage("Each line must name a product.");

            line.RuleFor(l => l.Quantity)
                .GreaterThan(0m)
                .WithMessage("An order line must be for a positive quantity.");

            line.RuleFor(l => l.Rate)
                .GreaterThanOrEqualTo(0m)
                .WithMessage("A rate cannot be negative.");

            line.RuleFor(l => l.Discount)
                .GreaterThanOrEqualTo(0m)
                .WithMessage("A discount cannot be negative.");

            line.RuleFor(l => l.TaxPercentage)
                .InclusiveBetween(0m, 100m)
                .WithMessage("A tax rate runs from 0 to 100 per cent.");
        });
    }
}

/// <summary>Validates <see cref="CloseSalesOrderCommand"/>.</summary>
public sealed class CloseSalesOrderCommandValidator : AbstractValidator<CloseSalesOrderCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CloseSalesOrderCommandValidator"/> class.</summary>
    public CloseSalesOrderCommandValidator()
    {
        RuleFor(c => c.SalesOrderId).NotEqual(Guid.Empty);

        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("A reason is required when closing an order.")
            .MaximumLength(500);
    }
}

/// <summary>Validates <see cref="ListSalesOrdersQuery"/>.</summary>
public sealed class ListSalesOrdersQueryValidator : AbstractValidator<ListSalesOrdersQuery>
{
    /// <summary>The largest page this endpoint will serve.</summary>
    public const int MaximumPageSize = 200;

    /// <summary>Initialises a new instance of the <see cref="ListSalesOrdersQueryValidator"/> class.</summary>
    public ListSalesOrdersQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0).WithMessage("Pages are numbered from one.");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, MaximumPageSize)
            .WithMessage($"A page holds between 1 and {MaximumPageSize} rows.");

        RuleFor(q => q.Status!.Value).IsInEnum().When(q => q.Status is not null);

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From!.Value)
            .When(q => q.From is not null && q.To is not null)
            .WithMessage("The end of the range cannot fall before its start.");
    }
}
