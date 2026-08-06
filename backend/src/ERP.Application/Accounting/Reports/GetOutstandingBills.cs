using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>
/// Lists what is still owed, bill by bill: the debtors report for receivables and
/// the creditors report for payables.
/// </summary>
/// <param name="Type">
/// <see cref="BillType.Receivable"/> for debtors, <see cref="BillType.Payable"/>
/// for creditors.
/// </param>
/// <param name="AsAt">
/// The date the position is stated as at. Bills raised later are excluded, and
/// settlements made later are ignored - so a report run for the end of last month
/// says what was outstanding then, not what is outstanding now.
/// </param>
/// <param name="LedgerId">Restricts the report to one party. Omit for all of them.</param>
/// <param name="OverdueOnly">
/// Restricts the report to bills already past their due date - the specification's
/// "Over Due Sales Invoice" and its purchase equivalent.
/// </param>
/// <remarks>
/// Debtors and creditors are the same report over the opposite kind of bill, so
/// they are one query rather than two that would drift apart. They are exposed as
/// separate endpoints because that is how they are asked for.
/// </remarks>
public sealed record GetOutstandingBillsQuery(
    BillType Type,
    DateOnly AsAt,
    Guid? LedgerId = null,
    bool OverdueOnly = false) : IQuery<OutstandingBillsResponse>;

/// <summary>One bill still outstanding.</summary>
/// <param name="BillId">The bill, so the client can drill through to it.</param>
/// <param name="BillNumber">The reference the party knows it by.</param>
/// <param name="BillDate">The date it was raised.</param>
/// <param name="DueDate">The date payment falls due.</param>
/// <param name="OriginalAmount">The amount it was raised for.</param>
/// <param name="SettledAmount">How much had been allocated by the reporting date.</param>
/// <param name="OutstandingAmount">What remained owing on that date.</param>
/// <param name="DaysOverdue">Days past the due date, zero when not yet due.</param>
/// <param name="Currency">The currency the bill is denominated in.</param>
public sealed record OutstandingBill(
    Guid BillId,
    string BillNumber,
    DateOnly BillDate,
    DateOnly DueDate,
    decimal OriginalAmount,
    decimal SettledAmount,
    decimal OutstandingAmount,
    int DaysOverdue,
    string Currency);

/// <summary>One party's outstanding position.</summary>
/// <param name="LedgerId">The party.</param>
/// <param name="LedgerCode">The party's ledger code.</param>
/// <param name="LedgerName">The party's name.</param>
/// <param name="TotalOutstanding">Everything they still owe, or are still owed.</param>
/// <param name="TotalOverdue">The part of that already past its due date.</param>
/// <param name="Bills">Their open bills, oldest due date first.</param>
public sealed record OutstandingParty(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    decimal TotalOutstanding,
    decimal TotalOverdue,
    IReadOnlyList<OutstandingBill> Bills);

/// <summary>A debtors or creditors report.</summary>
/// <param name="Type">Which of the two this is.</param>
/// <param name="AsAt">The date the position is stated as at.</param>
/// <param name="Currency">The firm's base currency.</param>
/// <param name="Parties">One section per party, in ledger-code order.</param>
/// <param name="TotalOutstanding">The total across every party.</param>
/// <param name="TotalOverdue">The overdue part of that total.</param>
/// <param name="Currencies">
/// Every currency appearing in the report. A total is only meaningful when this
/// holds a single entry; where a firm bills in more than one currency the caller
/// must present the sections rather than the sum.
/// </param>
/// <remarks>
/// Bills are stated in the currency they were raised in, because that is what the
/// party owes. The totals are therefore a straight sum, correct whenever the firm
/// bills in one currency and flagged by <see cref="Currencies"/> when it does not -
/// converting at today's rate would restate a historical debt, and converting at
/// the original rate would make the report disagree with the ledger.
/// </remarks>
public sealed record OutstandingBillsResponse(
    BillType Type,
    DateOnly AsAt,
    string Currency,
    IReadOnlyList<OutstandingParty> Parties,
    decimal TotalOutstanding,
    decimal TotalOverdue,
    IReadOnlyList<string> Currencies);

/// <summary>Validates a <see cref="GetOutstandingBillsQuery"/>.</summary>
public sealed class GetOutstandingBillsQueryValidator
    : AbstractValidator<GetOutstandingBillsQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetOutstandingBillsQueryValidator"/> class.</summary>
    public GetOutstandingBillsQueryValidator()
    {
        RuleFor(q => q.Type).IsInEnum();
        RuleFor(q => q.AsAt).NotEqual(default(DateOnly));
    }
}

/// <summary>Reads the open bills behind the outstanding and aging reports.</summary>
public interface IOutstandingBillsReader
{
    /// <summary>Reads the bills of a firm that were still open on a date.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="type">Receivable or payable.</param>
    /// <param name="asAt">The date the position is stated as at.</param>
    /// <param name="ledgerId">One party, or <see langword="null"/> for all of them.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The open bills with their party, in no particular order.</returns>
    Task<IReadOnlyList<OutstandingBillRow>> ReadAsync(
        FirmId firmId,
        BillType type,
        DateOnly asAt,
        LedgerId? ledgerId,
        CancellationToken cancellationToken = default);
}

