using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>
/// Reads the cheques behind the PDC report, the PDC calendar, and the cheque
/// register.
/// </summary>
/// <remarks>
/// <para>
/// One reader for all three, because they differ only in which date they read by and
/// how the handler above arranges the result - not in what a cheque is. The criteria
/// carry that difference: <see cref="ChequeReportCriteria.ByInstrumentDate"/> chooses
/// the date the range applies to, and <see cref="ChequeReportCriteria.OpenOnly"/>
/// drops the cheques that have already resolved.
/// </para>
/// <para>
/// The bank shown is the firm's own account once one is known, resolved here from the
/// bank ledger; the payer's bank named on a received cheque still in hand is free text
/// on the cheque itself, and the handler falls back to it. The account names are
/// fetched in one query keyed by ledger rather than joined per row, because a received
/// cheque has no account until it is banked and an inner join would silently drop
/// every pending one.
/// </para>
/// </remarks>
public sealed class ChequeReportReader : IChequeReportReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="ChequeReportReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public ChequeReportReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChequeReportRow>> ReadAsync(
        ChequeReportCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        IQueryable<Cheque> cheques = _context.Cheques
            .Where(c => c.FirmId == criteria.FirmId);

        // The register reads by when a cheque changed hands; the PDC report and
        // calendar read by the date on its face. Which one the range applies to is the
        // whole of the difference between them.
        cheques = criteria.ByInstrumentDate
            ? cheques.Where(c =>
                c.InstrumentDate >= criteria.From && c.InstrumentDate <= criteria.To)
            : cheques.Where(c =>
                c.RecordedOn >= criteria.From && c.RecordedOn <= criteria.To);

        if (criteria.Direction is { } direction)
        {
            cheques = cheques.Where(c => c.Direction == direction);
        }

        if (criteria.Status is { } status)
        {
            cheques = cheques.Where(c => c.Status == status);
        }

        // Everything still live: Pending and Deposited are below Cleared, and every
        // terminal status is Cleared or above. Expressed as the range the partial
        // index is filtered on rather than the two values, so the planner can use it
        // and so a further open state added between them needs no change here.
        if (criteria.OpenOnly)
        {
            cheques = cheques.Where(c => c.Status < ChequeStatus.Cleared);
        }

        if (criteria.LedgerId is { } party)
        {
            cheques = cheques.Where(c => c.PartyLedgerId == party);
        }

        var candidates = await cheques
            .Join(
                _context.Ledgers,
                cheque => cheque.PartyLedgerId,
                ledger => ledger.Id,
                (cheque, ledger) => new
                {
                    cheque.Id,
                    cheque.ChequeNumber,
                    cheque.Direction,
                    cheque.Status,
                    cheque.PartyLedgerId,
                    PartyCode = ledger.Code,
                    PartyName = ledger.Name,
                    cheque.InstrumentDate,
                    cheque.RecordedOn,
                    Amount = cheque.Amount.Amount,
                    Currency = cheque.Amount.Currency,
                    cheque.BankLedgerId,
                    cheque.DrawnOnBank,
                    cheque.DepositedOn,
                    cheque.ClosedOn,
                    cheque.ClosureReason,
                })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        // The firm's own accounts, in one query rather than a join per row. A join
        // would have to be a left join - a received cheque in hand has no account yet -
        // and doing it here keeps the pending cheques the report exists to show.
        List<LedgerId> bankLedgerIds = [.. candidates
            .Where(candidate => candidate.BankLedgerId.HasValue)
            .Select(candidate => candidate.BankLedgerId!.Value)
            .Distinct()];

        Dictionary<LedgerId, string> bankNames = bankLedgerIds.Count == 0
            ? []
            : await _context.Ledgers
                .Where(ledger => bankLedgerIds.Contains(ledger.Id))
                .Select(ledger => new { ledger.Id, ledger.Name })
                .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);

        List<ChequeReportRow> rows = new(candidates.Count);

        foreach (var candidate in candidates)
        {
            string? bankAccountName = candidate.BankLedgerId is { } bankLedgerId
                ? bankNames.GetValueOrDefault(bankLedgerId)
                : null;

            rows.Add(new ChequeReportRow(
                candidate.Id.Value,
                candidate.ChequeNumber,
                candidate.Direction,
                candidate.Status,
                candidate.PartyLedgerId.Value,
                candidate.PartyCode,
                candidate.PartyName,
                candidate.InstrumentDate,
                candidate.RecordedOn,
                candidate.Amount,
                candidate.Currency.Code,
                bankAccountName,
                candidate.DrawnOnBank,
                candidate.DepositedOn,
                candidate.ClosedOn,
                candidate.ClosureReason));
        }

        return rows;
    }
}
