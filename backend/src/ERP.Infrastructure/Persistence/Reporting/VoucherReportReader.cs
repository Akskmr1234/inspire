using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the vouchers behind the voucher report.</summary>
/// <remarks>
/// <para>
/// Unlike the day book, this reader is deliberately not restricted to posted
/// vouchers: a register whose purpose is to find a particular voucher must be able to
/// show a draft awaiting posting or a cancelled entry somebody is asking about. It
/// does exclude soft-deleted rows, which are a genuine purge - a discarded draft whose
/// number was handed back - and have no place on any report.
/// </para>
/// <para>
/// Each voucher's value is the sum of its debit lines, gathered in one grouped query
/// rather than by loading the lines per voucher. Both the document amount and the base
/// amount are summed together, so a multi-currency voucher can be shown in the currency
/// it was entered in and still be totalled against the rest in the base currency.
/// </para>
/// </remarks>
public sealed class VoucherReportReader : IVoucherReportReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="VoucherReportReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public VoucherReportReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<VoucherReportLine>> ReadAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        VoucherType? type,
        VoucherStatus? status,
        CancellationToken cancellationToken = default)
    {
        // Firm, date range, and status ride the ix_vouchers_firm_date_status index.
        // Soft-deleted vouchers are excluded here rather than by a global filter,
        // because none is configured - the day book gets away without one only because
        // it takes posted vouchers alone.
        IQueryable<Voucher> vouchers = _context.Vouchers
            .Where(v => v.FirmId == firmId && !v.IsDeleted && v.Date >= from && v.Date <= to);

        if (type is { } voucherType)
        {
            vouchers = vouchers.Where(v => v.Type == voucherType);
        }

        if (status is { } voucherStatus)
        {
            vouchers = vouchers.Where(v => v.Status == voucherStatus);
        }

        var headers = await vouchers
            .Select(v => new
            {
                v.Id,
                v.Date,
                v.Number,
                v.Type,
                v.Status,
                v.ReferenceNumber,
                v.Narration,
                v.Currency,
                v.ExchangeRate,
            })
            .ToListAsync(cancellationToken);

        if (headers.Count == 0)
        {
            return [];
        }

        List<VoucherId> voucherIds = [.. headers.Select(h => h.Id)];

        // The value of every voucher, summed from its debit lines in one query. Doing
        // it per voucher would be a round trip each, which over a year of a busy branch
        // is exactly the N+1 the day book reader is also at pains to avoid. Debits
        // rather than credits by convention; the two are equal by the voucher's own
        // balance invariant.
        Dictionary<VoucherId, (decimal Document, decimal Base)> totals =
            await _context.VoucherLines
                .Where(line =>
                    voucherIds.Contains(line.VoucherId) && line.Side == EntrySide.Debit)
                .GroupBy(line => line.VoucherId)
                .Select(group => new
                {
                    VoucherId = group.Key,
                    Document = group.Sum(line => line.Amount.Amount),
                    Base = group.Sum(line => line.BaseAmount.Amount),
                })
                .ToDictionaryAsync(
                    row => row.VoucherId, row => (row.Document, row.Base), cancellationToken);

        List<VoucherReportLine> rows = new(headers.Count);

        foreach (var header in headers)
        {
            // A voucher with no debit lines has no total. A draft can legitimately be
            // empty mid-entry, and it counts as zero rather than being dropped, so the
            // register still lists it - which is one of the things the register is for.
            (decimal document, decimal @base) = totals.GetValueOrDefault(header.Id);

            rows.Add(new VoucherReportLine(
                header.Id.Value,
                header.Date,
                header.Number,
                header.Type,
                header.Status,
                header.ReferenceNumber,
                header.Narration,
                header.Currency.Code,
                header.ExchangeRate,
                document,
                @base));
        }

        return rows;
    }
}
