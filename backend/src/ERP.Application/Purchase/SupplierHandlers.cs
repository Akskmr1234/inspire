using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Purchase;

/// <summary>Handles <see cref="CreateSupplierCommand"/>.</summary>
/// <remarks>
/// A supplier is created as a sub-ledger under the firm's creditors group, which is what
/// makes a purchase billable by them and what puts them in the creditors report without
/// anything else being wired up.
/// </remarks>
public sealed class CreateSupplierCommandHandler
    : ICommandHandler<CreateSupplierCommand, SupplierResponse>
{
    /// <summary>The group a supplier lands in when the caller names none.</summary>
    /// <remarks>
    /// The code the standard chart seeds for Sundry Creditors. Read as a convenience at
    /// creation time and nowhere else, for the reason the customer master reads the
    /// debtors code that way: a supplier filed under the wrong group is visible
    /// immediately and moved by editing them, where a posting made to the wrong account is
    /// found at a reconciliation months later.
    /// </remarks>
    private const string DefaultGroupCode = "2200";

    private readonly ILedgerRepository _ledgers;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreateSupplierCommandHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateSupplierCommandHandler(
        ILedgerRepository ledgers,
        IFirmRepository firms,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _ledgers = ledgers;
        _firms = firms;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<SupplierResponse>> Handle(
        CreateSupplierCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<SupplierResponse>(Error.Forbidden(
                "Supplier.NoFirmSelected",
                "A firm must be selected before creating a supplier."));
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<SupplierResponse>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        string code = request.Code.Trim().ToUpperInvariant();

        // Checked before the group is resolved, so the commonest mistake - re-entering a
        // supplier somebody has already created - is reported as itself rather than as
        // whatever the database says about a unique index.
        if (await _ledgers.IsCodeInUseAsync(firmId, code, cancellationToken))
        {
            return Result.Failure<SupplierResponse>(Error.Conflict(
                "Supplier.CodeInUse", $"'{code}' is already used by another account."));
        }

        Result<AccountGroup> group = await ResolveGroupAsync(
            request.AccountGroupId, firmId, cancellationToken);

        if (group.IsFailure)
        {
            return Result.Failure<SupplierResponse>(group.Error);
        }

        Result<Ledger> created = Ledger.Create(
            group.Value, code, request.Name, LedgerKind.Supplier, firm.BaseCurrency);

        if (created.IsFailure)
        {
            return Result.Failure<SupplierResponse>(created.Error);
        }

        Ledger supplier = created.Value;

        Result applied = SupplierDetails.Apply(
            supplier, request.NameArabic, request.Contact, request.Terms, request.TaxDetails);

        if (applied.IsFailure)
        {
            return Result.Failure<SupplierResponse>(applied.Error);
        }

        // A supplier who was already owed something when the books were taken on is owed
        // it on the credit side: a payable is a liability. The one line that is the
        // mirror image of the customer master rather than a copy of it.
        Result opening = supplier.SetOpeningBalance(request.OpeningBalance, EntrySide.Credit);

        if (opening.IsFailure)
        {
            return Result.Failure<SupplierResponse>(opening.Error);
        }

        _ledgers.Add(supplier);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SupplierDetails.Describe(supplier));
    }

    /// <summary>Finds the group the supplier will report under.</summary>
    private async Task<Result<AccountGroup>> ResolveGroupAsync(
        Guid? accountGroupId,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        if (accountGroupId is { } id)
        {
            AccountGroup? named = await _ledgers.FindGroupAsync(
                AccountGroupId.From(id), cancellationToken);

            return named is null || named.FirmId != firmId
                ? Result.Failure<AccountGroup>(Error.NotFound(
                    "Supplier.GroupNotFound",
                    "That account group is not in the selected firm."))
                : Result.Success(named);
        }

        AccountGroup? creditors = await _ledgers.FindGroupByCodeAsync(
            firmId, DefaultGroupCode, cancellationToken);

        return creditors is null
            ? Result.Failure<AccountGroup>(Error.BusinessRule(
                "Supplier.NoCreditorsGroup",
                "This firm has no Sundry Creditors group, so a supplier has nowhere to "
                + "report. Name the account group to create them under."))
            : Result.Success(creditors);
    }
}

/// <summary>Handles <see cref="UpdateSupplierCommand"/>.</summary>
public sealed class UpdateSupplierCommandHandler
    : ICommandHandler<UpdateSupplierCommand, SupplierResponse>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="UpdateSupplierCommandHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public UpdateSupplierCommandHandler(
        ILedgerRepository ledgers,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _ledgers = ledgers;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<SupplierResponse>> Handle(
        UpdateSupplierCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Ledger> found = await SupplierDetails.ResolveAsync(
            _ledgers, _tenantContext, request.SupplierId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<SupplierResponse>(found.Error);
        }

        Result applied = SupplierDetails.Apply(
            found.Value, request.NameArabic, request.Contact, request.Terms,
            request.TaxDetails, request.Name);

        if (applied.IsFailure)
        {
            return Result.Failure<SupplierResponse>(applied.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SupplierDetails.Describe(found.Value));
    }
}

