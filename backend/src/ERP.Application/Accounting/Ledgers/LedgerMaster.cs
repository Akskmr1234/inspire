using System.Text;
using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Ledgers;

/// <summary>A ledger as somebody enters or corrects it on the chart of accounts.</summary>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="AccountGroupId">The group it reports under.</param>
/// <param name="Kind">General, cash, bank or employee.</param>
/// <param name="OpeningBalance">What stood on it when the books were taken on.</param>
/// <param name="OpeningSide">Which side that balance falls on.</param>
/// <param name="Phone">A telephone number, for a bank or an employee.</param>
/// <param name="Email">An email address.</param>
/// <param name="Address">A one-line address or note, such as a bank's branch and account number.</param>
public sealed record LedgerInput(
    string Name,
    string? NameArabic,
    Guid AccountGroupId,
    LedgerKind Kind,
    decimal OpeningBalance = 0m,
    EntrySide OpeningSide = EntrySide.Debit,
    string? Phone = null,
    string? Email = null,
    string? Address = null);

/// <summary>Creates a ledger: an expense or income head, a bank account, cash, an employee.</summary>
/// <param name="Code">Its code. Made from the name when omitted.</param>
/// <param name="Ledger">The ledger.</param>
/// <remarks>
/// Customers and suppliers are created on their own screens, which also record their
/// terms and registration; tax ledgers belong to the firm's tax map and charges to the
/// charge setup. This is for every other account a firm posts to.
/// </remarks>
public sealed record CreateLedgerCommand(string? Code, LedgerInput Ledger)
    : ICommand<LedgerDetail>, ITransactional, INamesMaster
{
    /// <inheritdoc />
    NamedMaster INamesMaster.Master => NamedMaster.Ledger;

    /// <inheritdoc />
    string INamesMaster.NameGiven => Ledger.Name;

    /// <inheritdoc />
    Guid? INamesMaster.RenamedId => null;
}

/// <summary>Corrects a ledger. Its code stays as it is.</summary>
/// <param name="LedgerId">The ledger.</param>
/// <param name="Ledger">The ledger as it should be.</param>
public sealed record UpdateLedgerCommand(Guid LedgerId, LedgerInput Ledger)
    : ICommand<LedgerDetail>, ITransactional, INamesMaster
{
    /// <inheritdoc />
    NamedMaster INamesMaster.Master => NamedMaster.Ledger;

    /// <inheritdoc />
    string INamesMaster.NameGiven => Ledger.Name;

    /// <inheritdoc />
    Guid? INamesMaster.RenamedId => LedgerId;
}

/// <summary>Withdraws a ledger from use, or puts it back.</summary>
/// <param name="LedgerId">The ledger.</param>
/// <param name="IsActive">Whether it may be posted to.</param>
public sealed record SetLedgerActiveCommand(Guid LedgerId, bool IsActive)
    : ICommand<LedgerDetail>, ITransactional;

/// <summary>Reads one ledger.</summary>
/// <param name="LedgerId">The ledger.</param>
public sealed record GetLedgerQuery(Guid LedgerId) : IQuery<LedgerDetail>;

/// <summary>A ledger with everything its form shows.</summary>
/// <param name="LedgerId">The ledger.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="AccountGroupId">The group it reports under.</param>
/// <param name="GroupName">That group's name.</param>
/// <param name="Nature">Asset, liability, equity, income or expense.</param>
/// <param name="Kind">What it represents.</param>
/// <param name="Currency">The currency it is kept in.</param>
/// <param name="OpeningBalance">What stood on it when the books were taken on.</param>
/// <param name="OpeningSide">Which side that balance falls on.</param>
/// <param name="Phone">A telephone number.</param>
/// <param name="Email">An email address.</param>
/// <param name="Address">An address or note.</param>
/// <param name="IsActive">Whether it may be posted to.</param>
/// <param name="IsEditable">
/// Whether this form may change it. Customers, suppliers, tax and charge ledgers are kept
/// on their own screens.
/// </param>
public sealed record LedgerDetail(
    Guid LedgerId,
    string Code,
    string Name,
    string? NameArabic,
    Guid AccountGroupId,
    string GroupName,
    AccountNature Nature,
    LedgerKind Kind,
    string Currency,
    decimal OpeningBalance,
    EntrySide OpeningSide,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive,
    bool IsEditable);

