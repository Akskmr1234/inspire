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
                .Select(charge => Describe(charge, ledgers[charge.LedgerId])),
        ]);
    }

    /// <summary>Describes one charge as the entry screens read it.</summary>
    /// <param name="charge">The mapping.</param>
    /// <param name="ledger">The account it posts to.</param>
    /// <returns>The charge.</returns>
    internal static ChargeSetup Describe(AdditionalLedger charge, Ledger ledger) =>
        new(
            ledger.Id.Value,
            ledger.Code,
            ledger.Name,
            charge.IsAddition,
            charge.IsDefault,
            charge.AppliesUnderTax,
            charge.AppliesUnderNonTax,
            charge.AppliesUnderCst,
            string.Equals(ledger.Code, RoundOffCode, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Sets a ledger up as a charge one kind of document may carry.</summary>
/// <param name="Document">The kind of document.</param>
/// <param name="LedgerId">The account the charge posts to.</param>
/// <param name="IsAddition">Whether it adds to the document's total; otherwise it deducts.</param>
/// <remarks>
/// What lets somebody entering an invoice use a ledger nobody set up as a charge yet -
/// a cleaning charge, a special discount - without leaving the invoice. A ledger already
/// set up for that document keeps its own direction, so a second person picking it does
/// not quietly turn every later invoice's freight into a deduction.
/// </remarks>
public sealed record MapChargeCommand(ChargeableDocument Document, Guid LedgerId, bool IsAddition)
    : ICommand<ChargeSetup>, ITransactional;

/// <summary>Handles <see cref="MapChargeCommand"/>.</summary>
public sealed class MapChargeCommandHandler : ICommandHandler<MapChargeCommand, ChargeSetup>
{
    private readonly IAdditionalLedgerRepository _charges;
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="MapChargeCommandHandler"/> class.</summary>
    /// <param name="charges">The charge setup.</param>
    /// <param name="ledgers">The ledgers the charges post to.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public MapChargeCommandHandler(
        IAdditionalLedgerRepository charges,
        ILedgerRepository ledgers,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _charges = charges;
        _ledgers = ledgers;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<ChargeSetup>> Handle(
        MapChargeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<ChargeSetup>(Error.Forbidden(
                "Charges.NoFirmSelected", "A firm must be selected to set up its charges."));
        }

        Ledger? ledger = await _ledgers.FindAsync(LedgerId.From(request.LedgerId), cancellationToken);

        if (ledger is null || ledger.FirmId != firmId)
        {
            return Result.Failure<ChargeSetup>(Error.NotFound(
                "Charges.LedgerNotFound", "That ledger is not in the selected firm."));
        }

        // A party, a cash or bank account or a tax head already has its own place on a
        // document; as a charge it would post the same money twice over.
        if (ledger.Kind is not (LedgerKind.General or LedgerKind.AdditionalCharge))
        {
            return Result.Failure<ChargeSetup>(Error.Validation(
                "Charges.LedgerNotChargeable",
                $"'{ledger.Name}' is a {ledger.Kind} account and cannot be used as an additional ledger."));
        }

        IReadOnlyList<AdditionalLedger> mapped = await _charges.ListForDocumentAsync(
            firmId, request.Document, cancellationToken);

        AdditionalLedger? existing = mapped.FirstOrDefault(charge => charge.LedgerId == ledger.Id);

        if (existing is not null)
        {
            existing.Restore();
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ListChargesQueryHandler.Describe(existing, ledger));
        }

        int order = mapped.Count == 0 ? 1 : mapped.Max(charge => charge.DisplayOrder) + 1;

        Result<AdditionalLedger> created = AdditionalLedger.Map(
            _tenantContext.TenantId, firmId, request.Document, ledger, request.IsAddition, order);

        if (created.IsFailure)
        {
            return Result.Failure<ChargeSetup>(created.Error);
        }

        _charges.Add(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ListChargesQueryHandler.Describe(created.Value, ledger));
    }
}

/// <summary>Takes a ledger off the charges one kind of document carries.</summary>
/// <param name="Document">The kind of document.</param>
/// <param name="LedgerId">The account the charge posts to.</param>
/// <remarks>
/// Withdrawn rather than deleted: documents already carrying it still name it, and
/// setting it up again restores it with the direction it had.
/// </remarks>
public sealed record WithdrawChargeCommand(ChargeableDocument Document, Guid LedgerId)
    : ICommand, ITransactional;

/// <summary>Handles <see cref="WithdrawChargeCommand"/>.</summary>
public sealed class WithdrawChargeCommandHandler : ICommandHandler<WithdrawChargeCommand>
{
    private readonly IAdditionalLedgerRepository _charges;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="WithdrawChargeCommandHandler"/> class.</summary>
    /// <param name="charges">The charge setup.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public WithdrawChargeCommandHandler(
        IAdditionalLedgerRepository charges,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _charges = charges;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(WithdrawChargeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure(Error.Forbidden(
                "Charges.NoFirmSelected", "A firm must be selected to set up its charges."));
        }

        IReadOnlyList<AdditionalLedger> mapped = await _charges.ListForDocumentAsync(
            firmId, request.Document, cancellationToken);

        AdditionalLedger? charge = mapped.FirstOrDefault(
            candidate => candidate.LedgerId == LedgerId.From(request.LedgerId));

        if (charge is null)
        {
            return Result.Failure(Error.NotFound(
                "Charges.NotSetUp", "That ledger is not set up for this kind of document."));
        }

        charge.Withdraw();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
