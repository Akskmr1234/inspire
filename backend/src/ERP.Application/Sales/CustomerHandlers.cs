using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Parties;
using ERP.Domain.Accounting;
using ERP.Domain.Parties;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Sales;

/// <summary>Handles <see cref="CreateCustomerCommand"/>.</summary>
/// <remarks>
/// A customer is created as a sub-ledger under the firm's debtors group, which is what
/// makes an invoice billable to them and what puts them in the debtors report without
/// anything else being wired up.
/// </remarks>
public sealed class CreateCustomerCommandHandler
    : ICommandHandler<CreateCustomerCommand, CustomerResponse>
{
    /// <summary>The group a customer lands in when the caller names none.</summary>
    /// <remarks>
    /// The code the standard chart seeds for Sundry Debtors. Read as a convenience at
    /// creation time and nowhere else - unlike the tax and stock accounts, which are on
    /// a per-firm map precisely so no posting ever depends on a code. The difference is
    /// what happens when it is wrong: a customer filed under the wrong group is visible
    /// immediately and moved by editing them, where a posting made to the wrong account
    /// is found at a reconciliation months later. A firm that has reshaped its chart
    /// passes the group explicitly.
    /// </remarks>
    private const string DefaultGroupCode = "1200";

    private readonly ILedgerRepository _ledgers;
    private readonly IFirmRepository _firms;
    private readonly IPlaceRepository _places;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMasterCodes _codes;

    /// <summary>Initialises a new instance of the <see cref="CreateCustomerCommandHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="places">The areas and routes a customer may be in.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="codes">Issues the code of a record entered without one.</param>
    public CreateCustomerCommandHandler(
        ILedgerRepository ledgers,
        IFirmRepository firms,
        IPlaceRepository places,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        IMasterCodes codes)
    {
        _ledgers = ledgers;
        _firms = firms;
        _places = places;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _codes = codes;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerResponse>> Handle(
        CreateCustomerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<CustomerResponse>(Error.Forbidden(
                "Customer.NoFirmSelected",
                "A firm must be selected before creating a customer."));
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<CustomerResponse>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        // Left blank, the next number in the customer sequence is issued: 10000 first.
        string code = string.IsNullOrWhiteSpace(request.Code)
            ? await _codes.NextAsync(
                firmId,
                CodedMaster.Customer,
                candidate => _ledgers.IsCodeInUseAsync(firmId, candidate, cancellationToken),
                cancellationToken)
            : request.Code.Trim().ToUpperInvariant();

        // Checked before the group is resolved, so the commonest mistake - re-entering a
        // customer somebody has already created - is reported as itself rather than as
        // whatever the database says about a unique index.
        if (await _ledgers.IsCodeInUseAsync(firmId, code, cancellationToken))
        {
            return Result.Failure<CustomerResponse>(Error.Conflict(
                "Customer.CodeInUse", $"'{code}' is already used by another account."));
        }

        Result<AccountGroup> group = await ResolveGroupAsync(
            request.AccountGroupId, firmId, cancellationToken);

        if (group.IsFailure)
        {
            return Result.Failure<CustomerResponse>(group.Error);
        }

        Result<Ledger> created = Ledger.Create(
            group.Value, code, request.Name, LedgerKind.Customer, firm.BaseCurrency);

        if (created.IsFailure)
        {
            return Result.Failure<CustomerResponse>(created.Error);
        }

        Ledger customer = created.Value;

        Result applied = CustomerDetails.Apply(
            customer, request.NameArabic, request.Contact, request.Terms, request.TaxDetails);

        if (applied.IsFailure)
        {
            return Result.Failure<CustomerResponse>(applied.Error);
        }

        Result profiled = await CustomerDetails.ApplyProfileAsync(
            _places, customer, request.Profile, cancellationToken);

        if (profiled.IsFailure)
        {
            return Result.Failure<CustomerResponse>(profiled.Error);
        }

        // A customer who already owed something when the books were taken on owes it on
        // the debit side: a receivable is an asset.
        Result opening = customer.SetOpeningBalance(request.OpeningBalance, EntrySide.Debit);

        if (opening.IsFailure)
        {
            return Result.Failure<CustomerResponse>(opening.Error);
        }

        _ledgers.Add(customer);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CustomerDetails.Describe(customer));
    }

    /// <summary>Finds the group the customer will report under.</summary>
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
                    "Customer.GroupNotFound",
                    "That account group is not in the selected firm."))
                : Result.Success(named);
        }

        AccountGroup? debtors = await _ledgers.FindGroupByCodeAsync(
            firmId, DefaultGroupCode, cancellationToken);

        return debtors is null
            ? Result.Failure<AccountGroup>(Error.BusinessRule(
                "Customer.NoDebtorsGroup",
                "This firm has no Sundry Debtors group, so a customer has nowhere to "
                + "report. Name the account group to create them under."))
            : Result.Success(debtors);
    }
}