/// <summary>Lists the firm's account groups.</summary>
public sealed record ListAccountGroupsQuery : IQuery<IReadOnlyList<AccountGroupSummary>>;

/// <summary>An account group, for the chart of accounts and its pickers.</summary>
/// <param name="AccountGroupId">The group.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="Nature">Asset, liability, equity, income or expense.</param>
/// <param name="ParentId">The group above it, if any.</param>
/// <param name="IsSystemGroup">Whether it is one of the five heads, which stay as they are.</param>
public sealed record AccountGroupSummary(
    Guid AccountGroupId,
    string Code,
    string Name,
    string? NameArabic,
    AccountNature Nature,
    Guid? ParentId,
    bool IsSystemGroup);

/// <summary>Creates a group beneath another, such as "Loans" under Liabilities.</summary>
/// <param name="Code">Its code. Made from the name when omitted.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="ParentId">The group it sits under, whose nature it takes.</param>
public sealed record CreateAccountGroupCommand(
    string? Code,
    string Name,
    string? NameArabic,
    Guid ParentId) : ICommand<AccountGroupSummary>, ITransactional, INamesMaster
{
    /// <inheritdoc />
    NamedMaster INamesMaster.Master => NamedMaster.AccountGroup;

    /// <inheritdoc />
    string INamesMaster.NameGiven => Name;

    /// <inheritdoc />
    Guid? INamesMaster.RenamedId => null;
}

/// <summary>Renames a group.</summary>
/// <param name="AccountGroupId">The group.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
public sealed record UpdateAccountGroupCommand(
    Guid AccountGroupId,
    string Name,
    string? NameArabic) : ICommand<AccountGroupSummary>, ITransactional, INamesMaster
{
    /// <inheritdoc />
    NamedMaster INamesMaster.Master => NamedMaster.AccountGroup;

    /// <inheritdoc />
    string INamesMaster.NameGiven => Name;

    /// <inheritdoc />
    Guid? INamesMaster.RenamedId => AccountGroupId;
}

/// <summary>Validates <see cref="CreateLedgerCommand"/>.</summary>
public sealed class CreateLedgerCommandValidator : AbstractValidator<CreateLedgerCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateLedgerCommandValidator"/> class.</summary>
    public CreateLedgerCommandValidator()
    {
        RuleFor(c => c.Code).MaximumLength(30);
        RuleFor(c => c.Ledger).NotNull().SetValidator(new LedgerInputValidator());
    }
}

/// <summary>Validates <see cref="UpdateLedgerCommand"/>.</summary>
public sealed class UpdateLedgerCommandValidator : AbstractValidator<UpdateLedgerCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateLedgerCommandValidator"/> class.</summary>
    public UpdateLedgerCommandValidator()
    {
        RuleFor(c => c.LedgerId).NotEqual(Guid.Empty);
        RuleFor(c => c.Ledger).NotNull().SetValidator(new LedgerInputValidator());
    }
}

/// <summary>Validates <see cref="CreateAccountGroupCommand"/>.</summary>
public sealed class CreateAccountGroupCommandValidator : AbstractValidator<CreateAccountGroupCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateAccountGroupCommandValidator"/> class.</summary>
    public CreateAccountGroupCommandValidator()
    {
        RuleFor(c => c.Code).MaximumLength(20);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(150);
        RuleFor(c => c.NameArabic).MaximumLength(150);
        RuleFor(c => c.ParentId).NotEqual(Guid.Empty);
    }
}

/// <summary>Validates <see cref="UpdateAccountGroupCommand"/>.</summary>
public sealed class UpdateAccountGroupCommandValidator : AbstractValidator<UpdateAccountGroupCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateAccountGroupCommandValidator"/> class.</summary>
    public UpdateAccountGroupCommandValidator()
    {
        RuleFor(c => c.AccountGroupId).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(150);
        RuleFor(c => c.NameArabic).MaximumLength(150);
    }
}

