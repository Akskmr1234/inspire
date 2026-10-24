using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Inventory.Masters;

/// <summary>Lists a firm's tax rates.</summary>
/// <param name="IncludeInactive">Whether withdrawn rates are included.</param>
public sealed record ListTaxSlabsQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<TaxSlabSummary>>;

/// <summary>A tax rate as a list shows it.</summary>
/// <param name="Id">The rate.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="Percentage">The percentage.</param>
/// <param name="IsDefault">Whether new products start with it.</param>
/// <param name="IsActive">Whether it may be given to products.</param>
public sealed record TaxSlabSummary(
    Guid Id,
    string Code,
    string Name,
    string? NameArabic,
    decimal Percentage,
    bool IsDefault,
    bool IsActive);

/// <summary>Adds a tax rate.</summary>
/// <param name="Code">The code; made from the name when left blank.</param>
/// <param name="Name">The name.</param>
/// <param name="Percentage">The percentage.</param>
/// <param name="NameArabic">The name in Arabic.</param>
/// <param name="IsDefault">Whether new products start with it.</param>
public sealed record CreateTaxSlabCommand(
    string? Code,
    string Name,
    decimal Percentage,
    string? NameArabic = null,
    bool IsDefault = false) : ICommand<Guid>, INamesMaster
{
    /// <inheritdoc />
    NamedMaster INamesMaster.Master => NamedMaster.TaxSlab;

    /// <inheritdoc />
    string INamesMaster.NameGiven => Name;

    /// <inheritdoc />
    Guid? INamesMaster.RenamedId => null;
}

/// <summary>Corrects a tax rate.</summary>
/// <param name="Id">The rate.</param>
/// <param name="Name">The name.</param>
/// <param name="Percentage">The percentage, for documents raised from now on.</param>
/// <param name="NameArabic">The name in Arabic.</param>
/// <param name="IsDefault">Whether new products start with it.</param>
public sealed record UpdateTaxSlabCommand(
    Guid Id,
    string Name,
    decimal Percentage,
    string? NameArabic = null,
    bool IsDefault = false) : ICommand, INamesMaster
{
    /// <inheritdoc />
    NamedMaster INamesMaster.Master => NamedMaster.TaxSlab;

    /// <inheritdoc />
    string INamesMaster.NameGiven => Name;

    /// <inheritdoc />
    Guid? INamesMaster.RenamedId => Id;
}

/// <summary>Withdraws a tax rate from use, or returns it.</summary>
/// <param name="Id">The rate.</param>
/// <param name="IsActive">Whether it should be usable.</param>
public sealed record SetTaxSlabActiveCommand(Guid Id, bool IsActive) : ICommand;

/// <summary>Reads and writes tax rates.</summary>
public interface ITaxSlabRepository
{
    /// <summary>Lists a firm's rates, lowest percentage first.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="includeInactive">Whether withdrawn rates are included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rates.</returns>
    Task<IReadOnlyList<TaxSlab>> ListAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one rate.</summary>
    /// <param name="id">The rate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rate, or null.</returns>
    Task<TaxSlab?> FindAsync(TaxSlabId id, CancellationToken cancellationToken = default);

    /// <summary>Finds the firm's default rate.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The default, or null when the firm has none.</returns>
    Task<TaxSlab?> FindDefaultAsync(FirmId firmId, CancellationToken cancellationToken = default);

    /// <summary>Adds a rate.</summary>
    /// <param name="slab">The rate.</param>
    void Add(TaxSlab slab);
}

/// <summary>Validates <see cref="CreateTaxSlabCommand"/>.</summary>
public sealed class CreateTaxSlabCommandValidator : AbstractValidator<CreateTaxSlabCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateTaxSlabCommandValidator"/> class.</summary>
    public CreateTaxSlabCommandValidator()
    {
        RuleFor(c => c.Code!).MaximumLength(TaxSlab.MaximumCodeLength).When(c => c.Code is not null);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(TaxSlab.MaximumNameLength);
        RuleFor(c => c.Percentage).InclusiveBetween(0m, 100m);
    }
}

/// <summary>Validates <see cref="UpdateTaxSlabCommand"/>.</summary>
public sealed class UpdateTaxSlabCommandValidator : AbstractValidator<UpdateTaxSlabCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateTaxSlabCommandValidator"/> class.</summary>
    public UpdateTaxSlabCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(TaxSlab.MaximumNameLength);
        RuleFor(c => c.Percentage).InclusiveBetween(0m, 100m);
    }
}

