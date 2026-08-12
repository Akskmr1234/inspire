using ERP.Application.Abstractions;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads pages of sales documents for a list.</summary>
/// <remarks>
/// <para>
/// The page is taken in the database and the totals are worked out afterwards, from the
/// documents themselves. Summing lines and charges in SQL would mean writing the rounding
/// rule a second time - to the currency's own scale, once, at the end - and the day the
/// two spellings disagreed, a list would contradict the invoice it lists.
/// </para>
/// <para>
/// That costs one extra fetch of the page's lines, which is a page's worth: fifty
/// documents of a few lines each. It is the whole reason for paging first and loading
/// second rather than loading and paging in memory.
/// </para>
/// </remarks>
public sealed class SalesInvoiceReader : ISalesInvoiceReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="SalesInvoiceReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public SalesInvoiceReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<PagedResult<SalesInvoiceSummary>> ListAsync(
        FirmId firmId,
        SalesInvoiceFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<SalesInvoice> matching = Narrow(
            _context.SalesInvoices.Where(invoice => invoice.FirmId == firmId), filter);

        int total = await matching.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult.Empty<SalesInvoiceSummary>(page, pageSize);
        }

        // Newest first, and the number breaks a tie: several documents share a date on
        // any busy day, and a list whose order changed between one page and the next
        // would show the same row twice and skip another.
        List<SalesInvoiceId> ids = await matching
            .OrderByDescending(invoice => invoice.Date)
            .ThenByDescending(invoice => invoice.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(invoice => invoice.Id)
            .ToListAsync(cancellationToken);

        List<SalesInvoice> documents = await _context.SalesInvoices
            .Include(invoice => invoice.Lines)
            .Include(invoice => invoice.Charges)
            .Where(invoice => ids.Contains(invoice.Id))
            .ToListAsync(cancellationToken);

        Dictionary<LedgerId, Ledger> customers = await _context.Ledgers
            .Where(ledger => documents.Select(d => d.CustomerLedgerId).Contains(ledger.Id))
            .ToDictionaryAsync(ledger => ledger.Id, cancellationToken);

        // Ordered by the page's own order rather than by whatever the second query
        // returned, which is unordered by definition.
        List<SalesInvoiceSummary> rows =
        [
            .. ids
                .Select(id => documents.Find(document => document.Id == id))
                .Where(document => document is not null)
                .Select(document => Describe(document!, customers)),
        ];

        return new PagedResult<SalesInvoiceSummary>(rows, page, pageSize, total);
    }

    private static IQueryable<SalesInvoice> Narrow(
        IQueryable<SalesInvoice> invoices,
        SalesInvoiceFilter filter)
    {
        if (filter.From is { } from)
        {
            invoices = invoices.Where(invoice => invoice.Date >= from);
        }

        if (filter.To is { } to)
        {
            invoices = invoices.Where(invoice => invoice.Date <= to);
        }

        if (filter.Kind is { } kind)
        {
            invoices = invoices.Where(invoice => invoice.Kind == kind);
        }

        if (filter.Status is { } status)
        {
            invoices = invoices.Where(invoice => invoice.Status == status);
        }

        if (filter.CustomerLedgerId is { } customer)
        {
            invoices = invoices.Where(invoice => invoice.CustomerLedgerId == customer);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim();

            invoices = invoices.Where(invoice =>
                EF.Functions.ILike(invoice.Number, $"%{term}%")
                || (invoice.ReferenceNumber != null
                    && EF.Functions.ILike(invoice.ReferenceNumber, $"%{term}%")));
        }

        return invoices;
    }

    private static SalesInvoiceSummary Describe(
        SalesInvoice document,
        IReadOnlyDictionary<LedgerId, Ledger> customers)
    {
        Ledger? customer = customers.GetValueOrDefault(document.CustomerLedgerId);

        return new SalesInvoiceSummary(
            document.Id.Value,
            document.Number,
            document.Kind,
            document.Date,
            document.CustomerLedgerId.Value,
            customer?.Code ?? string.Empty,
            customer?.Name ?? string.Empty,
            document.Status,
            document.Currency.Code,
            document.ReferenceNumber,
            document.Lines.Count,
            document.Taxable.Amount,
            document.Tax.Amount,
            document.Total.Amount);
    }
}