/// <summary>Validates a <see cref="LedgerInput"/>.</summary>
internal sealed class LedgerInputValidator : AbstractValidator<LedgerInput>
{
    /// <summary>Initialises a new instance of the <see cref="LedgerInputValidator"/> class.</summary>
    public LedgerInputValidator()
    {
        RuleFor(l => l.Name).NotEmpty().MaximumLength(200);
        RuleFor(l => l.NameArabic).MaximumLength(200);
        RuleFor(l => l.AccountGroupId).NotEqual(Guid.Empty);
        RuleFor(l => l.Kind).IsInEnum();
        RuleFor(l => l.OpeningBalance).GreaterThanOrEqualTo(0m);
        RuleFor(l => l.OpeningSide).IsInEnum();
        RuleFor(l => l.Phone).MaximumLength(30);
        RuleFor(l => l.Email).MaximumLength(200);
        RuleFor(l => l.Address).MaximumLength(200);
    }
}

/// <summary>Handles the ledger master: creating, correcting and withdrawing ledgers.</summary>
public sealed class LedgerMasterHandler
    : ICommandHandler<CreateLedgerCommand, LedgerDetail>,
      ICommandHandler<UpdateLedgerCommand, LedgerDetail>,
      ICommandHandler<SetLedgerActiveCommand, LedgerDetail>,
      IQueryHandler<GetLedgerQuery, LedgerDetail>
{
    private readonly ILedgerRepository _ledgers;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="LedgerMasterHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public LedgerMasterHandler(
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
    public async Task<Result<LedgerDetail>> Handle(
        CreateLedgerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return NoFirm();
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<LedgerDetail>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        LedgerInput input = request.Ledger;

        if (!Ledger.IsPlain(input.Kind))
        {
            return Result.Failure<LedgerDetail>(Error.Validation(
                "Ledger.KindNotHere",
                "Customers, suppliers, tax and charge ledgers are created on their own "
                + "screens. Choose general, cash, bank or employee."));
        }

        Result<AccountGroup> group = await GroupOfFirmAsync(input.AccountGroupId, firmId, cancellationToken);

        if (group.IsFailure)
        {
            return Result.Failure<LedgerDetail>(group.Error);
        }

        string code;

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            code = await FreeCodeAsync(
                Codes.FromName(input.Name, 30, "LEDGER"),
                30,
                candidate => _ledgers.IsCodeInUseAsync(firmId, candidate, cancellationToken));
        }
        else
        {
            code = request.Code.Trim().ToUpperInvariant();

            if (await _ledgers.IsCodeInUseAsync(firmId, code, cancellationToken))
            {
                return Result.Failure<LedgerDetail>(Error.Conflict(
                    "Ledger.CodeInUse", $"'{code}' is already used by another account."));
            }
        }

        Result<Ledger> created = Ledger.Create(
            group.Value, code, input.Name, input.Kind, firm.BaseCurrency);

        if (created.IsFailure)
        {
            return Result.Failure<LedgerDetail>(created.Error);
        }

        Ledger ledger = created.Value;
        Result applied = Apply(ledger, input);

        if (applied.IsFailure)
        {
            return Result.Failure<LedgerDetail>(applied.Error);
        }

        _ledgers.Add(ledger);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(ledger, group.Value));
    }

    /// <inheritdoc />
    public async Task<Result<LedgerDetail>> Handle(
        UpdateLedgerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return NoFirm();
        }

        Ledger? ledger = await _ledgers.FindAsync(LedgerId.From(request.LedgerId), cancellationToken);

        if (ledger is null || ledger.FirmId != firmId)
        {
            return NotFound();
        }

        if (!Ledger.IsPlain(ledger.Kind))
        {
            return Result.Failure<LedgerDetail>(Error.BusinessRule(
                "Ledger.EditedElsewhere",
                $"'{ledger.Name}' is a {ledger.Kind} ledger and is changed on its own screen."));
        }

        LedgerInput input = request.Ledger;
        Result<AccountGroup> group = await GroupOfFirmAsync(input.AccountGroupId, firmId, cancellationToken);

        if (group.IsFailure)
        {
            return Result.Failure<LedgerDetail>(group.Error);
        }

        Result changed = ledger.Rename(input.Name);
        changed = changed.IsSuccess ? ledger.MoveTo(group.Value) : changed;
        changed = changed.IsSuccess ? ledger.ChangeKind(input.Kind) : changed;
        changed = changed.IsSuccess ? Apply(ledger, input) : changed;

        if (changed.IsFailure)
        {
            return Result.Failure<LedgerDetail>(changed.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(ledger, group.Value));
    }

    /// <inheritdoc />
    public async Task<Result<LedgerDetail>> Handle(
        SetLedgerActiveCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return NoFirm();
        }

        Ledger? ledger = await _ledgers.FindAsync(LedgerId.From(request.LedgerId), cancellationToken);

        if (ledger is null || ledger.FirmId != firmId)
        {
            return NotFound();
        }

        if (request.IsActive)
        {
            ledger.Activate();
        }
        else
        {
            ledger.Deactivate();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        AccountGroup? group = await _ledgers.FindGroupAsync(ledger.AccountGroupId, cancellationToken);

        return Result.Success(Describe(ledger, group));
    }

    /// <inheritdoc />
    public async Task<Result<LedgerDetail>> Handle(
        GetLedgerQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return NoFirm();
        }

        Ledger? ledger = await _ledgers.FindAsync(LedgerId.From(request.LedgerId), cancellationToken);

        if (ledger is null || ledger.FirmId != firmId)
        {
            return NotFound();
        }

        AccountGroup? group = await _ledgers.FindGroupAsync(ledger.AccountGroupId, cancellationToken);

        return Result.Success(Describe(ledger, group));
    }

    /// <summary>Finds a code nobody holds: the wanted one, or it with -2, -3 and so on.</summary>
    /// <param name="wanted">The code to start from.</param>
    /// <param name="maxLength">The longest a code may be.</param>
    /// <param name="inUse">Whether a code is taken.</param>
    /// <returns>A free code.</returns>
    internal static async Task<string> FreeCodeAsync(
        string wanted,
        int maxLength,
        Func<string, Task<bool>> inUse)
    {
        string candidate = wanted;
        int attempt = 1;

        while (await inUse(candidate))
        {
            attempt++;
            string suffix = $"-{attempt}";
            candidate = wanted[..Math.Min(wanted.Length, maxLength - suffix.Length)] + suffix;
        }

        return candidate;
    }

    private static Result Apply(Ledger ledger, LedgerInput input)
    {
        ledger.SetArabicName(input.NameArabic);
        ledger.SetContactDetails(input.Phone, null, input.Email, input.Address, null);

        return ledger.SetOpeningBalance(input.OpeningBalance, input.OpeningSide);
    }

    private static LedgerDetail Describe(Ledger ledger, AccountGroup? group) =>
        new(
            ledger.Id.Value,
            ledger.Code,
            ledger.Name,
            ledger.NameArabic,
            ledger.AccountGroupId.Value,
            group?.Name ?? string.Empty,
            group?.Nature ?? AccountNature.Asset,
            ledger.Kind,
            ledger.Currency.Code,
            ledger.OpeningBalance,
            ledger.OpeningBalanceSide,
            ledger.Phone,
            ledger.Email,
            ledger.AddressLine1,
            ledger.IsActive,
            Ledger.IsPlain(ledger.Kind));

    private static Result<LedgerDetail> NoFirm() =>
        Result.Failure<LedgerDetail>(Error.Forbidden(
            "Ledger.NoFirmSelected", "A firm must be selected to keep its ledgers."));

    private static Result<LedgerDetail> NotFound() =>
        Result.Failure<LedgerDetail>(Error.NotFound(
            "Ledger.NotFound", "That ledger is not in the selected firm."));

    private async Task<Result<AccountGroup>> GroupOfFirmAsync(
        Guid groupId,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        AccountGroup? group = await _ledgers.FindGroupAsync(
            AccountGroupId.From(groupId), cancellationToken);

        return group is null || group.FirmId != firmId
            ? Result.Failure<AccountGroup>(Error.NotFound(
                "Ledger.GroupNotFound", "That account group is not in the selected firm."))
            : Result.Success(group);
    }
}

