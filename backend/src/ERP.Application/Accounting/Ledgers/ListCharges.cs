using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Accounting.Ledgers;

/// <summary>One charge a kind of document may carry, as the firm set it up.</summary>
/// <param name="LedgerId">The account it posts to.</param>
/// <param name="Code">That account's code.</param>
/// <param name="Name">That account's name.</param>
/// <param name="IsAddition">Whether it adds to the document's total; otherwise it deducts.</param>
/// <param name="IsDefault">Whether new documents start with it.</param>
/// <param name="AppliesUnderTax">Whether a tax document may carry it.</param>
/// <param name="AppliesUnderNonTax">Whether a non-tax document may carry it.</param>
/// <param name="AppliesUnderCst">Whether a CST document may carry it.</param>
/// <param name="IsComputed">
/// Whether the document works it out itself - round off - so nobody types an amount.
/// </param>
public sealed record ChargeSetup(
    Guid LedgerId,
    string Code,
    string Name,
    bool IsAddition,
    bool IsDefault,
    bool AppliesUnderTax,
    bool AppliesUnderNonTax,
    bool AppliesUnderCst,
    bool IsComputed);

/// <summary>Lists the charges one kind of document may carry, and which way each moves it.</summary>
/// <param name="Document">The kind of document.</param>
/// <remarks>
/// What the entry screens add up charges with. They used to guess the direction from the
/// account's code, or let somebody choose it, and the server - which decides it from this
/// setup - posted something else: freight marked "deduct" on the screen was added on the
/// invoice.
/// </remarks>
public sealed record ListChargesQuery(ChargeableDocument Document) : IQuery<IReadOnlyList<ChargeSetup>>;

/// <summary>Handles <see cref="ListChargesQuery"/>.</summary>
public sealed class ListChargesQueryHandler : IQueryHandler<ListChargesQuery, IReadOnlyList<ChargeSetup>>
{
    /// <summary>The code of the charge every document computes for itself.</summary>
    public const string RoundOffCode = "ROUND-OFF";

    private readonly IAdditionalLedgerRepository _charges;
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ListChargesQueryHandler"/> class.</summary>
    /// <param name="charges">The charge setup.</param>
    /// <param name="ledgers">The ledgers the charges post to.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public ListChargesQueryHandler(
        IAdditionalLedgerRepository charges,
        ILedgerRepository ledgers,
        ITenantContext tenantContext)
    {
        _charges = charges;
        _ledgers = ledgers;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ChargeSetup>>> Handle(
        ListChargesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<ChargeSetup>>(Error.Forbidden(
                "Charges.NoFirmSelected", "A firm must be selected to read its charges."));
        }

        IReadOnlyList<AdditionalLedger> mapped = await _charges.ListForDocumentAsync(
            firmId, request.Document, cancellationToken);

        IReadOnlyDictionary<LedgerId, Ledger> ledgers = await _ledgers.GetManyAsync(
            [.. mapped.Select(charge => charge.LedgerId).Distinct()], cancellationToken);

        return Result.Success<IReadOnlyList<ChargeSetup>>(
        [
            .. mapped
                .Where(charge => charge.IsActive && ledgers.ContainsKey(charge.LedgerId))
                .Select(charge =>
                {
                    Ledger ledger = ledgers[charge.LedgerId];

                    return new ChargeSetup(
                        ledger.Id.Value,
                        ledger.Code,
                        ledger.Name,
                        charge.IsAddition,
                        charge.IsDefault,
                        charge.AppliesUnderTax,
                        charge.AppliesUnderNonTax,
                        charge.AppliesUnderCst,
                        string.Equals(ledger.Code, RoundOffCode, StringComparison.OrdinalIgnoreCase));
                }),
        ]);
    }
}
