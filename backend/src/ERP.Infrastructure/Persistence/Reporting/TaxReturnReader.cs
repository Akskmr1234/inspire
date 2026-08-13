using ERP.Application.Abstractions.Persistence;
using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the figures a statutory tax return is built from.</summary>
/// <remarks>
/// <para>
/// Only <b>posted</b> documents and vouchers count. A draft has charged nobody anything
/// and a cancelled document has been taken back, so either on a return would state a
/// liability the firm does not have.
/// </para>
/// <para>
/// Returns are netted rather than listed apart. A credit note reduces the tax owed for the
/// period it falls in, which is what both regimes' returns do with them - and its sign is
/// already the document's kind, so nothing here has to decide it twice.
/// </para>
/// <para>
/// The taxable value is counted <b>once per line</b>, not once per head. A supply under
/// GST carries CGST and SGST on the same base; adding the base up per head would report
/// twice the sales the firm actually made, which is the single easiest way to make a
/// return wrong and the hardest to notice, because every tax figure on it would still be
/// right.
/// </para>
/// </remarks>
public sealed class TaxReturnReader : ITaxReturnReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="TaxReturnReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public TaxReturnReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<OutputTaxReport> ReadOutputAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        Firm? firm = await _context.Firms
            .FirstOrDefaultAsync(f => f.Id == firmId, cancellationToken);

        var rows = await OutputRowsAsync(firmId, from, to, cancellationToken);

        // Counted once per line, however many heads that line carried.
        var bases = await TaxableByLineAsync(firmId, from, to, cancellationToken);

        decimal taxable = bases.Where(line => line.Taxed).Sum(line => line.Amount);
        decimal zeroRated = bases.Where(line => !line.Taxed).Sum(line => line.Amount);

        return new OutputTaxReport(
            from,
            to,
            firm?.TaxRegime ?? TaxRegime.None,
            firm?.BaseCurrency.Code ?? string.Empty,
            taxable,
            zeroRated,
            Totals(rows.Select(row => (row.Component, row.TaxAmount))),
            rows);
    }

    /// <inheritdoc />
    public async Task<InputTaxReport> ReadInputAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        Firm? firm = await _context.Firms
            .FirstOrDefaultAsync(f => f.Id == firmId, cancellationToken);

        IReadOnlyList<InputTaxRow> rows = await InputRowsAsync(
            firmId, from, to, cancellationToken);

        // Counted once per line, however many heads that line carried - the same trap the
        // output side avoids, and the same fix.
        var bases = await PurchasedByLineAsync(firmId, from, to, cancellationToken);

        return new InputTaxReport(
            from,
            to,
            firm?.TaxRegime ?? TaxRegime.None,
            firm?.BaseCurrency.Code ?? string.Empty,
            bases.Where(line => line.Taxed).Sum(line => line.Amount),
            bases.Where(line => !line.Taxed).Sum(line => line.Amount),
            Totals(rows.Select(row => (row.Component, row.TaxAmount))),
            rows);
    }

    /// <inheritdoc />
    public async Task<TaxSummaryReport> ReadSummaryAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        OutputTaxReport output = await ReadOutputAsync(firmId, from, to, cancellationToken);
        InputTaxReport input = await ReadInputAsync(firmId, from, to, cancellationToken);

        // What the tax accounts themselves moved by, so a journal somebody wrote straight
        // into one is not silently left out of the return.
        IReadOnlyDictionary<TaxComponentType, decimal> postedOut = await PostedByHeadAsync(
            firmId, from, to, TaxDirection.Output, cancellationToken);

        IReadOnlyDictionary<TaxComponentType, decimal> postedIn = await PostedByHeadAsync(
            firmId, from, to, TaxDirection.Input, cancellationToken);

        Dictionary<TaxComponentType, decimal> outputs = output.Totals
            .ToDictionary(total => total.Component, total => total.TaxAmount);

        Dictionary<TaxComponentType, decimal> inputs = input.Totals
            .ToDictionary(total => total.Component, total => total.TaxAmount);

        List<TaxComponentType> heads =
        [
            .. outputs.Keys
                .Concat(inputs.Keys)
                .Concat(postedOut.Keys)
                .Concat(postedIn.Keys)
                .Distinct()
                .OrderBy(head => head),
        ];

        List<TaxSummaryLine> lines = [];

        foreach (TaxComponentType head in heads)
        {
            decimal charged = outputs.GetValueOrDefault(head);
            decimal recovered = inputs.GetValueOrDefault(head);
            decimal chargedOnLedger = postedOut.GetValueOrDefault(head);
            decimal recoveredOnLedger = postedIn.GetValueOrDefault(head);

            lines.Add(new TaxSummaryLine(
                head,
                charged,
                recovered,
                charged - recovered,
                chargedOnLedger,
                chargedOnLedger - charged,
                recoveredOnLedger,
                recoveredOnLedger - recovered));
        }

        return new TaxSummaryReport(
            from,
            to,
            output.Regime,
            output.Currency,
            output.TaxableSupplies,
            output.ZeroRatedSupplies,
            input.TaxablePurchases,
            input.ZeroRatedPurchases,
            lines,
            lines.Sum(line => line.NetPayable),
            lines.TrueForAll(line => line.Difference == 0m && line.InputDifference == 0m));
    }

    /// <summary>Sums a set of amounts by head, dropping heads that came to nothing.</summary>
    /// <remarks>
    /// A head that netted to zero across the period - everything sold under it returned -
    /// is left out rather than shown as a row of zeroes, which on a return reads as a
    /// head the firm charges rather than one it happened not to owe on.
    /// </remarks>
    private static IReadOnlyList<TaxHeadTotal> Totals(
        IEnumerable<(TaxComponentType Component, decimal TaxAmount)> amounts) =>
        [
            .. amounts
                .GroupBy(entry => entry.Component)
                .Select(group => new TaxHeadTotal(group.Key, group.Sum(entry => entry.TaxAmount)))
                .Where(total => total.TaxAmount != 0m)
                .OrderBy(total => total.Component),
        ];

    /// <summary>Reads every head charged on every posted document in the period.</summary>
    private async Task<IReadOnlyList<OutputTaxRow>> OutputRowsAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        // Aliased: `from` is a query-expression keyword, so the parameter cannot be
        // named inside the query itself.
        DateOnly start = from;
        DateOnly end = to;

        var rows = await (
            from invoice in _context.SalesInvoices
            join line in _context.SalesInvoiceLines on invoice.Id equals line.SalesInvoiceId
            join component in _context.SalesInvoiceLineTaxes
                on line.Id equals component.SalesInvoiceLineId
            join customer in _context.Ledgers on invoice.CustomerLedgerId equals customer.Id
            where invoice.FirmId == firmId
                && !invoice.IsDeleted
                && invoice.Status == SalesInvoiceStatus.Posted
                && invoice.Date >= start
                && invoice.Date <= end
            select new
            {
                invoice.Id,
                invoice.Number,
                invoice.Kind,
                invoice.Date,
                CustomerCode = customer.Code,
                CustomerName = customer.Name,
                customer.TaxRegistrationNumber,
                customer.StateCode,
                component.Type,
                component.Percentage,
                Taxable = line.TaxableAmount.Amount,
                Tax = component.Amount,
            }).ToListAsync(cancellationToken);

        return
        [
            .. rows
                .OrderBy(row => row.Date)
                .ThenBy(row => row.Number, StringComparer.Ordinal)
                .ThenBy(row => row.Type)
                .Select(row => new OutputTaxRow(
                    row.Id.Value,
                    row.Number,
                    row.Kind,
                    row.Date,
                    row.CustomerCode,
                    row.CustomerName,
                    row.TaxRegistrationNumber,
                    row.StateCode,
                    row.Type,
                    row.Percentage,
                    Sign(row.Kind) * row.Taxable,
                    Sign(row.Kind) * row.Tax)),
        ];
    }

    /// <summary>Reads what each posted line was charged on, once per line.</summary>
    private async Task<IReadOnlyList<(decimal Amount, bool Taxed)>> TaxableByLineAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        DateOnly start = from;
        DateOnly end = to;

        var lines = await (
            from invoice in _context.SalesInvoices
            join line in _context.SalesInvoiceLines on invoice.Id equals line.SalesInvoiceId
            where invoice.FirmId == firmId
                && !invoice.IsDeleted
                && invoice.Status == SalesInvoiceStatus.Posted
                && invoice.Date >= start
                && invoice.Date <= end
            select new
            {
                invoice.Kind,
                Taxable = line.TaxableAmount.Amount,
                Tax = line.TaxAmount.Amount,
            }).ToListAsync(cancellationToken);

        return
        [
            .. lines.Select(line =>
                (Sign(line.Kind) * line.Taxable, line.Tax != 0m)),
        ];
    }

    /// <summary>Reads the input tax of a period: the purchases, and anything booked by hand.</summary>
    /// <remarks>
    /// Two sources, and the second is the interesting one. A purchase document knows the
    /// rate and the value the tax was charged on, so it produces the rows a return actually
    /// wants. A journal somebody wrote straight into an input account knows only the money -
    /// but it is still input tax sitting in the ledger, and leaving it out would make the
    /// return understate what is reclaimable. So the postings a purchase's own journal made
    /// are excluded by identity and everything else is listed, baseless and labelled so.
    /// </remarks>
    private async Task<IReadOnlyList<InputTaxRow>> InputRowsAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        List<InputTaxRow> rows = [.. await PurchaseRowsAsync(firmId, from, to, cancellationToken)];

        rows.AddRange(await UnexplainedPostingsAsync(firmId, from, to, cancellationToken));

        return
        [
            .. rows
                .OrderBy(row => row.Date)
                .ThenBy(row => row.Number, StringComparer.Ordinal)
                .ThenBy(row => row.Component),
        ];
    }

    /// <summary>Reads every head charged on every posted purchase in the period.</summary>
    private async Task<IReadOnlyList<InputTaxRow>> PurchaseRowsAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        DateOnly start = from;
        DateOnly end = to;

        var rows = await (
            from invoice in _context.PurchaseInvoices
            join line in _context.PurchaseInvoiceLines
                on invoice.Id equals line.PurchaseInvoiceId
            join component in _context.PurchaseInvoiceLineTaxes
                on line.Id equals component.PurchaseInvoiceLineId
            join supplier in _context.Ledgers on invoice.SupplierLedgerId equals supplier.Id
            where invoice.FirmId == firmId
                && !invoice.IsDeleted
                && invoice.Status == PurchaseInvoiceStatus.Posted
                && invoice.Date >= start
                && invoice.Date <= end
            select new
            {
                invoice.Id,
                invoice.Number,
                invoice.Kind,
                invoice.Date,
                invoice.SupplierInvoiceNumber,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                supplier.TaxRegistrationNumber,
                component.Type,
                component.Percentage,
                Taxable = line.TaxableAmount.Amount,
                Tax = component.Amount,
            }).ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new InputTaxRow(
                row.Id.Value,
                row.Number,
                row.Kind,
                row.Date,
                row.SupplierCode,
                row.SupplierName,
                row.TaxRegistrationNumber,
                row.SupplierInvoiceNumber,
                row.Type,
                row.Percentage,
                Sign(row.Kind) * row.Taxable,
                Sign(row.Kind) * row.Tax,
                Narration: null)),
        ];
    }

    /// <summary>Reads what each posted purchase line was charged on, once per line.</summary>
    private async Task<IReadOnlyList<(decimal Amount, bool Taxed)>> PurchasedByLineAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        DateOnly start = from;
        DateOnly end = to;

        var lines = await (
            from invoice in _context.PurchaseInvoices
            join line in _context.PurchaseInvoiceLines
                on invoice.Id equals line.PurchaseInvoiceId
            where invoice.FirmId == firmId
                && !invoice.IsDeleted
                && invoice.Status == PurchaseInvoiceStatus.Posted
                && invoice.Date >= start
                && invoice.Date <= end
            select new
            {
                invoice.Kind,
                Taxable = line.TaxableAmount.Amount,
                Tax = line.TaxAmount.Amount,
            }).ToListAsync(cancellationToken);

        return [.. lines.Select(line => (Sign(line.Kind) * line.Taxable, line.Tax != 0m))];
    }

    /// <summary>Reads input tax that reached the ledger by some route other than a purchase.</summary>
    /// <remarks>
    /// Identified by the voucher rather than by the amount: every posted purchase names the
    /// journal it raised, so excluding those leaves exactly the entries somebody wrote by
    /// hand. Matching on figures instead would drop a hand-written entry that happened to
    /// equal a purchase's tax, which is not a coincidence a return should be built on.
    /// </remarks>
    private async Task<IReadOnlyList<InputTaxRow>> UnexplainedPostingsAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<LedgerId, TaxComponentType> accounts = await MappedAccountsAsync(
            firmId, TaxDirection.Input, cancellationToken);

        if (accounts.Count == 0)
        {
            return [];
        }

        List<LedgerId> ledgerIds = [.. accounts.Keys];

        DateOnly start = from;
        DateOnly end = to;

        List<VoucherId> raisedByPurchases = await _context.PurchaseInvoices
            .Where(invoice =>
                invoice.FirmId == firmId
                && !invoice.IsDeleted
                && invoice.JournalVoucherId != null)
            .Select(invoice => invoice.JournalVoucherId!.Value)
            .ToListAsync(cancellationToken);

        var postings = await (
            from voucher in _context.Vouchers
            join line in _context.VoucherLines on voucher.Id equals line.VoucherId
            join ledger in _context.Ledgers on line.LedgerId equals ledger.Id
            where voucher.FirmId == firmId
                && !voucher.IsDeleted
                && voucher.Status == VoucherStatus.Posted
                && voucher.Date >= start
                && voucher.Date <= end
                && ledgerIds.Contains(line.LedgerId)
                && !raisedByPurchases.Contains(voucher.Id)
            select new
            {
                voucher.Id,
                voucher.Number,
                voucher.Date,
                line.LedgerId,
                LedgerName = ledger.Name,
                line.Side,
                Amount = line.Amount.Amount,
                line.Narration,
            }).ToListAsync(cancellationToken);

        return
        [
            .. postings.Select(posting => new InputTaxRow(
                posting.Id.Value,
                posting.Number,
                Kind: null,
                posting.Date,
                SupplierCode: string.Empty,
                posting.LedgerName,
                TaxRegistrationNumber: null,
                SupplierInvoiceNumber: null,
                accounts[posting.LedgerId],
                Percentage: 0m,
                TaxableAmount: null,
                // A debit is tax the firm may recover; a credit gives it back.
                posting.Side == EntrySide.Debit ? posting.Amount : -posting.Amount,
                posting.Narration)),
        ];
    }

    /// <summary>Sums what the accounts of one direction actually moved by.</summary>
    private async Task<IReadOnlyDictionary<TaxComponentType, decimal>> PostedByHeadAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        TaxDirection direction,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<LedgerId, TaxComponentType> accounts = await MappedAccountsAsync(
            firmId, direction, cancellationToken);

        if (accounts.Count == 0)
        {
            return new Dictionary<TaxComponentType, decimal>();
        }

        List<LedgerId> ledgerIds = [.. accounts.Keys];

        DateOnly start = from;
        DateOnly end = to;

        var postings = await (
            from voucher in _context.Vouchers
            join line in _context.VoucherLines on voucher.Id equals line.VoucherId
            where voucher.FirmId == firmId
                && !voucher.IsDeleted
                && voucher.Status == VoucherStatus.Posted
                && voucher.Date >= start
                && voucher.Date <= end
                && ledgerIds.Contains(line.LedgerId)
            select new
            {
                line.LedgerId,
                line.Side,
                Amount = line.Amount.Amount,
            }).ToListAsync(cancellationToken);

        Dictionary<TaxComponentType, decimal> totals = [];

        foreach (var posting in postings)
        {
            TaxComponentType head = accounts[posting.LedgerId];

            // Output tax is a liability, so a credit is what the firm owes. Stated as a
            // positive figure to sit beside the documents, which state it the same way.
            decimal signed = posting.Side == EntrySide.Credit
                ? posting.Amount
                : -posting.Amount;

            totals[head] = totals.GetValueOrDefault(head)
                + (direction == TaxDirection.Output ? signed : -signed);
        }

        return totals;
    }

    /// <summary>The accounts a firm has mapped to heads in one direction.</summary>
    private async Task<IReadOnlyDictionary<LedgerId, TaxComponentType>> MappedAccountsAsync(
        FirmId firmId,
        TaxDirection direction,
        CancellationToken cancellationToken)
    {
        var assignments = await (
            from map in _context.TaxAccountMaps
            join assignment in _context.TaxAccountAssignments
                on map.Id equals assignment.TaxAccountMapId
            where map.FirmId == firmId && assignment.Direction == direction
            select new { assignment.LedgerId, assignment.Component })
            .ToListAsync(cancellationToken);

        // Keyed by ledger, because two heads pointed at one account would otherwise
        // throw here rather than simply reporting under whichever was mapped first.
        return assignments
            .GroupBy(assignment => assignment.LedgerId)
            .ToDictionary(group => group.Key, group => group.First().Component);
    }

    /// <summary>Which way a document runs: a return reduces what is owed.</summary>
    private static decimal Sign(SalesDocumentKind kind) =>
        kind == SalesDocumentKind.Return ? -1m : 1m;

    /// <summary>The same, for a purchase: goods going back reduce what is reclaimable.</summary>
    private static decimal Sign(PurchaseDocumentKind kind) =>
        kind == PurchaseDocumentKind.Return ? -1m : 1m;
}
