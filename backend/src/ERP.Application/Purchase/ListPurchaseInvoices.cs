using ERP.Application.Abstractions;
using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Purchase;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Purchase;

/// <summary>Lists purchase documents, newest first.</summary>
/// <param name="From">The earliest document date. Omit for no lower bound.</param>
/// <param name="To">The latest. Omit for no upper bound.</param>
/// <param name="Kind">Purchases or returns. Omit for both.</param>
/// <param name="Status">One lifecycle state. Omit for all.</param>
/// <param name="SupplierLedgerId">One supplier. Omit for all.</param>
/// <param name="Search">Matched against both numbers: the firm's and the supplier's.</param>
/// <param name="Page">Which page, from one.</param>
/// <param name="PageSize">How many rows a page holds.</param>
/// <remarks>
/// One list for both kinds, filtered by kind, because they are one kind of document - a
/// screen showing a supplier's history wants the debit notes among the purchases.
/// </remarks>
public sealed record ListPurchaseInvoicesQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    PurchaseDocumentKind? Kind = null,
    PurchaseInvoiceStatus? Status = null,
    Guid? SupplierLedgerId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<PurchaseInvoiceSummary>>;

/// <summary>A purchase document as a list shows it.</summary>
/// <param name="PurchaseInvoiceId">The document.</param>
/// <param name="Number">The firm's own number.</param>
/// <param name="Kind">Whether goods arrived or went back.</param>
/// <param name="Date">The date the firm booked it on.</param>
/// <param name="SupplierLedgerId">The supplier.</param>
/// <param name="SupplierCode">Their account code.</param>
/// <param name="SupplierName">Their name.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Currency">The currency it is stated in.</param>
/// <param name="SupplierInvoiceNumber">The number on the supplier's own invoice.</param>
/// <param name="SupplierInvoiceDate">The date on it.</param>
/// <param name="LineCount">How many products it carries.</param>
/// <param name="Taxable">The goods, before tax.</param>
/// <param name="Tax">The input tax on them.</param>
/// <param name="Total">What it comes to.</param>
public sealed record PurchaseInvoiceSummary(
    Guid PurchaseInvoiceId,
    string Number,
    PurchaseDocumentKind Kind,
    DateOnly Date,
    Guid SupplierLedgerId,
    string SupplierCode,
    string SupplierName,
    PurchaseInvoiceStatus Status,
    string Currency,
    string? SupplierInvoiceNumber,
    DateOnly? SupplierInvoiceDate,
    int LineCount,
    decimal Taxable,
    decimal Tax,
    decimal Total);

/// <summary>Validates <see cref="ListPurchaseInvoicesQuery"/>.</summary>
public sealed class ListPurchaseInvoicesQueryValidator
    : AbstractValidator<ListPurchaseInvoicesQuery>
{
    /// <summary>The largest page this endpoint will serve.</summary>
    public const int MaximumPageSize = 200;

    /// <summary>Initialises a new instance of the <see cref="ListPurchaseInvoicesQueryValidator"/> class.</summary>
    public ListPurchaseInvoicesQueryValidator()
    {
        RuleFor(q => q.Page)
            .GreaterThan(0)
            .WithMessage("Pages are numbered from one.");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, MaximumPageSize)
            .WithMessage($"A page holds between 1 and {MaximumPageSize} rows.");

        RuleFor(q => q.Kind!.Value).IsInEnum().When(q => q.Kind is not null);
        RuleFor(q => q.Status!.Value).IsInEnum().When(q => q.Status is not null);

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From!.Value)
            .When(q => q.From is not null && q.To is not null)
            .WithMessage("The end of the range cannot fall before its start.");
    }
}

/// <summary>Handles <see cref="ListPurchaseInvoicesQuery"/>.</summary>
public sealed class ListPurchaseInvoicesQueryHandler
    : IQueryHandler<ListPurchaseInvoicesQuery, PagedResult<PurchaseInvoiceSummary>>
{
    private readonly IPurchaseInvoiceReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ListPurchaseInvoicesQueryHandler"/> class.</summary>
    /// <param name="reader">The purchase invoice reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public ListPurchaseInvoicesQueryHandler(
        IPurchaseInvoiceReader reader,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PurchaseInvoiceSummary>>> Handle(
        ListPurchaseInvoicesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<PagedResult<PurchaseInvoiceSummary>>(Error.Forbidden(
                "PurchaseInvoice.NoFirmSelected",
                "A firm must be selected to list purchase documents."));
        }

        return Result.Success(await _reader.ListAsync(
            firmId,
            new PurchaseInvoiceFilter(
                request.From,
                request.To,
                request.Kind,
                request.Status,
                request.SupplierLedgerId is { } supplier ? LedgerId.From(supplier) : null,
                request.Search),
            request.Page,
            request.PageSize,
            cancellationToken));
    }
}
