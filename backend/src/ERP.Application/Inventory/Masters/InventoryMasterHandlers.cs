using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Masters;

/// <summary>Handles the four inventory master list queries.</summary>
/// <remarks>
/// One class for four queries. They differ only in which reader method they call, and
/// four classes would repeat the same firm resolution four times for no benefit.
/// </remarks>
public sealed class ListInventoryMastersQueryHandler
    : IQueryHandler<ListUnitsQuery, IReadOnlyList<UnitSummary>>,
      IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategorySummary>>,
      IQueryHandler<ListBrandsQuery, IReadOnlyList<BrandSummary>>,
      IQueryHandler<ListWarehousesQuery, IReadOnlyList<WarehouseSummary>>
{
    private readonly IInventoryMasterReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ListInventoryMastersQueryHandler"/> class.</summary>
    /// <param name="reader">The master reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public ListInventoryMastersQueryHandler(
        IInventoryMasterReader reader,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<UnitSummary>>> Handle(
        ListUnitsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        return firm.IsFailure
            ? Result.Failure<IReadOnlyList<UnitSummary>>(firm.Error)
            : Result.Success(await _reader.ReadUnitsAsync(
                firm.Value, request.IncludeInactive, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CategorySummary>>> Handle(
        ListCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        return firm.IsFailure
            ? Result.Failure<IReadOnlyList<CategorySummary>>(firm.Error)
            : Result.Success(await _reader.ReadCategoriesAsync(
                firm.Value, request.IncludeInactive, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BrandSummary>>> Handle(
        ListBrandsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        return firm.IsFailure
            ? Result.Failure<IReadOnlyList<BrandSummary>>(firm.Error)
            : Result.Success(await _reader.ReadBrandsAsync(
                firm.Value, request.IncludeInactive, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<WarehouseSummary>>> Handle(
        ListWarehousesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        return firm.IsFailure
            ? Result.Failure<IReadOnlyList<WarehouseSummary>>(firm.Error)
            : Result.Success(await _reader.ReadWarehousesAsync(
                firm.Value, request.IncludeInactive, cancellationToken));
    }
}

/// <summary>Handles <see cref="CreateUnitCommand"/>.</summary>
public sealed class CreateUnitCommandHandler : ICommandHandler<CreateUnitCommand, Guid>
{
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreateUnitCommandHandler"/> class.</summary>
    /// <param name="masters">The master repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateUnitCommandHandler(
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _masters = masters;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        CreateUnitCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure<Guid>(firm.Error);
        }

        Result<string> code = await MasterContext.ReserveCodeAsync(
            _masters, InventoryMasterKind.UnitOfMeasure, firm.Value, request.Code,
            cancellationToken);

        if (code.IsFailure)
        {
            return Result.Failure<Guid>(code.Error);
        }

        Result<UnitOfMeasure> created;

        if (request.BaseUnitId is { } baseUnitId)
        {
            UnitOfMeasure? baseUnit = await _masters.FindUnitAsync(
                UnitOfMeasureId.From(baseUnitId), cancellationToken);

            if (baseUnit is null || baseUnit.FirmId != firm.Value)
            {
                return Result.Failure<Guid>(Error.NotFound(
                    "UnitOfMeasure.BaseNotFound",
                    "No such base unit in the selected firm."));
            }

            created = UnitOfMeasure.CreateDerived(
                baseUnit, code.Value, request.Name, request.ConversionFactor,
                request.Symbol, request.DecimalPlaces);
        }
        else
        {
            created = UnitOfMeasure.CreateBase(
                _tenantContext.TenantId, firm.Value, code.Value, request.Name,
                request.Symbol, request.DecimalPlaces);
        }

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        _masters.Add(created.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }
}

/// <summary>Handles <see cref="RenameUnitCommand"/>.</summary>
public sealed class RenameUnitCommandHandler : ICommandHandler<RenameUnitCommand>
{
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="RenameUnitCommandHandler"/> class.</summary>
    /// <param name="masters">The master repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public RenameUnitCommandHandler(
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _masters = masters;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        RenameUnitCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure(firm.Error);
        }

        UnitOfMeasure? unit = await _masters.FindUnitAsync(
            UnitOfMeasureId.From(request.UnitId), cancellationToken);

        if (unit is null || unit.FirmId != firm.Value)
        {
            return Result.Failure(Error.NotFound(
                "UnitOfMeasure.NotFound", "No such unit in the selected firm."));
        }

        Result renamed = unit.Rename(request.Name);

        if (renamed.IsFailure)
        {
            return renamed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="CreateCategoryCommand"/>.</summary>
public sealed class CreateCategoryCommandHandler
    : ICommandHandler<CreateCategoryCommand, Guid>
{
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreateCategoryCommandHandler"/> class.</summary>
    /// <param name="masters">The master repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateCategoryCommandHandler(
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _masters = masters;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        CreateCategoryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure<Guid>(firm.Error);
        }

        Result<string> code = await MasterContext.ReserveCodeAsync(
            _masters, InventoryMasterKind.Category, firm.Value, request.Code,
            cancellationToken);

        if (code.IsFailure)
        {
            return Result.Failure<Guid>(code.Error);
        }

        Result<Category> created;

        if (request.ParentId is { } parentId)
        {
            Category? parent = await _masters.FindCategoryAsync(
                CategoryId.From(parentId), cancellationToken);

            if (parent is null || parent.FirmId != firm.Value)
            {
                return Result.Failure<Guid>(Error.NotFound(
                    "Category.ParentNotFound", "No such parent category in the selected firm."));
            }

            created = Category.CreateChild(parent, code.Value, request.Name);
        }
        else
        {
            created = Category.CreateRoot(
                _tenantContext.TenantId, firm.Value, code.Value, request.Name);
        }

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        created.Value.SetArabicName(request.NameArabic);
        _masters.Add(created.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }
}

/// <summary>Handles <see cref="CreateBrandCommand"/>.</summary>
public sealed class CreateBrandCommandHandler : ICommandHandler<CreateBrandCommand, Guid>
{
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreateBrandCommandHandler"/> class.</summary>
    /// <param name="masters">The master repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateBrandCommandHandler(
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _masters = masters;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        CreateBrandCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure<Guid>(firm.Error);
        }

        Result<string> code = await MasterContext.ReserveCodeAsync(
            _masters, InventoryMasterKind.Brand, firm.Value, request.Code, cancellationToken);

        if (code.IsFailure)
        {
            return Result.Failure<Guid>(code.Error);
        }

        Result<Brand> created = Brand.Create(
            _tenantContext.TenantId, firm.Value, code.Value, request.Name);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        created.Value.SetArabicName(request.NameArabic);
        _masters.Add(created.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }
}

/// <summary>Handles <see cref="CreateWarehouseCommand"/>.</summary>
public sealed class CreateWarehouseCommandHandler
    : ICommandHandler<CreateWarehouseCommand, Guid>
{
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreateWarehouseCommandHandler"/> class.</summary>
    /// <param name="masters">The master repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateWarehouseCommandHandler(
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _masters = masters;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        CreateWarehouseCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure<Guid>(firm.Error);
        }

        Result<string> code = await MasterContext.ReserveCodeAsync(
            _masters, InventoryMasterKind.Warehouse, firm.Value, request.Code,
            cancellationToken);

        if (code.IsFailure)
        {
            return Result.Failure<Guid>(code.Error);
        }

        Result<Warehouse> created = Warehouse.Create(
            _tenantContext.TenantId, firm.Value, code.Value, request.Name,
            request.BranchId is { } branchId ? BranchId.From(branchId) : null);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        Warehouse warehouse = created.Value;
        warehouse.SetArabicName(request.NameArabic);

        Result addressed = warehouse.SetAddress(request.Address);

        if (addressed.IsFailure)
        {
            return Result.Failure<Guid>(addressed.Error);
        }

        // The first warehouse a firm creates becomes the default, because a firm with
        // stock locations and no default would have every document refuse to fill
        // itself in - and somebody would have to discover the setting before they could
        // enter anything at all.
        if (await _masters.FindDefaultWarehouseAsync(firm.Value, cancellationToken) is null)
        {
            warehouse.MakeDefault();
        }

        _masters.Add(warehouse);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(warehouse.Id.Value);
    }
}

/// <summary>Handles <see cref="SetDefaultWarehouseCommand"/>.</summary>
public sealed class SetDefaultWarehouseCommandHandler
    : ICommandHandler<SetDefaultWarehouseCommand>
{
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SetDefaultWarehouseCommandHandler"/> class.</summary>
    /// <param name="masters">The master repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SetDefaultWarehouseCommandHandler(
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _masters = masters;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetDefaultWarehouseCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure(firm.Error);
        }

        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.WarehouseId), cancellationToken);

        if (warehouse is null || warehouse.FirmId != firm.Value)
        {
            return Result.Failure(Error.NotFound(
                "Warehouse.NotFound", "No such warehouse in the selected firm."));
        }

        // Demoted before the new one is promoted, and in the same transaction. The
        // filtered unique index permits one default per firm, so writing the second
        // without clearing the first would be rejected by the database - correctly,
        // and with a message about an index rather than about warehouses.
        Warehouse? current = await _masters.FindDefaultWarehouseAsync(
            firm.Value, cancellationToken);

        if (current is not null && current.Id != warehouse.Id)
        {
            current.ClearDefault();
        }

        Result promoted = warehouse.MakeDefault();

        if (promoted.IsFailure)
        {
            return promoted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="SetMasterActiveCommand"/>.</summary>
public sealed class SetMasterActiveCommandHandler : ICommandHandler<SetMasterActiveCommand>
{
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SetMasterActiveCommandHandler"/> class.</summary>
    /// <param name="masters">The master repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SetMasterActiveCommandHandler(
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _masters = masters;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetMasterActiveCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = MasterContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure(firm.Error);
        }

        Result applied = request.Kind switch
        {
            InventoryMasterKind.UnitOfMeasure => await SetUnitAsync(
                request, firm.Value, cancellationToken),
            InventoryMasterKind.Category => await SetCategoryAsync(
                request, firm.Value, cancellationToken),
            InventoryMasterKind.Brand => await SetBrandAsync(
                request, firm.Value, cancellationToken),
            InventoryMasterKind.Warehouse => await SetWarehouseAsync(
                request, firm.Value, cancellationToken),
            _ => Result.Failure(Error.Validation(
                "InventoryMaster.UnknownKind",
                $"'{request.Kind}' is not a recognised inventory master.")),
        };

        if (applied.IsFailure)
        {
            return applied;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<Result> SetUnitAsync(
        SetMasterActiveCommand request,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        UnitOfMeasure? unit = await _masters.FindUnitAsync(
            UnitOfMeasureId.From(request.Id), cancellationToken);

        if (unit is null || unit.FirmId != firmId)
        {
            return NotFound("unit");
        }

        if (request.IsActive)
        {
            unit.Activate();
        }
        else
        {
            unit.Deactivate();
        }

        return Result.Success();
    }

    private async Task<Result> SetCategoryAsync(
        SetMasterActiveCommand request,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        Category? category = await _masters.FindCategoryAsync(
            CategoryId.From(request.Id), cancellationToken);

        if (category is null || category.FirmId != firmId)
        {
            return NotFound("category");
        }

        if (request.IsActive)
        {
            category.Activate();
        }
        else
        {
            category.Deactivate();
        }

        return Result.Success();
    }

    private async Task<Result> SetBrandAsync(
        SetMasterActiveCommand request,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        Brand? brand = await _masters.FindBrandAsync(
            BrandId.From(request.Id), cancellationToken);

        if (brand is null || brand.FirmId != firmId)
        {
            return NotFound("brand");
        }

        if (request.IsActive)
        {
            brand.Activate();
        }
        else
        {
            brand.Deactivate();
        }

        return Result.Success();
    }

    private async Task<Result> SetWarehouseAsync(
        SetMasterActiveCommand request,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.Id), cancellationToken);

        if (warehouse is null || warehouse.FirmId != firmId)
        {
            return NotFound("warehouse");
        }

        if (request.IsActive)
        {
            warehouse.Activate();

            return Result.Success();
        }

        // The aggregate refuses to withdraw the default, which is the one case here
        // that carries a rule rather than just a flag.
        return warehouse.Deactivate();
    }

    private static Result NotFound(string noun) =>
        Result.Failure(Error.NotFound(
            "InventoryMaster.NotFound", $"No such {noun} in the selected firm."));
}
