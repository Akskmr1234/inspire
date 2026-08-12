using ERP.Application.Abstractions.Persistence;
using ERP.Application.Accounting.Vouchers;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Stock;

/// <summary>Raises and withdraws the journal a stock document owes the ledger.</summary>
/// <remarks>
/// <para>
/// Kept beside the stock poster rather than inside it. The two answer different
/// questions - what is on the shelf, and what the books say it is worth - and they fail
/// for different reasons: a movement is refused because the goods are not there, and a
/// journal is refused because nobody has said which account it posts to. Keeping them
/// apart means each refusal says which of the two happened.
/// </para>
/// <para>
/// Everything runs in the caller's transaction. A stock document whose journal could
/// not be raised does not post at all, which is the only arrangement that keeps the
/// stock ledger and the nominal ledger describing the same firm.
/// </para>
/// </remarks>
internal sealed class StockJournalPoster
{
    private readonly IInventoryAccountMapRepository _accounts;
    private readonly IVoucherRepository _vouchers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;

    internal StockJournalPoster(
        IInventoryAccountMapRepository accounts,
        IVoucherRepository vouchers,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears)
    {
        _accounts = accounts;
        _vouchers = vouchers;
        _numbering = numbering;
        _financialYears = financialYears;
    }

    /// <summary>Raises the journal for a document that has just posted.</summary>
    /// <param name="document">The document, already posted.</param>
    /// <param name="movements">The stock ledger entries it wrote.</param>
    /// <param name="firm">The firm, for its base currency.</param>
    /// <param name="branchId">The branch the journal belongs to.</param>
    /// <param name="postedBy">The user posting it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or the reason the document cannot post.</returns>
    internal async Task<Result> RaiseAsync(
        StockDocument document,
        IReadOnlyList<StockLedgerEntry> movements,
        Firm firm,
        BranchId branchId,
        UserId postedBy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Asked before anything is loaded, so a transfer costs nothing: it moves goods
        // between shelves without changing whose they are or what they are worth, and
        // has nothing to say to the accounts.
        if (StockJournal.CounterAccountFor(document.Type, incoming: true) is null)
        {
            return Result.Success();
        }

        InventoryAccountMap? map = await _accounts.FindAsync(document.FirmId, cancellationToken);

        if (map is null)
        {
            return Result.Failure(Error.BusinessRule(
                "InventoryAccounts.NotConfigured",
                "This firm has not chosen which accounts stock posts to. Set the "
                + "inventory accounts up before posting stock."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            document.FirmId, document.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {document.Date:yyyy-MM-dd}."));
        }

        Result<string> number = await JournalNumbering.ReserveAsync(
            _numbering, document.TenantId, document.FirmId, branchId, year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure(number.Error);
        }

        Result<Voucher?> raised = StockJournal.Raise(
            document, movements, map, firm, branchId, year, number.Value);

        if (raised.IsFailure)
        {
            return Result.Failure(raised.Error);
        }

        // Nothing moved in value terms - a count that agreed with the books, or goods
        // nobody has ever costed. There is no journal to raise and none is owed.
        if (raised.Value is not { } journal)
        {
            return Result.Success();
        }

        Result posted = journal.Post(postedBy, nowUtc);

        if (posted.IsFailure)
        {
            return Result.Failure(posted.Error);
        }

        _vouchers.Add(journal);

        return document.RecordJournal(journal.Id);
    }

    /// <summary>Withdraws the journal of a document being cancelled.</summary>
    /// <param name="document">The document being cancelled.</param>
    /// <param name="reason">Why it is being cancelled.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or the reason the journal cannot be withdrawn.</returns>
    /// <remarks>
    /// The journal is cancelled rather than reversed by a contra. A voucher's own
    /// cancellation already keeps its number and its lines and takes it out of the
    /// balances - that is how every other cancelled voucher in this system behaves, and
    /// a stock document's journal is not special enough to behave differently.
    /// </remarks>
    internal async Task<Result> WithdrawAsync(
        StockDocument document,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.JournalVoucherId is not { } journalId)
        {
            return Result.Success();
        }

        Voucher? journal = await _vouchers.FindAsync(journalId, cancellationToken);

        if (journal is null)
        {
            return Result.Failure(Error.NotFound(
                "StockDocument.JournalMissing",
                $"The journal document '{document.Number}' raised no longer exists."));
        }

        return journal.Status == VoucherStatus.Cancelled
            ? Result.Success()
            : journal.Cancel($"Stock document {document.Number} cancelled: {reason}");
    }
}
