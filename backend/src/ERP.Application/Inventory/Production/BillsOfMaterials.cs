using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Inventory.Production;

/// <summary>One component of a bill, as entered.</summary>
/// <param name="ProductId">The material.</param>
/// <param name="Quantity">How much of it, in its stock unit, makes the bill's output.</param>
/// <param name="Remarks">A remark.</param>
public sealed record BillOfMaterialsComponentInput(Guid ProductId, decimal Quantity, string? Remarks = null);

/// <summary>Creates a bill of materials for a finished good.</summary>
/// <param name="ProductId">The finished good.</param>
/// <param name="OutputQuantity">How much of it the components make, in its stock unit.</param>
/// <param name="Components">What goes into it.</param>
/// <param name="Narration">A note on how it is made.</param>
public sealed record CreateBillOfMaterialsCommand(
    Guid ProductId,
    decimal OutputQuantity,
    IReadOnlyList<BillOfMaterialsComponentInput> Components,
    string? Narration = null) : ICommand<Guid>, ITransactional;

/// <summary>Replaces a bill's quantity, note and components.</summary>
/// <param name="BillOfMaterialsId">The bill.</param>
/// <param name="OutputQuantity">How much the components make, in the stock unit.</param>
/// <param name="Components">What goes into it, whole.</param>
/// <param name="Narration">A note on how it is made.</param>
public sealed record UpdateBillOfMaterialsCommand(
    Guid BillOfMaterialsId,
    decimal OutputQuantity,
    IReadOnlyList<BillOfMaterialsComponentInput> Components,
    string? Narration = null) : ICommand, ITransactional;

/// <summary>Withdraws a bill from use, or brings it back.</summary>
/// <param name="BillOfMaterialsId">The bill.</param>
/// <param name="IsActive">Whether production may use it.</param>
public sealed record SetBillOfMaterialsActiveCommand(Guid BillOfMaterialsId, bool IsActive)
    : ICommand, ITransactional;

/// <summary>Lists the firm's bills of materials.</summary>
/// <param name="IncludeInactive">Whether withdrawn bills are included.</param>
public sealed record ListBillsOfMaterialsQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<BillOfMaterialsSummary>>;

/// <summary>Reads one bill, with what each component costs today.</summary>
/// <param name="BillOfMaterialsId">The bill.</param>
public sealed record GetBillOfMaterialsQuery(Guid BillOfMaterialsId) : IQuery<BillOfMaterialsDetail>;

/// <summary>A bill as the list shows it.</summary>
/// <param name="Id">The bill.</param>
/// <param name="ProductId">The finished good.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductName">Its description.</param>
/// <param name="StockUnitCode">Its stock unit.</param>
/// <param name="OutputQuantity">How much the components make.</param>
/// <param name="ComponentCount">How many materials go into it.</param>
/// <param name="EstimatedCost">What the components cost at their products' cost today.</param>
/// <param name="IsActive">Whether production may use it.</param>
public sealed record BillOfMaterialsSummary(
    Guid Id,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string StockUnitCode,
    decimal OutputQuantity,
    int ComponentCount,
    decimal EstimatedCost,
    bool IsActive);

/// <summary>One component, as the bill shows it.</summary>
/// <param name="ProductId">The material.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductName">Its description.</param>
/// <param name="StockUnitCode">Its stock unit.</param>
/// <param name="Quantity">How much goes in.</param>
/// <param name="UnitCost">What one stock unit costs today.</param>
/// <param name="TracksBatches">Whether using it up has to name a batch.</param>
/// <param name="Remarks">The remark.</param>
public sealed record BillOfMaterialsComponent(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string StockUnitCode,
    decimal Quantity,
    decimal UnitCost,
    bool TracksBatches,
    string? Remarks);

/// <summary>A bill with its components.</summary>
/// <param name="Summary">What the list shows.</param>
/// <param name="Narration">The note on how it is made.</param>
/// <param name="Components">What goes into it.</param>
public sealed record BillOfMaterialsDetail(
    BillOfMaterialsSummary Summary,
    string? Narration,
    IReadOnlyList<BillOfMaterialsComponent> Components);

/// <summary>Reads bills of materials for the screens.</summary>
public interface IBillOfMaterialsReader
{
    /// <summary>Lists a firm's bills.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="includeInactive">Whether withdrawn bills are included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bills, by finished good.</returns>
    Task<IReadOnlyList<BillOfMaterialsSummary>> ListAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one bill.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="id">The bill.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bill, or <see langword="null"/>.</returns>
    Task<BillOfMaterialsDetail?> FindAsync(
        FirmId firmId,
        BillOfMaterialsId id,
        CancellationToken cancellationToken = default);
}

/// <summary>Validates <see cref="CreateBillOfMaterialsCommand"/>.</summary>
public sealed class CreateBillOfMaterialsCommandValidator : AbstractValidator<CreateBillOfMaterialsCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateBillOfMaterialsCommandValidator"/> class.</summary>
    public CreateBillOfMaterialsCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEqual(Guid.Empty);
        RuleFor(c => c.OutputQuantity).GreaterThan(0m);
        RuleFor(c => c.Components).NotEmpty().WithMessage("A bill needs at least one component.");
        RuleForEach(c => c.Components).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty);
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.Remarks).MaximumLength(BillOfMaterials.MaximumNarrationLength);
        });
        RuleFor(c => c.Narration).MaximumLength(BillOfMaterials.MaximumNarrationLength);
    }
}

