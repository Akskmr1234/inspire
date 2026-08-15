using ERP.Application.Abstractions;
using ERP.Application.Abstractions.Messaging;
using ERP.Domain.Accounting;
using ERP.Domain.Purchase;
using FluentValidation;

namespace ERP.Application.Purchase;

/// <summary>One product on a purchase order being entered.</summary>
/// <param name="ProductId">The product ordered.</param>
/// <param name="Quantity">How many, in the unit named.</param>
/// <param name="Rate">What one of them was ordered at.</param>
/// <param name="TaxPercentage">The rate tax is expected at, supplied per line.</param>
/// <param name="UnitId">The unit the quantity is in. Defaults to the product's stock unit.</param>
/// <param name="Discount">What comes off the line before tax.</param>
/// <remarks>
/// No batch and no serial numbers, and for a firmer reason than on a sales order line: the
/// batch a supplier will ship does not exist anywhere yet. Both are keyed off the carton
/// when the purchase that receives the goods is entered.
/// </remarks>
public sealed record PurchaseOrderLineInput(
    Guid ProductId,
    decimal Quantity,
    decimal Rate,
    decimal TaxPercentage = 0m,
    Guid? UnitId = null,
    decimal Discount = 0m);

/// <summary>Enters a purchase order as a draft.</summary>
/// <param name="Date">The date it was raised.</param>
/// <param name="SupplierLedgerId">The supplier it is placed with.</param>
/// <param name="WarehouseId">The warehouse the goods are expected at.</param>
/// <param name="Lines">What was ordered.</param>
/// <param name="Charges">Carriage, packing, a settlement discount - whatever was agreed beside it.</param>
/// <param name="Mode">The tax mode. Defaults from the firm's regime.</param>
/// <param name="ExpectedOn">When the supplier promised the goods.</param>
/// <param name="ReferenceNumber">The supplier's own reference: their quotation number.</param>
/// <param name="Narration">What is recorded against the order.</param>
public sealed record CreatePurchaseOrderCommand(
    DateOnly Date,
    Guid SupplierLedgerId,
    Guid WarehouseId,
    IReadOnlyList<PurchaseOrderLineInput> Lines,
    IReadOnlyList<PurchaseInvoiceChargeInput>? Charges = null,
    TaxMode? Mode = null,
    DateOnly? ExpectedOn = null,
    string? ReferenceNumber = null,
    string? Narration = null) : ICommand<PurchaseOrderResponse>, ITransactional;

/// <summary>Confirms a draft order, so purchases may be raised from it.</summary>
/// <param name="PurchaseOrderId">The order.</param>
public sealed record ConfirmPurchaseOrderCommand(Guid PurchaseOrderId)
    : ICommand<PurchaseOrderResponse>, ITransactional;

/// <summary>Closes an order short, or cancels one nothing has arrived against.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="Reason">Why. Required, and kept on the order.</param>
public sealed record ClosePurchaseOrderCommand(Guid PurchaseOrderId, string Reason)
    : ICommand, ITransactional;

/// <summary>How much of one order line is arriving on this purchase.</summary>
/// <param name="PurchaseOrderLineId">The line.</param>
/// <param name="Quantity">
/// How much, in the unit the order was entered in. Omit to invoice whatever is left.
/// </param>
/// <param name="BatchNumber">The batch it arrives in, read off the carton.</param>
/// <param name="ExpiresOn">When that batch expires, where the supplier stated it.</param>
/// <param name="SerialNumbers">The units arriving, where the product is serialised.</param>
/// <remarks>
/// The batch is typed here rather than chosen, which is where this stops mirroring a sales
/// conversion. A sale picks from batches a warehouse holds; a purchase is usually the moment
/// a batch comes into existence, so the number and its expiry are keyed from the supplier's
/// carton and the receipt opens the batch.
/// </remarks>
public sealed record PurchaseOrderConversionLine(
    Guid PurchaseOrderLineId,
    decimal? Quantity = null,
    string? BatchNumber = null,
    DateOnly? ExpiresOn = null,
    IReadOnlyList<string>? SerialNumbers = null);

