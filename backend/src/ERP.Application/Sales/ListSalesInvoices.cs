using ERP.Application.Abstractions;
using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Sales;

/// <summary>Lists sales documents, newest first.</summary>
/// <param name="From">The earliest document date. Omit for no lower bound.</param>
/// <param name="To">The latest. Omit for no upper bound.</param>
/// <param name="Kind">Invoices or returns. Omit for both.</param>
/// <param name="Status">One lifecycle state. Omit for all.</param>
/// <param name="CustomerLedgerId">One customer. Omit for all.</param>
/// <param name="Search">Matched against the document number and the customer's reference.</param>
/// <param name="Page">Which page, from one.</param>
/// <param name="PageSize">How many rows a page holds.</param>
/// <remarks>
/// One list for both kinds, filtered by kind, because they are one kind of document -
/// a screen showing a customer's history wants the credit notes among the invoices, not
/// in a second list beside them.
/// </remarks>
public sealed record ListSalesInvoicesQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    SalesDocumentKind? Kind = null,
    SalesInvoiceStatus? Status = null,
    Guid? CustomerLedgerId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<SalesInvoiceSummary>>;

/// <summary>A sales document as a list shows it.</summary>
/// <param name="SalesInvoiceId">The document.</param>
/// <param name="Number">Its number.</param>
/// <param name="Kind">Whether goods went out or came back.</param>
/// <param name="Date">Its date.</param>
/// <param name="CustomerLedgerId">The customer.</param>
/// <param name="CustomerCode">Their account code.</param>
/// <param name="CustomerName">Their name.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Currency">The currency it is stated in.</param>
/// <param name="ReferenceNumber">The customer's own reference.</param>
/// <param name="LineCount">How many products it carries.</param>
/// <param name="Taxable">The goods, before tax.</param>
/// <param name="Tax">The tax on them.</param>
/// <param name="Total">What it comes to.</param>
public sealed record SalesInvoiceSummary(
    Guid SalesInvoiceId,
    string Number,
    SalesDocumentKind Kind,
    DateOnly Date,
    Guid CustomerLedgerId,
    string CustomerCode,
    string CustomerName,
    SalesInvoiceStatus Status,
    string Currency,
    string? ReferenceNumber,
    int LineCount,
    decimal Taxable,
    decimal Tax,
    decimal Total);

/// <summary>Validates <see cref="ListSalesInvoicesQuery"/>.</summary>
public sealed class ListSalesInvoicesQueryValidator : AbstractValidator<ListSalesInvoicesQuery>
{
    /// <summary>The largest page this endpoint will serve.</summary>
    /// <remarks>
    /// A ceiling rather than a suggestion. Without one, a client asking for a page of a
    /// million rows would be asking the database for the whole table with extra steps,
    /// and paging would have bought nothing.
    /// </remarks>
    public const int MaximumPageSize = 200;

    /// <summary>Initialises a new instance of the <see cref="ListSalesInvoicesQueryValidator"/> class.</summary>
    public ListSalesInvoicesQueryValidator()
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

/// <summary>Handles <see cref="ListSalesInvoicesQuery"/>.</summary>
public sealed class ListSalesInvoicesQueryHandler
    : IQueryHandler<ListSalesInvoicesQuery, PagedResult<SalesInvoiceSummary>>
{
    private readonly ISalesInvoiceReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ListSalesInvoicesQueryHandler"/> class.</summary>
    /// <param name="reader">The sales invoice reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public ListSalesInvoicesQueryHandler(
        ISalesInvoiceReader reader,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<SalesInvoiceSummary>>> Handle(
        ListSalesInvoicesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<PagedResult<SalesInvoiceSummary>>(Error.Forbidden(
                "SalesInvoice.NoFirmSelected",
                "A firm must be selected to list sales documents."));
        }

        return Result.Success(await _reader.ListAsync(
            firmId,
            new SalesInvoiceFilter(
                request.From,
                request.To,
                request.Kind,
                request.Status,
                request.CustomerLedgerId is { } customer ? LedgerId.From(customer) : null,
                request.Search),
            request.Page,
            request.PageSize,
            cancellationToken));
    }
}
