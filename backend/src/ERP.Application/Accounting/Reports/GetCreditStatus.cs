using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Accounting.Reports;

/// <summary>What a party owes, against what they were allowed to owe.</summary>
/// <param name="LedgerId">The party.</param>
/// <param name="AsAt">The date to state the position as at. Defaults to today.</param>
/// <remarks>
/// The reading half of the business's answer of 2026-08-10: a credit limit warns rather
/// than blocks. Nothing here refuses anything - it says where the party stands, and the
/// document that asks decides what to do with the answer.
/// </remarks>
public sealed record GetCreditStatusQuery(Guid LedgerId, DateOnly? AsAt = null)
    : IQuery<CreditStatus>;

/// <summary>Where a party stands against their credit limit.</summary>
/// <param name="LedgerId">The party.</param>
/// <param name="LedgerCode">Their ledger code.</param>
/// <param name="LedgerName">Their name.</param>
/// <param name="Currency">The currency the figures are stated in.</param>
/// <param name="CreditLimit">
/// What they were allowed to owe, or <see langword="null"/> where no limit was agreed.
/// </param>
/// <param name="CreditDays">The period agreed, or <see langword="null"/> for none.</param>
/// <param name="Outstanding">What they owe as at the date.</param>
/// <param name="Overdue">How much of that is past its due date.</param>
/// <param name="Available">
/// What is left of the limit. Negative once it is passed, and
/// <see langword="null"/> where there is no limit to be left of.
/// </param>
/// <param name="IsOverLimit">Whether they are past the limit already.</param>
public sealed record CreditStatus(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    string Currency,
    decimal? CreditLimit,
    int? CreditDays,
    decimal Outstanding,
    decimal Overdue,
    decimal? Available,
    bool IsOverLimit);

/// <summary>Handles <see cref="GetCreditStatusQuery"/>.</summary>
/// <remarks>
/// Read from the open bills rather than from the ledger's balance. The two answer
/// different questions: a balance includes payments on account and anything else posted
/// to the party, while a credit limit is about invoices they have not paid. A customer
/// who has paid in advance is not using credit, and a balance would say they were.
/// </remarks>
public sealed class GetCreditStatusQueryHandler : IQueryHandler<GetCreditStatusQuery, CreditStatus>
{
    private readonly ILedgerRepository _ledgers;
    private readonly IOutstandingBillsReader _bills;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="GetCreditStatusQueryHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="bills">The outstanding bills reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    public GetCreditStatusQueryHandler(
        ILedgerRepository ledgers,
        IOutstandingBillsReader bills,
        ITenantContext tenantContext,
        IClock clock)
    {
        _ledgers = ledgers;
        _bills = bills;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<CreditStatus>> Handle(
        GetCreditStatusQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<CreditStatus>(Error.Forbidden(
                "Ledger.NoFirmSelected", "A firm must be selected to read a credit position."));
        }

        Ledger? party = await _ledgers.FindAsync(
            LedgerId.From(request.LedgerId), cancellationToken);

        if (party is null || party.FirmId != firmId)
        {
            return Result.Failure<CreditStatus>(Error.NotFound(
                "Ledger.NotFound", "No such ledger in the selected firm."));
        }

        // Which side of the books they sit on decides which bills count. A supplier's
        // credit limit is what the firm may owe them, and reading receivables for it
        // would report nothing at all.
        BillType type = party.Kind == LedgerKind.Supplier
            ? BillType.Payable
            : BillType.Receivable;

        DateOnly asAt = request.AsAt ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        IReadOnlyList<OutstandingBillRow> bills = await _bills.ReadAsync(
            firmId, type, asAt, party.Id, cancellationToken);

        decimal outstanding = bills.Sum(bill => bill.OutstandingAmount);
        decimal overdue = bills
            .Where(bill => bill.DueDate < asAt)
            .Sum(bill => bill.OutstandingAmount);

        decimal? available = party.CreditLimit is { } limit ? limit - outstanding : null;

        return Result.Success(new CreditStatus(
            party.Id.Value,
            party.Code,
            party.Name,
            party.Currency.Code,
            party.CreditLimit,
            party.CreditDays,
            outstanding,
            overdue,
            available,
            // A party with no limit is never over it. That is not a technicality: "no
            // limit agreed" and "limit of nothing" are different arrangements, and
            // reporting the first as a breach would put every cash customer on a
            // management report.
            available is < 0m));
    }
}
