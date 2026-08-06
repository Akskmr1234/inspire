using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>
/// Ages what is owed into buckets: the age-wise debtors and creditors reports.
/// </summary>
/// <param name="Type">
/// <see cref="BillType.Receivable"/> for debtors, <see cref="BillType.Payable"/>
/// for creditors.
/// </param>
/// <param name="AsAt">
/// The date the position is aged as at. Bills raised later are excluded and
/// settlements made later are ignored.
/// </param>
/// <param name="BucketDays">
/// The upper bound of each bucket, in days overdue, ascending. Omitted,
/// <see cref="AgingBuckets.Default"/> applies. The final bucket is always open-ended.
/// </param>
/// <param name="LedgerId">Restricts the report to one party. Omit for all of them.</param>
/// <remarks>
/// The buckets are a parameter rather than a constant. The specification records
/// 0-30/31-60/61-90/90+ as an assumption rather than a stated requirement, and a
/// firm that ages on fortnights should not need a deployment to say so.
/// </remarks>
public sealed record GetAgingAnalysisQuery(
    BillType Type,
    DateOnly AsAt,
    IReadOnlyList<int>? BucketDays = null,
    Guid? LedgerId = null) : IQuery<AgingAnalysisResponse>;

/// <summary>The bucket boundaries an aging report is cut on.</summary>
public static class AgingBuckets
{
    /// <summary>
    /// The default boundaries: 30, 60, and 90 days, giving 1-30, 31-60, 61-90, and
    /// over 90.
    /// </summary>
    /// <remarks>
    /// Recorded in the specification as an assumption, not a stated requirement.
    /// It is the conventional cut and the one the reference reports imply, but it
    /// is open question 3 until the business confirms it.
    /// </remarks>
    public static IReadOnlyList<int> Default { get; } = [30, 60, 90];

    /// <summary>The most buckets a single report will cut.</summary>
    /// <remarks>
    /// A guard rather than a business rule. The boundaries arrive from the caller,
    /// and a report a thousand columns wide helps nobody.
    /// </remarks>
    public const int Maximum = 12;
}

/// <summary>One party's aged position.</summary>
/// <param name="LedgerId">The party.</param>
/// <param name="LedgerCode">The party's ledger code.</param>
/// <param name="LedgerName">The party's name.</param>
/// <param name="NotDue">
/// The part not yet due. Kept out of the first overdue bucket deliberately: an
/// aging report exists to separate what is late from what is merely owed, and
/// folding the two together defeats the point of running it.
/// </param>
/// <param name="Buckets">
/// The overdue amounts, in the same order as
/// <see cref="AgingAnalysisResponse.BucketLabels"/>.
/// </param>
/// <param name="Total">Everything outstanding, due or not.</param>
public sealed record AgingRow(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    decimal NotDue,
    IReadOnlyList<decimal> Buckets,
    decimal Total);

/// <summary>An age-wise debtors or creditors report.</summary>
/// <param name="Type">Which of the two this is.</param>
/// <param name="AsAt">The date the position is aged as at.</param>
/// <param name="Currency">The firm's base currency.</param>
/// <param name="BucketLabels">The bucket headings, in column order.</param>
/// <param name="Rows">One row per party, in ledger-code order.</param>
/// <param name="TotalNotDue">The not-yet-due column's total.</param>
/// <param name="BucketTotals">Each bucket's total, in column order.</param>
/// <param name="Total">The grand total.</param>
/// <param name="Currencies">
/// Every currency appearing in the report, for the same reason as on the
/// outstanding report: a total means something only when there is one of them.
/// </param>
public sealed record AgingAnalysisResponse(
    BillType Type,
    DateOnly AsAt,
    string Currency,
    IReadOnlyList<string> BucketLabels,
    IReadOnlyList<AgingRow> Rows,
    decimal TotalNotDue,
    IReadOnlyList<decimal> BucketTotals,
    decimal Total,
    IReadOnlyList<string> Currencies);

/// <summary>Validates a <see cref="GetAgingAnalysisQuery"/>.</summary>
public sealed class GetAgingAnalysisQueryValidator : AbstractValidator<GetAgingAnalysisQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetAgingAnalysisQueryValidator"/> class.</summary>
    public GetAgingAnalysisQueryValidator()
    {
        RuleFor(q => q.Type).IsInEnum();
        RuleFor(q => q.AsAt).NotEqual(default(DateOnly));

        RuleFor(q => q.BucketDays!)
            .Must(days => days.Count is > 0 and <= AgingBuckets.Maximum)
            .WithMessage($"An aging report needs between 1 and {AgingBuckets.Maximum} buckets.")
            .Must(days => days.All(d => d > 0))
            .WithMessage("Every bucket boundary must be a positive number of days.")
            // Ascending and distinct, or the buckets would overlap and the same bill
            // would be counted in two of them - making the row totals disagree with
            // the outstanding report they are meant to break down.
            .Must(days => days.Zip(days.Skip(1)).All(pair => pair.Second > pair.First))
            .WithMessage("Bucket boundaries must ascend, with no repeats.")
            .When(q => q.BucketDays is not null);
    }
}

