using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the rates a customer has been quoted and charged.</summary>
public sealed class SalesRateReader : ISalesRateReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="SalesRateReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public SalesRateReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    /// <remarks>
    /// A quotation counts whatever became of it: a declined one still says what the
    /// customer was offered. A sale counts only once posted, and a line given away free
    /// is not a rate anybody charged.
    /// </remarks>
    public async Task<SalesRateHints> ReadAsync(
        FirmId firmId,
        LedgerId customerLedgerId,
        ProductId productId,
        CancellationToken cancellationToken = default)
    {
        SalesRateHint? quoted = await (
                from line in _context.SalesOrderLines.AsNoTracking()
                join order in _context.SalesOrders.AsNoTracking() on line.SalesOrderId equals order.Id
                where order.FirmId == firmId
                    && order.Kind == SalesOrderKind.Quotation
                    && order.CustomerLedgerId == customerLedgerId
                    && line.ProductId == productId
                orderby order.Date descending, order.CreatedAtUtc descending, line.LineNumber
                select new SalesRateHint(line.Rate, line.UnitId.Value, order.Number, order.Date))
            .FirstOrDefaultAsync(cancellationToken);

        SalesRateHint? sold = await (
                from line in _context.SalesInvoiceLines.AsNoTracking()
                join invoice in _context.SalesInvoices.AsNoTracking() on line.SalesInvoiceId equals invoice.Id
                where invoice.FirmId == firmId
                    && invoice.Kind == SalesDocumentKind.Invoice
                    && invoice.Status == SalesInvoiceStatus.Posted
                    && invoice.CustomerLedgerId == customerLedgerId
                    && line.ProductId == productId
                    && line.Rate > 0m
                orderby invoice.Date descending, invoice.CreatedAtUtc descending, line.LineNumber
                select new SalesRateHint(line.Rate, line.UnitId.Value, invoice.Number, invoice.Date))
            .FirstOrDefaultAsync(cancellationToken);

        return new SalesRateHints(quoted, sold);
    }
}