/// <summary>Handles the account groups: listing, creating and renaming them.</summary>
public sealed class AccountGroupHandler
    : IQueryHandler<ListAccountGroupsQuery, IReadOnlyList<AccountGroupSummary>>,
      ICommandHandler<CreateAccountGroupCommand, AccountGroupSummary>,
      ICommandHandler<UpdateAccountGroupCommand, AccountGroupSummary>
{
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="AccountGroupHandler"/> class.</summary>
    /// <param name="ledgers">The ledger repository, which also keeps the groups.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public AccountGroupHandler(
        ILedgerRepository ledgers,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _ledgers = ledgers;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AccountGroupSummary>>> Handle(
        ListAccountGroupsQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<AccountGroupSummary>>(Error.Forbidden(
                "AccountGroup.NoFirmSelected", "A firm must be selected to list its groups."));
        }

        IReadOnlyList<AccountGroup> groups = await _ledgers.ListGroupsAsync(firmId, cancellationToken);

        return Result.Success<IReadOnlyList<AccountGroupSummary>>([.. groups.Select(Describe)]);
    }

    /// <inheritdoc />
    public async Task<Result<AccountGroupSummary>> Handle(
        CreateAccountGroupCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return NoFirm();
        }

        AccountGroup? parent = await _ledgers.FindGroupAsync(
            AccountGroupId.From(request.ParentId), cancellationToken);

        if (parent is null || parent.FirmId != firmId)
        {
            return Result.Failure<AccountGroupSummary>(Error.NotFound(
                "AccountGroup.ParentNotFound", "That parent group is not in the selected firm."));
        }

        string code;

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            code = await LedgerMasterHandler.FreeCodeAsync(
                Codes.FromName(request.Name, 20, "GROUP"),
                20,
                candidate => _ledgers.IsGroupCodeInUseAsync(firmId, candidate, cancellationToken));
        }
        else
        {
            code = request.Code.Trim().ToUpperInvariant();

            if (await _ledgers.IsGroupCodeInUseAsync(firmId, code, cancellationToken))
            {
                return Result.Failure<AccountGroupSummary>(Error.Conflict(
                    "AccountGroup.CodeInUse", $"'{code}' is already used by another group."));
            }
        }

        Result<AccountGroup> created = AccountGroup.CreateChild(parent, code, request.Name);

        if (created.IsFailure)
        {
            return Result.Failure<AccountGroupSummary>(created.Error);
        }

        created.Value.SetArabicName(request.NameArabic);
        _ledgers.AddGroup(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(created.Value));
    }

    /// <inheritdoc />
    public async Task<Result<AccountGroupSummary>> Handle(
        UpdateAccountGroupCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return NoFirm();
        }

        AccountGroup? group = await _ledgers.FindGroupAsync(
            AccountGroupId.From(request.AccountGroupId), cancellationToken);

        if (group is null || group.FirmId != firmId)
        {
            return Result.Failure<AccountGroupSummary>(Error.NotFound(
                "AccountGroup.NotFound", "That account group is not in the selected firm."));
        }

        Result renamed = group.Rename(request.Name);

        if (renamed.IsFailure)
        {
            return Result.Failure<AccountGroupSummary>(renamed.Error);
        }

        group.SetArabicName(request.NameArabic);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(group));
    }

    private static AccountGroupSummary Describe(AccountGroup group) =>
        new(
            group.Id.Value,
            group.Code,
            group.Name,
            group.NameArabic,
            group.Nature,
            group.ParentId?.Value,
            group.IsSystemGroup);

    private static Result<AccountGroupSummary> NoFirm() =>
        Result.Failure<AccountGroupSummary>(Error.Forbidden(
            "AccountGroup.NoFirmSelected", "A firm must be selected to keep its groups."));
}

/// <summary>Codes made from names, for masters whose code the user left blank.</summary>
public static class Codes
{
    /// <summary>"Office rent &amp; rates" becomes "OFFICE-RENT-RATES".</summary>
    /// <param name="name">The name.</param>
    /// <param name="maxLength">The longest the code may be.</param>
    /// <param name="fallback">The code for a name with no letters or digits in it, such as an Arabic one.</param>
    /// <returns>The code.</returns>
    public static string FromName(string name, int maxLength, string fallback)
    {
        StringBuilder code = new();

        foreach (char c in (name ?? string.Empty).Trim().ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                code.Append(c);
            }
            else if (code.Length > 0 && code[^1] != '-')
            {
                code.Append('-');
            }
        }

        string result = code.ToString().Trim('-');

        if (result.Length > maxLength)
        {
            result = result[..maxLength].TrimEnd('-');
        }

        return result.Length == 0 ? fallback : result;
    }
}