/// <summary>Handles <see cref="UpdateCustomerCommand"/>.</summary>
public sealed class UpdateCustomerCommandHandler
    : ICommandHandler<UpdateCustomerCommand, CustomerResponse>
{
    private readonly ILedgerRepository _ledgers;
    private readonly IPlaceRepository _places;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="UpdateCustomerCommandHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="places">The areas and routes a customer may be in.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public UpdateCustomerCommandHandler(
        ILedgerRepository ledgers,
        IPlaceRepository places,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _ledgers = ledgers;
        _places = places;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerResponse>> Handle(
        UpdateCustomerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Ledger> found = await CustomerDetails.ResolveAsync(
            _ledgers, _tenantContext, request.CustomerId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<CustomerResponse>(found.Error);
        }

        Result applied = CustomerDetails.Apply(
            found.Value, request.NameArabic, request.Contact, request.Terms,
            request.TaxDetails, request.Name);

        if (applied.IsFailure)
        {
            return Result.Failure<CustomerResponse>(applied.Error);
        }

        Result profiled = await CustomerDetails.ApplyProfileAsync(
            _places, found.Value, request.Profile, cancellationToken);

        if (profiled.IsFailure)
        {
            return Result.Failure<CustomerResponse>(profiled.Error);
        }

        // The opening amount, corrected. A balance the books were taken on with is a fact
        // about that day, but one typed wrongly is still wrong, and the customer's own
        // screen is where somebody looks for it.
        if (request.OpeningBalance is { } opening)
        {
            Result reopened = found.Value.SetOpeningBalance(opening, EntrySide.Debit);

            if (reopened.IsFailure)
            {
                return Result.Failure<CustomerResponse>(reopened.Error);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CustomerDetails.Describe(found.Value));
    }
}

/// <summary>Handles <see cref="SetCustomerActiveCommand"/>.</summary>
public sealed class SetCustomerActiveCommandHandler
    : ICommandHandler<SetCustomerActiveCommand, CustomerResponse>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SetCustomerActiveCommandHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SetCustomerActiveCommandHandler(
        ILedgerRepository ledgers,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _ledgers = ledgers;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerResponse>> Handle(
        SetCustomerActiveCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Ledger> found = await CustomerDetails.ResolveAsync(
            _ledgers, _tenantContext, request.CustomerId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<CustomerResponse>(found.Error);
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

        return Result.Success(CustomerDetails.Describe(found.Value));
    }
}

/// <summary>Handles <see cref="GetCustomersQuery"/>.</summary>
public sealed class GetCustomersQueryHandler
    : IQueryHandler<GetCustomersQuery, IReadOnlyList<CustomerResponse>>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetCustomersQueryHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetCustomersQueryHandler(ILedgerRepository ledgers, ITenantContext tenantContext)
    {
        _ledgers = ledgers;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CustomerResponse>>> Handle(
        GetCustomersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<CustomerResponse>>(Error.Forbidden(
                "Customer.NoFirmSelected", "A firm must be selected to list customers."));
        }

        IReadOnlyList<Ledger> found = await _ledgers.ListByKindAsync(
            firmId, LedgerKind.Customer, request.Search, request.ActiveOnly, cancellationToken);

        return Result.Success<IReadOnlyList<CustomerResponse>>(
            [.. found.Select(CustomerDetails.Describe)]);
    }
}

/// <summary>Handles <see cref="GetCustomerQuery"/>.</summary>
public sealed class GetCustomerQueryHandler : IQueryHandler<GetCustomerQuery, CustomerResponse>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetCustomerQueryHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetCustomerQueryHandler(ILedgerRepository ledgers, ITenantContext tenantContext)
    {
        _ledgers = ledgers;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerResponse>> Handle(
        GetCustomerQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Ledger> found = await CustomerDetails.ResolveAsync(
            _ledgers, _tenantContext, request.CustomerId, cancellationToken);

        return found.IsFailure
            ? Result.Failure<CustomerResponse>(found.Error)
            : Result.Success(CustomerDetails.Describe(found.Value));
    }
}

/// <summary>The reading, writing and describing the customer handlers share.</summary>
internal static class CustomerDetails
{
    /// <summary>Finds a customer, refusing a ledger that is not one.</summary>
    /// <remarks>
    /// The kind is checked as well as the firm. A cash account reached through this
    /// endpoint would otherwise be given credit terms and a mobile number, and would
    /// then appear in a customer picker.
    /// </remarks>
    internal static async Task<Result<Ledger>> ResolveAsync(
        ILedgerRepository ledgers,
        ITenantContext tenantContext,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Ledger>(Error.Forbidden(
                "Customer.NoFirmSelected", "A firm must be selected to work with customers."));
        }

        Ledger? ledger = await ledgers.FindAsync(
            LedgerId.From(customerId), cancellationToken);

        return ledger is null || ledger.FirmId != firmId || ledger.Kind != LedgerKind.Customer
            ? Result.Failure<Ledger>(Error.NotFound(
                "Customer.NotFound", "That customer does not exist in the selected firm."))
            : Result.Success(ledger);
    }

    /// <summary>Applies whichever blocks the caller supplied, leaving the rest alone.</summary>
    /// <remarks>
    /// A block that was not sent is not cleared. Creating a customer and later changing
    /// only their address should not silently drop the credit terms somebody agreed with
    /// them, which is what a whole-record update would do to any field the caller's form
    /// did not happen to carry.
    /// </remarks>
    internal static Result Apply(
        Ledger customer,
        string? nameArabic,
        CustomerContact? contact,
        CustomerTerms? terms,
        CustomerTaxDetails? taxDetails,
        string? name = null)
    {
        if (name is not null)
        {
            Result renamed = customer.Rename(name);

            if (renamed.IsFailure)
            {
                return renamed;
            }
        }

        if (nameArabic is not null)
        {
            customer.SetArabicName(nameArabic);
        }

        if (contact is not null)
        {
            customer.SetContactDetails(
                contact.Phone, contact.MobileNumber, contact.Email,
                contact.AddressLine1, contact.AddressLine2);
        }

        if (taxDetails is not null)
        {
            customer.SetTaxDetails(taxDetails.RegistrationNumber, taxDetails.StateCode);
        }

        if (terms is null)
        {
            return Result.Success();
        }

        customer.SetBillWise(terms.IsBillWise);

        return customer.SetCreditTerms(terms.CreditLimit, terms.CreditDays);
    }

    /// <summary>Applies where a customer is, checking their area and route are the firm's own.</summary>
    /// <remarks>Left alone when the caller sent no profile, as the other blocks are.</remarks>
    internal static async Task<Result> ApplyProfileAsync(
        IPlaceRepository places,
        Ledger customer,
        CustomerProfile? profile,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return Result.Success();
        }

        Result<TerritoryId?> area = await TerritoryAsync(
            places, customer, profile.AreaId, TerritoryKind.Area, cancellationToken);

        if (area.IsFailure)
        {
            return area;
        }

        Result<TerritoryId?> route = await TerritoryAsync(
            places, customer, profile.RouteId, TerritoryKind.Route, cancellationToken);

        return route.IsFailure
            ? route
            : customer.SetPartyDetails(new PartyDetails(
                profile.City,
                profile.PinCode,
                profile.Website,
                profile.Fax,
                profile.ContactPerson,
                profile.GstNumber,
                profile.TinNumber,
                profile.CstNumber,
                profile.CountryCode,
                area.Value,
                route.Value,
                profile.SendsSms));
    }

    private static async Task<Result<TerritoryId?>> TerritoryAsync(
        IPlaceRepository places,
        Ledger customer,
        Guid? id,
        TerritoryKind kind,
        CancellationToken cancellationToken)
    {
        if (id is not { } value)
        {
            return Result.Success<TerritoryId?>(null);
        }

        Territory? territory = await places.FindTerritoryAsync(TerritoryId.From(value), cancellationToken);

        return territory is null || territory.FirmId != customer.FirmId || territory.Kind != kind
            ? Result.Failure<TerritoryId?>(Error.NotFound(
                "Customer.TerritoryNotFound", $"That {kind} is not one of the firm's."))
            : Result.Success<TerritoryId?>(territory.Id);
    }

    /// <summary>Describes a customer as the API reports them.</summary>
    internal static CustomerResponse Describe(Ledger customer) =>
        new(
            customer.Id.Value,
            customer.Code,
            customer.Name,
            customer.NameArabic,
            new CustomerContact(
                customer.MobileNumber,
                customer.Phone,
                customer.Email,
                customer.AddressLine1,
                customer.AddressLine2),
            new CustomerTerms(customer.CreditLimit, customer.CreditDays, customer.IsBillWise),
            new CustomerTaxDetails(customer.TaxRegistrationNumber, customer.StateCode),
            customer.Currency.Code,
            customer.OpeningBalance,
            customer.IsActive,
            new CustomerProfile(
                customer.City,
                customer.PinCode,
                customer.Website,
                customer.Fax,
                customer.ContactPerson,
                customer.GstNumber,
                customer.TinNumber,
                customer.CstNumber,
                customer.CountryCode,
                customer.AreaId?.Value,
                customer.RouteId?.Value,
                customer.SendsSms));
}