/// <summary>Handles <see cref="GetAgingAnalysisQuery"/>.</summary>
/// <remarks>
/// Reads through <see cref="IOutstandingBillsReader"/> rather than querying for
/// itself. The aging report is the outstanding report cut into columns, and one
/// reader means the two cannot disagree about what is outstanding - which they
/// would eventually, given they are read side by side.
/// </remarks>
public sealed class GetAgingAnalysisQueryHandler
    : IQueryHandler<GetAgingAnalysisQuery, AgingAnalysisResponse>
{
    private readonly IOutstandingBillsReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetAgingAnalysisQueryHandler"/> class.</summary>
    /// <param name="reader">The outstanding-bills reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetAgingAnalysisQueryHandler(
        IOutstandingBillsReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<AgingAnalysisResponse>> Handle(
        GetAgingAnalysisQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<AgingAnalysisResponse>(firm.Error);
        }

        // An empty list is treated as "unspecified" rather than "no buckets". The
        // validator rejects it on the way in; this keeps a handler called directly
        // from indexing off the end of it.
        IReadOnlyList<int> boundaries = request.BucketDays is { Count: > 0 } supplied
            ? supplied
            : AgingBuckets.Default;

        IReadOnlyList<string> labels = LabelsFor(boundaries);

        IReadOnlyList<OutstandingBillRow> rows = await _reader.ReadAsync(
            firm.Value.Id,
            request.Type,
            request.AsAt,
            request.LedgerId is { } id ? LedgerId.From(id) : null,
            cancellationToken);

        List<AgingRow> aged = [];

        decimal totalNotDue = 0m;
        decimal[] bucketTotals = new decimal[boundaries.Count + 1];
        HashSet<string> currencies = new(StringComparer.Ordinal);

        foreach (IGrouping<Guid, OutstandingBillRow> group in rows
            .GroupBy(row => row.LedgerId)
            .OrderBy(group => group.First().LedgerCode, StringComparer.Ordinal))
        {
            decimal notDue = 0m;
            decimal[] buckets = new decimal[boundaries.Count + 1];

            foreach (OutstandingBillRow row in group)
            {
                int daysOverdue = GetOutstandingBillsQueryHandler.DaysOverdue(
                    row.DueDate, request.AsAt);

                currencies.Add(row.Currency);

                if (daysOverdue == 0)
                {
                    notDue += row.OutstandingAmount;
                    continue;
                }

                buckets[BucketFor(daysOverdue, boundaries)] += row.OutstandingAmount;
            }

            OutstandingBillRow first = group.First();
            decimal total = notDue + buckets.Sum();

            aged.Add(new AgingRow(
                first.LedgerId, first.LedgerCode, first.LedgerName, notDue, buckets, total));

            totalNotDue += notDue;

            for (int index = 0; index < buckets.Length; index++)
            {
                bucketTotals[index] += buckets[index];
            }
        }

        return Result.Success(new AgingAnalysisResponse(
            request.Type,
            request.AsAt,
            firm.Value.BaseCurrency.Code,
            labels,
            aged,
            totalNotDue,
            bucketTotals,
            totalNotDue + bucketTotals.Sum(),
            [.. currencies.Order(StringComparer.Ordinal)]));
    }

    /// <summary>Finds the bucket an overdue bill falls in.</summary>
    /// <param name="daysOverdue">Days past the due date. Always at least one.</param>
    /// <param name="boundaries">The bucket upper bounds, ascending.</param>
    /// <returns>The bucket index, the last one being open-ended.</returns>
    private static int BucketFor(int daysOverdue, IReadOnlyList<int> boundaries)
    {
        for (int index = 0; index < boundaries.Count; index++)
        {
            if (daysOverdue <= boundaries[index])
            {
                return index;
            }
        }

        return boundaries.Count;
    }

    /// <summary>Builds the column headings for a set of boundaries.</summary>
    /// <param name="boundaries">The bucket upper bounds, ascending.</param>
    /// <returns>The headings, in column order.</returns>
    /// <remarks>
    /// Built here rather than by the client so that every caller - a screen, an
    /// export, a printed layout - labels the same figures the same way. The ranges
    /// are inclusive on both ends and start at one day overdue, because a bill that
    /// is not overdue at all belongs in the not-due column instead.
    /// </remarks>
    private static List<string> LabelsFor(IReadOnlyList<int> boundaries)
    {
        List<string> labels = new(boundaries.Count + 1);

        int lower = 1;

        foreach (int upper in boundaries)
        {
            labels.Add($"{lower}-{upper} days");
            lower = upper + 1;
        }

        labels.Add($"Over {boundaries[^1]} days");

        return labels;
    }
}