/// <summary>Raises a purchase from a confirmed order.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="Date">The purchase date. Defaults to today.</param>
/// <param name="Lines">
/// Which lines have arrived and how much of each. Omit for everything still outstanding.
/// </param>
/// <param name="WarehouseId">
/// Where the goods actually arrived, if not the warehouse the order expected. Nothing was
/// held there, so this is a free choice rather than an override.
/// </param>
/// <param name="SupplierInvoiceNumber">The number printed on the supplier's own invoice.</param>
/// <param name="SupplierInvoiceDate">The date printed on it.</param>
/// <remarks>
/// The purchase side of §12.2's <em>Create Invoice From</em>. It produces a <b>draft</b>
/// purchase: posting receives the goods, raises the bill and writes the books, and that
/// stays its own step - so a conversion can be checked against the supplier's document
/// before anything moves.
/// <para>
/// The supplier's own invoice number may be given here, unlike anything on the sales
/// conversion, because a purchase that will be posted needs one before its input tax is
/// reclaimable. Where the goods have arrived but the invoice has not, it is left off and
/// filled in later.
/// </para>
/// </remarks>
public sealed record ConvertPurchaseOrderCommand(
    Guid PurchaseOrderId,
    DateOnly? Date = null,
    IReadOnlyList<PurchaseOrderConversionLine>? Lines = null,
    Guid? WarehouseId = null,
    string? SupplierInvoiceNumber = null,
    DateOnly? SupplierInvoiceDate = null) : ICommand<PurchaseInvoiceResponse>, ITransactional;

/// <summary>Lists purchase orders, newest first.</summary>
/// <param name="From">The earliest order date. Omit for no lower bound.</param>
/// <param name="To">The latest. Omit for no upper bound.</param>
/// <param name="Status">One lifecycle state. Omit for all.</param>
/// <param name="SupplierLedgerId">One supplier. Omit for all.</param>
/// <param name="Search">Matched against the order number and the supplier's reference.</param>
/// <param name="OutstandingOnly">
/// Only orders with goods still owed. What a buyer asks for every morning.
/// </param>
/// <param name="Page">Which page, from one.</param>
/// <param name="PageSize">How many rows a page holds.</param>
public sealed record ListPurchaseOrdersQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    PurchaseOrderStatus? Status = null,
    Guid? SupplierLedgerId = null,
    string? Search = null,
    bool OutstandingOnly = false,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<PurchaseOrderSummary>>;

/// <summary>Reads one order, with its lines and what is still owed on each.</summary>
/// <param name="PurchaseOrderId">The order.</param>
public sealed record GetPurchaseOrderQuery(Guid PurchaseOrderId) : IQuery<PurchaseOrderDetail>;

/// <summary>An order's header figures.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="Number">The number its series issued.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Taxable">The goods, net of line discounts and before tax.</param>
/// <param name="Tax">The tax expected on them.</param>
/// <param name="ChargeTotal">What the charges add, net of what they deduct.</param>
/// <param name="RoundingDifference">What rounding the total to the currency moved it by.</param>
/// <param name="Total">What the firm expects to be billed.</param>
public sealed record PurchaseOrderResponse(
    Guid PurchaseOrderId,
    string Number,
    PurchaseOrderStatus Status,
    decimal Taxable,
    decimal Tax,
    decimal ChargeTotal,
    decimal RoundingDifference,
    decimal Total);

/// <summary>An order as a list shows it.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="Number">Its number.</param>
/// <param name="Date">The date it was raised.</param>
/// <param name="ExpectedOn">When the supplier promised the goods.</param>
/// <param name="SupplierLedgerId">The supplier.</param>
/// <param name="SupplierCode">Their account code.</param>
/// <param name="SupplierName">Their name.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Currency">The currency it is stated in.</param>
/// <param name="ReferenceNumber">The supplier's own reference.</param>
/// <param name="LineCount">How many products it carries.</param>
/// <param name="OutstandingLines">How many of those still owe something.</param>
/// <param name="Taxable">The goods, before tax.</param>
/// <param name="Tax">The tax expected on them.</param>
/// <param name="Total">What it comes to.</param>
public sealed record PurchaseOrderSummary(
    Guid PurchaseOrderId,
    string Number,
    DateOnly Date,
    DateOnly? ExpectedOn,
    Guid SupplierLedgerId,
    string SupplierCode,
    string SupplierName,
    PurchaseOrderStatus Status,
    string Currency,
    string? ReferenceNumber,
    int LineCount,
    int OutstandingLines,
    decimal Taxable,
    decimal Tax,
    decimal Total);

