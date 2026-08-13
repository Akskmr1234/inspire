using ERP.Application.Abstractions;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Purchase;
using ERP.Domain.Accounting;
using ERP.Domain.Purchase;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads pages of purchase documents for a list.</summary>
/// <remarks>
/// The page is taken in the database and the totals are worked out afterwards, from the
/// documents themselves - the same arrangement the sales list uses and for the same
/// reason: summing lines and charges in SQL would mean writing the rounding rule a second
/// time, and the day the two spellings disagreed a list would contradict the document it
/// lists.
/// </remarks>
public sealed class PurchaseInvoiceReader : IPurchaseInvoiceReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="PurchaseInvoiceReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public PurchaseInvoiceReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<PagedResult<PurchaseInvoiceSummary>> ListAsync(
        FirmId firmId,
        PurchaseInvoiceFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<PurchaseInvoice> matching = Narrow(
            _context.PurchaseInvoices.Where(invoice => invoice.FirmId == firmId), filter);

        int total = await matching.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult.Empty<PurchaseInvoiceSummary>(page, pageSize);
        }

        // Newest first, and the number breaks a tie: several documents share a date on any
        // busy day, and a list whose order changed between one page and the next would
        // show the same row twice and skip another.
        List<PurchaseInvoiceId> ids = await matching
            .OrderByDescending(invoice => invoice.Date)
            .ThenByDescending(invoice => invoice.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(invoice => invoice.Id)
            .ToListAsync(cancellationToken);

        List<PurchaseInvoice> documents = await _context.PurchaseInvoices
            .Include(invoice => invoice.Lines)
            .Include(invoice => invoice.Charges)
            .Where(invoice => ids.Contains(invoice.Id))
            .ToListAsync(cancellationToken);

        Dictionary<LedgerId, Ledger> suppliers = await _context.Ledgers
            .Where(ledger => documents.Select(d => d.SupplierLedgerId).Contains(ledger.Id))
            .ToDictionaryAsync(ledger => ledger.Id, cancellationToken);

        List<PurchaseInvoiceSummary> rows =
        [
            .. ids
                .Select(id => documents.Find(document => document.Id == id))
                .Where(document => document is not null)
                .Select(document => Describe(document!, suppliers)),
        ];

        return new PagedResult<PurchaseInvoiceSummary>(rows, page, pageSize, total);
    }

    private static IQueryable<PurchaseInvoice> Narrow(
        IQueryable<PurchaseInvoice> invoices,
        PurchaseInvoiceFilter filter)
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

        if (filter.SupplierLedgerId is { } supplier)
        {
            invoices = invoices.Where(invoice => invoice.SupplierLedgerId == supplier);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim();

            invoices = invoices.Where(invoice =>
                EF.Functions.ILike(invoice.Number, $"%{term}%")
                || (invoice.SupplierInvoiceNumber != null
                    && EF.Functions.ILike(invoice.SupplierInvoiceNumber, $"%{term}%")));
        }

        return invoices;
    }

    private static PurchaseInvoiceSummary Describe(
        PurchaseInvoice document,
        IReadOnlyDictionary<LedgerId, Ledger> suppliers)
    {
        Ledger? supplier = suppliers.GetValueOrDefault(document.SupplierLedgerId);

        return new PurchaseInvoiceSummary(
            document.Id.Value,
            document.Number,
            document.Kind,
            document.Date,
            document.SupplierLedgerId.Value,
            supplier?.Code ?? string.Empty,
            supplier?.Name ?? string.Empty,
            document.Status,
            document.Currency.Code,
            document.SupplierInvoiceNumber,
            document.SupplierInvoiceDate,
            document.Lines.Count,
            document.Taxable.Amount,
            document.Tax.Amount,
            document.Total.Amount);
    }
}