/// <summary>Handles the tax rate master.</summary>
public sealed class TaxSlabHandlers
    : IQueryHandler<ListTaxSlabsQuery, IReadOnlyList<TaxSlabSummary>>,
      ICommandHandler<CreateTaxSlabCommand, Guid>,
      ICommandHandler<UpdateTaxSlabCommand>,
      ICommandHandler<SetTaxSlabActiveCommand>
{
    private readonly ITaxSlabRepository _slabs;
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="TaxSlabHandlers"/> class.</summary>
    /// <param name="slabs">The tax rate repository.</param>
    /// <param name="masters">The master repository, for codes.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public TaxSlabHandlers(
        ITaxSlabRepository slabs,
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _slabs = slabs;
        _masters = masters;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<TaxSlabSummary>>> Handle(
        ListTaxSlabsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure<IReadOnlyList<TaxSlabSummary>>(firm.Error);
        }

        IReadOnlyList<TaxSlab> slabs = await _slabs.ListAsync(
            firm.Value, request.IncludeInactive, cancellationToken);

        return Result.Success<IReadOnlyList<TaxSlabSummary>>(
        [
            .. slabs.Select(slab => new TaxSlabSummary(
                slab.Id.Value, slab.Code, slab.Name, slab.NameArabic, slab.Percentage,
                slab.IsDefault, slab.IsActive)),
        ]);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        CreateTaxSlabCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure<Guid>(firm.Error);
        }

        Result taken = await EnsurePercentageFreeAsync(
            firm.Value, request.Percentage, except: null, cancellationToken);

        if (taken.IsFailure)
        {
            return Result.Failure<Guid>(taken.Error);
        }

        Result<string> code = await MasterContext.ReserveCodeAsync(
            _masters, InventoryMasterKind.TaxSlab, firm.Value, request.Code,
            request.Name, TaxSlab.MaximumCodeLength, cancellationToken);

        if (code.IsFailure)
        {
            return Result.Failure<Guid>(code.Error);
        }

        Result<TaxSlab> created = TaxSlab.Create(
            _tenantContext.TenantId, firm.Value, code.Value, request.Name, request.Percentage);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        TaxSlab slab = created.Value;
        slab.SetArabicName(request.NameArabic);

        if (request.IsDefault)
        {
            await MakeDefaultAsync(slab, firm.Value, cancellationToken);
        }

        _slabs.Add(slab);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(slab.Id.Value);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        UpdateTaxSlabCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<TaxSlab> found = await FindAsync(request.Id, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        TaxSlab slab = found.Value;

        Result taken = await EnsurePercentageFreeAsync(
            slab.FirmId, request.Percentage, slab.Id, cancellationToken);

        if (taken.IsFailure)
        {
            return taken;
        }

        Result changed = slab.Change(request.Name, request.Percentage);

        if (changed.IsFailure)
        {
            return changed;
        }

        slab.SetArabicName(request.NameArabic);

        if (request.IsDefault && !slab.IsDefault)
        {
            await MakeDefaultAsync(slab, slab.FirmId, cancellationToken);
        }
        else if (!request.IsDefault)
        {
            slab.ClearDefault();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetTaxSlabActiveCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<TaxSlab> found = await FindAsync(request.Id, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
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
        return Result.Success();
    }

    private async Task<Result<TaxSlab>> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure<TaxSlab>(firm.Error);
        }

        TaxSlab? slab = await _slabs.FindAsync(TaxSlabId.From(id), cancellationToken);

        return slab is null || slab.FirmId != firm.Value
            ? Result.Failure<TaxSlab>(Error.NotFound(
                "TaxSlab.NotFound", "No such tax rate in the selected firm."))
            : Result.Success(slab);
    }

    /// <summary>Makes one rate the default, taking it from whichever had it.</summary>
    /// <summary>Refuses a percentage another of the firm's tax rates already carries.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="percentage">The percentage wanted.</param>
    /// <param name="except">The rate being changed, which may keep its own percentage.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or a conflict naming the rate that has it.</returns>
    /// <remarks>
    /// Withdrawn rates count: two rates of the same percentage are two entries in every
    /// product's picker and two lines on the tax return for one tax, and the way back to
    /// a withdrawn one is to restore it rather than to make another.
    /// </remarks>
    private async Task<Result> EnsurePercentageFreeAsync(
        FirmId firmId,
        decimal percentage,
        TaxSlabId? except,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TaxSlab> slabs = await _slabs.ListAsync(
            firmId, includeInactive: true, cancellationToken);

        TaxSlab? holder = slabs.FirstOrDefault(slab =>
            slab.Percentage == percentage && (except is null || slab.Id != except));

        if (holder is null)
        {
            return Result.Success();
        }

        string withdrawn = holder.IsActive ? string.Empty : " (withdrawn - restore it instead)";

        return Result.Failure(Error.Conflict(
            "TaxSlab.DuplicatePercentage",
            $"A tax rate of {percentage:0.##}% already exists: {holder.Name}{withdrawn}."));
    }

    private async Task MakeDefaultAsync(TaxSlab slab, FirmId firmId, CancellationToken cancellationToken)
    {
        TaxSlab? previous = await _slabs.FindDefaultAsync(firmId, cancellationToken);

        if (previous is not null && previous.Id != slab.Id)
        {
            previous.ClearDefault();
        }

        slab.MakeDefault();
    }
}
