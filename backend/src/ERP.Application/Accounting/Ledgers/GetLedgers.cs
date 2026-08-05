using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Accounting.Ledgers;

/// <summary>Lists the ledgers available for posting.</summary>
/// <param name="ActiveOnly">Whether to exclude deactivated ledgers.</param>
/// <remarks>
/// Unpaged. A firm's chart of accounts is a few hundred rows at most, and every
/// caller is a lookup that filters client-side as the user types - paging it would
/// make the picker feel broken while adding a round trip per keystroke.
/// </remarks>
public sealed record GetLedgersQuery(bool ActiveOnly = true)
    : IQuery<IReadOnlyList<LedgerSummary>>;

/// <summary>A ledger as shown in a lookup.</summary>
/// <param name="LedgerId">The ledger.</param>
/// <param name="Code">The ledger code.</param>
/// <param name="Name">The ledger name.</param>
/// <param name="Kind">What the ledger represents.</param>
/// <param name="GroupCode">The account group's code.</param>
/// <param name="GroupName">The account group's name.</param>
/// <param name="Nature">Which side of the books the group sits on.</param>
/// <param name="Currency">The ledger's denominating currency.</param>
/// <param name="IsBillWise">Whether bills against it are settled individually.</param>
public sealed record LedgerSummary(
    Guid LedgerId,
    string Code,
    string Name,
    LedgerKind Kind,
    string GroupCode,
    string GroupName,
    AccountNature Nature,
    string Currency,
    bool IsBillWise);

/// <summary>Handles <see cref="GetLedgersQuery"/>.</summary>
public sealed class GetLedgersQueryHandler
    : IQueryHandler<GetLedgersQuery, IReadOnlyList<LedgerSummary>>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetLedgersQueryHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetLedgersQueryHandler(ILedgerRepository ledgers, ITenantContext tenantContext)
    {
        _ledgers = ledgers;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LedgerSummary>>> Handle(
        GetLedgersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<LedgerSummary>>(Error.Forbidden(
                "Ledger.NoFirmSelected", "A firm must be selected to list ledgers."));
        }

        IReadOnlyList<(Ledger Ledger, AccountGroup Group)> rows =
            await _ledgers.ListWithGroupAsync(firmId, request.ActiveOnly, cancellationToken);

        IReadOnlyList<LedgerSummary> summaries =
        [
            .. rows.Select(row => new LedgerSummary(
                row.Ledger.Id.Value,
                row.Ledger.Code,
                row.Ledger.Name,
                row.Ledger.Kind,
                row.Group.Code,
                row.Group.Name,
                row.Group.Nature,
                row.Ledger.Currency.Code,
                row.Ledger.IsBillWise)),
        ];

        return Result.Success(summaries);
    }
}