/// <summary>Handles <see cref="SetSupplierActiveCommand"/>.</summary>
public sealed class SetSupplierActiveCommandHandler
    : ICommandHandler<SetSupplierActiveCommand, SupplierResponse>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SetSupplierActiveCommandHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SetSupplierActiveCommandHandler(
        ILedgerRepository ledgers,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _ledgers = ledgers;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<SupplierResponse>> Handle(
        SetSupplierActiveCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Ledger> found = await SupplierDetails.ResolveAsync(
            _ledgers, _tenantContext, request.SupplierId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<SupplierResponse>(found.Error);
        }

        if (request.IsActive)
        {
            found.Value.Activate();
        }
        else
        {
            found.Value.Deactivate();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SupplierDetails.Describe(found.Value));
    }
}

/// <summary>Handles <see cref="GetSuppliersQuery"/>.</summary>
public sealed class GetSuppliersQueryHandler
    : IQueryHandler<GetSuppliersQuery, IReadOnlyList<SupplierResponse>>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetSuppliersQueryHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetSuppliersQueryHandler(ILedgerRepository ledgers, ITenantContext tenantContext)
    {
        _ledgers = ledgers;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SupplierResponse>>> Handle(
        GetSuppliersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<SupplierResponse>>(Error.Forbidden(
                "Supplier.NoFirmSelected", "A firm must be selected to list suppliers."));
        }

        IReadOnlyList<Ledger> found = await _ledgers.ListByKindAsync(
            firmId, LedgerKind.Supplier, request.Search, request.ActiveOnly, cancellationToken);

        return Result.Success<IReadOnlyList<SupplierResponse>>(
            [.. found.Select(SupplierDetails.Describe)]);
    }
}

/// <summary>Handles <see cref="GetSupplierQuery"/>.</summary>
public sealed class GetSupplierQueryHandler : IQueryHandler<GetSupplierQuery, SupplierResponse>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetSupplierQueryHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetSupplierQueryHandler(ILedgerRepository ledgers, ITenantContext tenantContext)
    {
        _ledgers = ledgers;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<SupplierResponse>> Handle(
        GetSupplierQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Ledger> found = await SupplierDetails.ResolveAsync(
            _ledgers, _tenantContext, request.SupplierId, cancellationToken);

        return found.IsFailure
            ? Result.Failure<SupplierResponse>(found.Error)
            : Result.Success(SupplierDetails.Describe(found.Value));
    }
}

/// <summary>The reading, writing and describing the supplier handlers share.</summary>
internal static class SupplierDetails
{
    /// <summary>Finds a supplier, refusing a ledger that is not one.</summary>
    /// <remarks>
    /// The kind is checked as well as the firm. A bank account reached through this
    /// endpoint would otherwise be given credit terms and would then appear in a supplier
    /// picker.
    /// </remarks>
    internal static async Task<Result<Ledger>> ResolveAsync(
        ILedgerRepository ledgers,
        ITenantContext tenantContext,
        Guid supplierId,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Ledger>(Error.Forbidden(
                "Supplier.NoFirmSelected", "A firm must be selected to work with suppliers."));
        }

        Ledger? ledger = await ledgers.FindAsync(LedgerId.From(supplierId), cancellationToken);

        return ledger is null || ledger.FirmId != firmId || ledger.Kind != LedgerKind.Supplier
            ? Result.Failure<Ledger>(Error.NotFound(
                "Supplier.NotFound", "That supplier does not exist in the selected firm."))
            : Result.Success(ledger);
    }

    /// <summary>Applies whichever blocks the caller supplied, leaving the rest alone.</summary>
    /// <remarks>
    /// A block that was not sent is not cleared, for the reason the customer master leaves
    /// them alone: changing only an address should not silently drop the terms somebody
    /// agreed.
    /// </remarks>
    internal static Result Apply(
        Ledger supplier,
        string? nameArabic,
        SupplierContact? contact,
        SupplierTerms? terms,
        SupplierTaxDetails? taxDetails,
        string? name = null)
    {
        if (name is not null)
        {
            Result renamed = supplier.Rename(name);

            if (renamed.IsFailure)
            {
                return renamed;
            }
        }

        if (nameArabic is not null)
        {
            supplier.SetArabicName(nameArabic);
        }

        if (contact is not null)
        {
            supplier.SetContactDetails(
                contact.Phone, contact.MobileNumber, contact.Email,
                contact.AddressLine1, contact.AddressLine2);
        }

        if (taxDetails is not null)
        {
            supplier.SetTaxDetails(taxDetails.RegistrationNumber, taxDetails.StateCode);
        }

        if (terms is null)
        {
            return Result.Success();
        }

        supplier.SetBillWise(terms.IsBillWise);

        return supplier.SetCreditTerms(terms.CreditLimit, terms.CreditDays);
    }

    /// <summary>Describes a supplier as the API reports them.</summary>
    internal static SupplierResponse Describe(Ledger supplier) =>
        new(
            supplier.Id.Value,
            supplier.Code,
            supplier.Name,
            supplier.NameArabic,
            new SupplierContact(
                supplier.MobileNumber,
                supplier.Phone,
                supplier.Email,
                supplier.AddressLine1,
                supplier.AddressLine2),
            new SupplierTerms(supplier.CreditLimit, supplier.CreditDays, supplier.IsBillWise),
            new SupplierTaxDetails(supplier.TaxRegistrationNumber, supplier.StateCode),
            supplier.Currency.Code,
            supplier.OpeningBalance,
            supplier.IsActive);
}