/// <summary>One line of an order, and how much of it has arrived.</summary>
/// <param name="PurchaseOrderLineId">The line, which a conversion names.</param>
/// <param name="LineNumber">Its position, from one.</param>
/// <param name="ProductId">The product ordered.</param>
/// <param name="UnitId">The unit the quantity was entered in.</param>
/// <param name="Quantity">How much was ordered.</param>
/// <param name="InvoicedQuantity">How much has been invoiced.</param>
/// <param name="OutstandingQuantity">How much is still owed.</param>
/// <param name="Rate">What one was ordered at.</param>
/// <param name="Discount">What came off before tax.</param>
/// <param name="Taxable">What the line comes to before tax.</param>
/// <param name="Tax">The tax expected on it.</param>
/// <param name="Components">That tax, head by head.</param>
public sealed record PurchaseOrderLineDetail(
    Guid PurchaseOrderLineId,
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
    IReadOnlyList<PurchaseInvoiceLineTaxDetail> Components);

/// <summary>An order in full.</summary>
/// <param name="Header">Its number, status and figures.</param>
/// <param name="Date">The date it was raised.</param>
/// <param name="ExpectedOn">When the supplier promised the goods.</param>
/// <param name="SupplierLedgerId">The supplier.</param>
/// <param name="WarehouseId">The warehouse the goods are expected at.</param>
/// <param name="Mode">The tax mode it was placed under.</param>
/// <param name="Currency">The currency it is stated in.</param>
/// <param name="ReferenceNumber">The supplier's own reference.</param>
/// <param name="Narration">What is recorded against it.</param>
/// <param name="ClosureReason">Why it was closed, where it was.</param>
/// <param name="Lines">What was ordered, and what is left.</param>
/// <param name="Charges">What was agreed beside it.</param>
public sealed record PurchaseOrderDetail(
    PurchaseOrderResponse Header,
    DateOnly Date,
    DateOnly? ExpectedOn,
    Guid SupplierLedgerId,
    Guid WarehouseId,
    TaxMode Mode,
    string Currency,
    string? ReferenceNumber,
    string? Narration,
    string? ClosureReason,
    IReadOnlyList<PurchaseOrderLineDetail> Lines,
    IReadOnlyList<PurchaseInvoiceChargeDetail> Charges);

/// <summary>Validates <see cref="CreatePurchaseOrderCommand"/>.</summary>
public sealed class CreatePurchaseOrderCommandValidator
    : AbstractValidator<CreatePurchaseOrderCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreatePurchaseOrderCommandValidator"/> class.</summary>
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(c => c.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("An order date is required.");

        RuleFor(c => c.SupplierLedgerId)
            .NotEqual(Guid.Empty)
            .WithMessage("An order must name the supplier it is placed with.");

        RuleFor(c => c.WarehouseId)
            .NotEqual(Guid.Empty)
            .WithMessage("An order must name the warehouse the goods are expected at.");

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

/// <summary>Validates <see cref="ClosePurchaseOrderCommand"/>.</summary>
public sealed class ClosePurchaseOrderCommandValidator
    : AbstractValidator<ClosePurchaseOrderCommand>
{
    /// <summary>Initialises a new instance of the <see cref="ClosePurchaseOrderCommandValidator"/> class.</summary>
    public ClosePurchaseOrderCommandValidator()
    {
        RuleFor(c => c.PurchaseOrderId).NotEqual(Guid.Empty);

        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("A reason is required when closing an order.")
            .MaximumLength(500);
    }
}

/// <summary>Validates <see cref="ListPurchaseOrdersQuery"/>.</summary>
public sealed class ListPurchaseOrdersQueryValidator : AbstractValidator<ListPurchaseOrdersQuery>
{
    /// <summary>The largest page this endpoint will serve.</summary>
    public const int MaximumPageSize = 200;

    /// <summary>Initialises a new instance of the <see cref="ListPurchaseOrdersQueryValidator"/> class.</summary>
    public ListPurchaseOrdersQueryValidator()
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