/// <summary>One open bill and its party, before presentation.</summary>
/// <param name="BillId">The bill.</param>
/// <param name="LedgerId">The party.</param>
/// <param name="LedgerCode">The party's ledger code.</param>
/// <param name="LedgerName">The party's name.</param>
/// <param name="BillNumber">The bill reference.</param>
/// <param name="BillDate">The date it was raised.</param>
/// <param name="DueDate">The date payment falls due.</param>
/// <param name="OriginalAmount">The amount it was raised for.</param>
/// <param name="SettledAmount">
/// How much had been allocated as at the reporting date - the sum of the
/// allocations made on or before it, not the bill's settled amount today.
/// </param>
/// <param name="Currency">The currency the bill is denominated in.</param>
public sealed record OutstandingBillRow(
    Guid BillId,
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    string BillNumber,
    DateOnly BillDate,
    DateOnly DueDate,
    decimal OriginalAmount,
    decimal SettledAmount,
    string Currency)
{
    /// <summary>Gets what remained owing on the reporting date.</summary>
    public decimal OutstandingAmount => OriginalAmount - SettledAmount;
}

/// <summary>Handles <see cref="GetOutstandingBillsQuery"/>.</summary>
public sealed class GetOutstandingBillsQueryHandler
    : IQueryHandler<GetOutstandingBillsQuery, OutstandingBillsResponse>
{
    private readonly IOutstandingBillsReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetOutstandingBillsQueryHandler"/> class.</summary>
    /// <param name="reader">The outstanding-bills reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetOutstandingBillsQueryHandler(
        IOutstandingBillsReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<OutstandingBillsResponse>> Handle(
        GetOutstandingBillsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<OutstandingBillsResponse>(firm.Error);
        }

        IReadOnlyList<OutstandingBillRow> rows = await _reader.ReadAsync(
            firm.Value.Id,
            request.Type,
            request.AsAt,
            request.LedgerId is { } id ? LedgerId.From(id) : null,
            cancellationToken);

        List<OutstandingParty> parties = [];

        decimal totalOutstanding = 0m;
        decimal totalOverdue = 0m;
        HashSet<string> currencies = new(StringComparer.Ordinal);

        foreach (IGrouping<Guid, OutstandingBillRow> group in rows
            .GroupBy(row => row.LedgerId)
            .OrderBy(group => group.First().LedgerCode, StringComparer.Ordinal))
        {
            List<OutstandingBill> bills = [];

            decimal partyOutstanding = 0m;
            decimal partyOverdue = 0m;

            // Oldest due date first. An outstanding report is read to decide who to
            // chase, and the bill that has been owed longest is the one that matters.
            foreach (OutstandingBillRow row in group
                .OrderBy(r => r.DueDate)
                .ThenBy(r => r.BillNumber, StringComparer.Ordinal))
            {
                int daysOverdue = DaysOverdue(row.DueDate, request.AsAt);

                if (request.OverdueOnly && daysOverdue == 0)
                {
                    continue;
                }

                bills.Add(new OutstandingBill(
                    row.BillId,
                    row.BillNumber,
                    row.BillDate,
                    row.DueDate,
                    row.OriginalAmount,
                    row.SettledAmount,
                    row.OutstandingAmount,
                    daysOverdue,
                    row.Currency));

                partyOutstanding += row.OutstandingAmount;
                currencies.Add(row.Currency);

                if (daysOverdue > 0)
                {
                    partyOverdue += row.OutstandingAmount;
                }
            }

            if (bills.Count == 0)
            {
                continue;
            }

            OutstandingBillRow first = group.First();

            parties.Add(new OutstandingParty(
                first.LedgerId,
                first.LedgerCode,
                first.LedgerName,
                partyOutstanding,
                partyOverdue,
                bills));

            totalOutstanding += partyOutstanding;
            totalOverdue += partyOverdue;
        }

        return Result.Success(new OutstandingBillsResponse(
            request.Type,
            request.AsAt,
            firm.Value.BaseCurrency.Code,
            parties,
            totalOutstanding,
            totalOverdue,
            [.. currencies.Order(StringComparer.Ordinal)]));
    }

    /// <summary>Counts days past the due date, never negative.</summary>
    /// <param name="dueDate">The date payment fell due.</param>
    /// <param name="asAt">The reporting date.</param>
    /// <returns>The days overdue, or zero when not yet due.</returns>
    /// <remarks>
    /// Counted from the due date, never the bill date - the same rule the
    /// <see cref="Bill"/> aggregate applies. Aging from when the invoice was raised
    /// would report every bill as overdue the moment it is issued.
    /// </remarks>
    internal static int DaysOverdue(DateOnly dueDate, DateOnly asAt)
    {
        int days = asAt.DayNumber - dueDate.DayNumber;

        return days > 0 ? days : 0;
    }
}