/// <summary>Validates <see cref="UpdateBillOfMaterialsCommand"/>.</summary>
public sealed class UpdateBillOfMaterialsCommandValidator : AbstractValidator<UpdateBillOfMaterialsCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateBillOfMaterialsCommandValidator"/> class.</summary>
    public UpdateBillOfMaterialsCommandValidator()
    {
        RuleFor(c => c.BillOfMaterialsId).NotEqual(Guid.Empty);
        RuleFor(c => c.OutputQuantity).GreaterThan(0m);
        RuleFor(c => c.Components).NotEmpty().WithMessage("A bill needs at least one component.");
        RuleForEach(c => c.Components).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty);
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.Remarks).MaximumLength(BillOfMaterials.MaximumNarrationLength);
        });
        RuleFor(c => c.Narration).MaximumLength(BillOfMaterials.MaximumNarrationLength);
    }
}

/// <summary>Handles the bill-of-materials commands and queries.</summary>
public sealed class BillOfMaterialsHandlers
    : ICommandHandler<CreateBillOfMaterialsCommand, Guid>,
      ICommandHandler<UpdateBillOfMaterialsCommand>,
      ICommandHandler<SetBillOfMaterialsActiveCommand>,
      IQueryHandler<ListBillsOfMaterialsQuery, IReadOnlyList<BillOfMaterialsSummary>>,
      IQueryHandler<GetBillOfMaterialsQuery, BillOfMaterialsDetail>
{
    private readonly IBillOfMaterialsRepository _bills;
    private readonly IBillOfMaterialsReader _reader;
    private readonly IProductRepository _products;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="BillOfMaterialsHandlers"/> class.</summary>
    /// <param name="bills">The bill repository.</param>
    /// <param name="reader">The bill reader.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public BillOfMaterialsHandlers(
        IBillOfMaterialsRepository bills,
        IBillOfMaterialsReader reader,
        IProductRepository products,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _bills = bills;
        _reader = reader;
        _products = products;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        CreateBillOfMaterialsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Guid>(NoFirm());
        }

        ProductId finished = ProductId.From(request.ProductId);

        if (await _bills.ExistsForAsync(firmId, finished, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Bom.AlreadyExists",
                "That product already has a bill of materials. Change the one it has."));
        }

        IReadOnlyDictionary<ProductId, Product> products = await LoadAsync(
            request.ProductId, request.Components, cancellationToken);

        if (!products.TryGetValue(finished, out Product? product))
        {
            return Result.Failure<Guid>(NotFound());
        }

        Result<BillOfMaterials> created = BillOfMaterials.Create(
            _tenantContext.TenantId, firmId, product, request.OutputQuantity);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        Result filled = Fill(created.Value, request.OutputQuantity, request.Narration, request.Components, products);

        if (filled.IsFailure)
        {
            return Result.Failure<Guid>(filled.Error);
        }

        _bills.Add(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        UpdateBillOfMaterialsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<BillOfMaterials> found = await FindAsync(request.BillOfMaterialsId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        BillOfMaterials bill = found.Value;

        IReadOnlyDictionary<ProductId, Product> products = await LoadAsync(
            bill.ProductId.Value, request.Components, cancellationToken);

        bill.ClearComponents();

        Result filled = Fill(bill, request.OutputQuantity, request.Narration, request.Components, products);

        if (filled.IsFailure)
        {
            return filled;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetBillOfMaterialsActiveCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<BillOfMaterials> found = await FindAsync(request.BillOfMaterialsId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        found.Value.SetActive(request.IsActive);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BillOfMaterialsSummary>>> Handle(
        ListBillsOfMaterialsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is not { } firmId
            ? Result.Failure<IReadOnlyList<BillOfMaterialsSummary>>(NoFirm())
            : Result.Success(await _reader.ListAsync(firmId, request.IncludeInactive, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<BillOfMaterialsDetail>> Handle(
        GetBillOfMaterialsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<BillOfMaterialsDetail>(NoFirm());
        }

        BillOfMaterialsDetail? found = await _reader.FindAsync(
            firmId, BillOfMaterialsId.From(request.BillOfMaterialsId), cancellationToken);

        return found is null
            ? Result.Failure<BillOfMaterialsDetail>(NotFound())
            : Result.Success(found);
    }

    private static Error NoFirm() =>
        Error.Forbidden("Bom.NoFirmSelected", "A firm must be selected to work with bills of materials.");

    private static Error NotFound() =>
        Error.NotFound("Bom.NotFound", "No such bill of materials, or product, in the selected firm.");

    private static Result Fill(
        BillOfMaterials bill,
        decimal outputQuantity,
        string? narration,
        IReadOnlyList<BillOfMaterialsComponentInput> components,
        IReadOnlyDictionary<ProductId, Product> products)
    {
        Result revised = bill.Revise(outputQuantity, narration);

        if (revised.IsFailure)
        {
            return revised;
        }

        foreach (BillOfMaterialsComponentInput input in components)
        {
            if (!products.TryGetValue(ProductId.From(input.ProductId), out Product? component))
            {
                return Result.Failure(NotFound());
            }

            Result<BillOfMaterialsLine> added = bill.AddComponent(component, input.Quantity, input.Remarks);

            if (added.IsFailure)
            {
                return added;
            }
        }

        return Result.Success();
    }

    private async Task<IReadOnlyDictionary<ProductId, Product>> LoadAsync(
        Guid finished,
        IReadOnlyList<BillOfMaterialsComponentInput> components,
        CancellationToken cancellationToken) =>
        await _products.GetManyAsync(
            [.. components.Select(line => ProductId.From(line.ProductId)).Append(ProductId.From(finished)).Distinct()],
            cancellationToken);

    private async Task<Result<BillOfMaterials>> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<BillOfMaterials>(NoFirm());
        }

        BillOfMaterials? bill = await _bills.FindAsync(BillOfMaterialsId.From(id), cancellationToken);

        return bill is null || bill.FirmId != firmId
            ? Result.Failure<BillOfMaterials>(NotFound())
            : Result.Success(bill);
    }
}
