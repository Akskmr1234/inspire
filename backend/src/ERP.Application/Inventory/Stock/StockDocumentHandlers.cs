using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Inventory.Stock;

/// <summary>Everything a stock document needs loaded before it can be built.</summary>
/// <param name="Firm">The firm, for its base currency.</param>
/// <param name="Year">The financial year the date falls in.</param>
/// <param name="Warehouse">The warehouse acted on.</param>
/// <param name="Destination">The warehouse a transfer moves into.</param>
/// <param name="Products">Every product named, by identifier.</param>
/// <param name="Units">Every unit involved, entry units and stock units alike.</param>
internal sealed record StockContext(
    Firm Firm,
    FinancialYear Year,
    Warehouse Warehouse,
    Warehouse? Destination,
    IReadOnlyDictionary<ProductId, Product> Products,
    IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> Units);

/// <summary>A document ready to post, and the batches its lines point at.</summary>
/// <param name="Document">The draft, with its lines.</param>
/// <param name="Batches">Every batch the lines name, by identifier.</param>
internal sealed record StockAssembly(
    StockDocument Document,
    IReadOnlyDictionary<BatchId, Batch> Batches);

/// <summary>Handles <see cref="CreateStockDocumentCommand"/>.</summary>
/// <remarks>
/// Everything is loaded and checked before anything is built. Discovering a product
/// from another firm halfway through assembling the document would leave a reserved
/// number burnt on a document that never existed, and a gap in a stock sequence is
/// exactly what an auditor asks about.
/// </remarks>
public sealed class CreateStockDocumentCommandHandler
    : ICommandHandler<CreateStockDocumentCommand, CreateStockDocumentResponse>
{
    private readonly IStockDocumentRepository _documents;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IFinancialYearRepository _financialYears;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly StockPoster _poster;

    /// <summary>Initialises a new instance of the <see cref="CreateStockDocumentCommandHandler"/> class.</summary>
    /// <param name="documents">The stock document repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="balances">The stock balance repository.</param>
    /// <param name="batchBalances">The batch position repository.</param>
    /// <param name="ledger">The stock ledger repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateStockDocumentCommandHandler(
        IStockDocumentRepository documents,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IStockLedgerRepository ledger,
        IFinancialYearRepository financialYears,
        INumberingSeriesRepository numbering,
        IFirmRepository firms,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _documents = documents;
        _masters = masters;
        _products = products;
        _batches = batches;
        _financialYears = financialYears;
        _numbering = numbering;
        _firms = firms;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _poster = new StockPoster(balances, batchBalances, ledger);
    }

    /// <inheritdoc />
    public async Task<Result<CreateStockDocumentResponse>> Handle(
        CreateStockDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<CreateStockDocumentResponse>(Error.Forbidden(
                "StockDocument.NoFirmOrBranchSelected",
                "A firm and a branch must be selected before entering a stock document."));
        }

        Result<StockContext> context = await StockLoader.LoadAsync(
            request, firmId, _firms, _financialYears, _masters, _products, cancellationToken);

        if (context.IsFailure)
        {
            return Result.Failure<CreateStockDocumentResponse>(context.Error);
        }

        Result<string> number = await ReserveNumberAsync(
            request.Type, firmId, branchId, context.Value.Year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<CreateStockDocumentResponse>(number.Error);
        }

        Result<StockAssembly> built = await StockLoader.BuildAsync(
            request, _tenantContext.TenantId, firmId, number.Value, context.Value, _batches,
            cancellationToken);

        if (built.IsFailure)
        {
            return Result.Failure<CreateStockDocumentResponse>(built.Error);
        }

        StockDocument document = built.Value.Document;
        IReadOnlyList<StockLedgerEntry> movements = [];

        if (request.PostImmediately)
        {
            Result posted = document.Post(_currentUser.UserId, _clock.UtcNow);

            if (posted.IsFailure)
            {
                return Result.Failure<CreateStockDocumentResponse>(posted.Error);
            }

            // The command is transactional, so a line that cannot move rolls the whole
            // document back - including the number it reserved, which is why a refused
            // posting leaves no gap in the sequence.
            Result<IReadOnlyList<StockLedgerEntry>> applied = await _poster.ApplyAsync(
                document,
                context.Value.Products,
                built.Value.Batches,
                context.Value.Firm.BaseCurrency,
                _clock.UtcNow,
                cancellationToken);

            if (applied.IsFailure)
            {
                return Result.Failure<CreateStockDocumentResponse>(applied.Error);
            }

            movements = applied.Value;
        }

        _documents.Add(document);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(StockLoader.Describe(document, movements));
    }

    /// <summary>
    /// Takes the next number, creating the series if the firm has none configured.
    /// </summary>
    /// <remarks>
    /// Auto-created for the same reason vouchers auto-create theirs: refusing to
    /// record a receipt until somebody visits a settings screen makes a fresh
    /// installation look broken.
    /// </remarks>
    private async Task<Result<string>> ReserveNumberAsync(
        StockDocumentType type,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        string documentType = DocumentTypes.ForStockDocument(type);

        NumberingSeries? series = await _numbering.FindForUpdateAsync(
            documentType, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                _tenantContext.TenantId, firmId, documentType, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat(
                prefix: DefaultPrefix(type),
                suffix: null,
                separator: "/",
                financialYearLabel: year.Code);

            _numbering.Add(series);
        }

        return series.Reserve();
    }

    /// <summary>The prefix a firm's documents start out carrying.</summary>
    /// <remarks>
    /// Short and conventional, and only a default: the numbering series is a master an
    /// administrator can reshape, so a firm with its own convention changes it once
    /// rather than living with this.
    /// </remarks>
    private static string DefaultPrefix(StockDocumentType type) => type switch
    {
        StockDocumentType.OpeningStock => "OS",
        StockDocumentType.MaterialReceipt => "MR",
        StockDocumentType.MaterialIssue => "MI",
        StockDocumentType.StockTransfer => "ST",
        StockDocumentType.StockAdjustment => "ADJ",
        StockDocumentType.DamagedStock => "DMG",
        StockDocumentType.PhysicalVerification => "PV",
        _ => "SD",
    };
}

/// <summary>Handles <see cref="PostStockDocumentCommand"/>.</summary>
public sealed class PostStockDocumentCommandHandler
    : ICommandHandler<PostStockDocumentCommand, CreateStockDocumentResponse>
{
    private readonly IStockDocumentRepository _documents;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly StockPoster _poster;

    /// <summary>Initialises a new instance of the <see cref="PostStockDocumentCommandHandler"/> class.</summary>
    /// <param name="documents">The stock document repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="balances">The stock balance repository.</param>
    /// <param name="batchBalances">The batch position repository.</param>
    /// <param name="ledger">The stock ledger repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public PostStockDocumentCommandHandler(
        IStockDocumentRepository documents,
        IProductRepository products,
        IBatchRepository batches,
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IStockLedgerRepository ledger,
        IFirmRepository firms,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _documents = documents;
        _products = products;
        _batches = batches;
        _firms = firms;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _poster = new StockPoster(balances, batchBalances, ledger);
    }

    /// <inheritdoc />
    public async Task<Result<CreateStockDocumentResponse>> Handle(
        PostStockDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockDocument> found = await StockLoader.ResolveAsync(
            _documents, _tenantContext, request.StockDocumentId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<CreateStockDocumentResponse>(found.Error);
        }

        StockDocument document = found.Value;

        Firm? firm = await _firms.FindAsync(document.FirmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<CreateStockDocumentResponse>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        Result posted = document.Post(_currentUser.UserId, _clock.UtcNow);

        if (posted.IsFailure)
        {
            return Result.Failure<CreateStockDocumentResponse>(posted.Error);
        }

        IReadOnlyDictionary<ProductId, Product> products = await _products.GetManyAsync(
            [.. document.Lines.Select(line => line.ProductId).Distinct()], cancellationToken);

        IReadOnlyDictionary<BatchId, Batch> batches = await BatchResolver.ForDocumentAsync(
            document, _batches, cancellationToken);

        Result<IReadOnlyList<StockLedgerEntry>> applied = await _poster.ApplyAsync(
            document, products, batches, firm.BaseCurrency, _clock.UtcNow, cancellationToken);

        if (applied.IsFailure)
        {
            return Result.Failure<CreateStockDocumentResponse>(applied.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(StockLoader.Describe(document, applied.Value));
    }
}

/// <summary>Handles <see cref="CancelStockDocumentCommand"/>.</summary>
public sealed class CancelStockDocumentCommandHandler
    : ICommandHandler<CancelStockDocumentCommand>
{
    private readonly IStockDocumentRepository _documents;
    private readonly IStockLedgerRepository _ledger;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly StockPoster _poster;

    /// <summary>Initialises a new instance of the <see cref="CancelStockDocumentCommandHandler"/> class.</summary>
    /// <param name="documents">The stock document repository.</param>
    /// <param name="balances">The stock balance repository.</param>
    /// <param name="batchBalances">The batch position repository.</param>
    /// <param name="ledger">The stock ledger repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CancelStockDocumentCommandHandler(
        IStockDocumentRepository documents,
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IStockLedgerRepository ledger,
        IProductRepository products,
        IBatchRepository batches,
        IFirmRepository firms,
        ITenantContext tenantContext,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _documents = documents;
        _ledger = ledger;
        _products = products;
        _batches = batches;
        _firms = firms;
        _tenantContext = tenantContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _poster = new StockPoster(balances, batchBalances, ledger);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        CancelStockDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockDocument> found = await StockLoader.ResolveAsync(
            _documents, _tenantContext, request.StockDocumentId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        StockDocument document = found.Value;

        Firm? firm = await _firms.FindAsync(document.FirmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        // The state change first, so a document that was never posted is refused
        // before anything goes looking for movements it does not have.
        Result cancelled = document.Cancel(request.Reason);

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        IReadOnlyList<StockLedgerEntry> movements =
            await _ledger.ForDocumentAsync(document.Id, cancellationToken);

        IReadOnlyDictionary<ProductId, Product> products = await _products.GetManyAsync(
            [.. document.Lines.Select(line => line.ProductId).Distinct()], cancellationToken);

        IReadOnlyDictionary<BatchId, Batch> batches = await BatchResolver.ForDocumentAsync(
            document, _batches, cancellationToken);

        Result reversed = await _poster.ReverseAsync(
            document, movements, products, batches, firm.BaseCurrency, _clock.UtcNow,
            cancellationToken);

        if (reversed.IsFailure)
        {
            return reversed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles the two stock document read queries.</summary>
public sealed class StockDocumentQueryHandler
    : IQueryHandler<ListStockDocumentsQuery, IReadOnlyList<StockDocumentSummary>>,
      IQueryHandler<GetStockDocumentQuery, StockDocumentDetail>
{
    private readonly IStockDocumentReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="StockDocumentQueryHandler"/> class.</summary>
    /// <param name="reader">The stock document reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public StockDocumentQueryHandler(IStockDocumentReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StockDocumentSummary>>> Handle(
        ListStockDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<StockDocumentSummary>>(Error.Forbidden(
                "StockDocument.NoFirmSelected",
                "A firm must be selected to read stock documents."));
        }

        if (request.To < request.From)
        {
            return Result.Failure<IReadOnlyList<StockDocumentSummary>>(Error.Validation(
                "StockDocument.RangeInverted",
                "The end of the range falls before its start."));
        }

        return Result.Success(await _reader.ListAsync(
            firmId,
            request.From,
            request.To,
            request.Type,
            request.WarehouseId is { } warehouse ? WarehouseId.From(warehouse) : null,
            request.Status,
            cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<StockDocumentDetail>> Handle(
        GetStockDocumentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<StockDocumentDetail>(Error.Forbidden(
                "StockDocument.NoFirmSelected",
                "A firm must be selected to read stock documents."));
        }

        StockDocumentDetail? document = await _reader.FindAsync(
            firmId, StockDocumentId.From(request.StockDocumentId), cancellationToken);

        return document is null
            ? Result.Failure<StockDocumentDetail>(Error.NotFound(
                "StockDocument.NotFound", "No such stock document in the selected firm."))
            : Result.Success(document);
    }
}

/// <summary>Loading and assembly shared by the stock document handlers.</summary>
internal static class StockLoader
{
    /// <summary>Loads and checks everything a document names, in four queries.</summary>
    internal static async Task<Result<StockContext>> LoadAsync(
        CreateStockDocumentCommand request,
        FirmId firmId,
        IFirmRepository firms,
        IFinancialYearRepository financialYears,
        IInventoryMasterRepository masters,
        IProductRepository products,
        CancellationToken cancellationToken)
    {
        Firm? firm = await firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<StockContext>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        FinancialYear? year = await financialYears.FindContainingAsync(
            firmId, request.Date, cancellationToken);

        if (year is null)
        {
            return Result.Failure<StockContext>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {request.Date:yyyy-MM-dd}. Create one before "
                + "recording stock on that date."));
        }

        Result<Warehouse> warehouse = await ResolveWarehouseAsync(
            masters, firmId, request.WarehouseId, cancellationToken);

        if (warehouse.IsFailure)
        {
            return Result.Failure<StockContext>(warehouse.Error);
        }

        Warehouse? destination = null;

        if (request.DestinationWarehouseId is { } into)
        {
            Result<Warehouse> resolved = await ResolveWarehouseAsync(
                masters, firmId, into, cancellationToken);

            if (resolved.IsFailure)
            {
                return Result.Failure<StockContext>(resolved.Error);
            }

            destination = resolved.Value;
        }

        List<ProductId> productIds =
            [.. request.Lines.Select(line => ProductId.From(line.ProductId)).Distinct()];

        IReadOnlyDictionary<ProductId, Product> found =
            await products.GetManyAsync(productIds, cancellationToken);

        foreach (ProductId id in productIds)
        {
            if (!found.TryGetValue(id, out Product? product) || product.FirmId != firmId)
            {
                return Result.Failure<StockContext>(Error.NotFound(
                    "StockDocument.ProductNotFound",
                    $"Product {id} is not in the selected firm."));
            }

            if (!product.IsActive)
            {
                return Result.Failure<StockContext>(Error.BusinessRule(
                    "StockDocument.ProductWithdrawn",
                    $"'{product.Code}' has been withdrawn from use."));
            }
        }

        // Both the units the lines were entered in and the stock unit of every product
        // they name. They overlap heavily - most lines are entered in the stock unit -
        // so one query for the union beats one per line.
        List<UnitOfMeasureId> unitIds =
        [
            .. request.Lines
                .Where(line => line.UnitId is not null)
                .Select(line => UnitOfMeasureId.From(line.UnitId!.Value))
                .Concat(found.Values.Select(product => product.StockUnitId))
                .Distinct(),
        ];

        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units =
            await masters.GetUnitsAsync(unitIds, cancellationToken);

        foreach (UnitOfMeasureId id in unitIds)
        {
            if (!units.TryGetValue(id, out UnitOfMeasure? unit) || unit.FirmId != firmId)
            {
                return Result.Failure<StockContext>(Error.NotFound(
                    "StockDocument.UnitNotFound",
                    $"Unit {id} is not in the selected firm."));
            }
        }

        return Result.Success(new StockContext(firm, year, warehouse.Value, destination, found, units));
    }

    /// <summary>Builds the draft and its lines, converting every quantity to stock units.</summary>
    /// <returns>The document and every batch it names, or the first refusal.</returns>
    /// <remarks>
    /// The batches come back with the document because posting needs them and the
    /// lines only carry their identifiers. Reading them again from the database would
    /// also miss the ones this document has just opened, which are not there yet.
    /// </remarks>
    internal static async Task<Result<StockAssembly>> BuildAsync(
        CreateStockDocumentCommand request,
        TenantId tenantId,
        FirmId firmId,
        string number,
        StockContext context,
        IBatchRepository batches,
        CancellationToken cancellationToken)
    {
        Result<StockDocument> draft = StockDocument.CreateDraft(
            tenantId, firmId, context.Year, request.Type, number, request.Date,
            context.Warehouse, context.Destination);

        if (draft.IsFailure)
        {
            return Result.Failure<StockAssembly>(draft.Error);
        }

        StockDocument document = draft.Value;

        Result<IReadOnlyList<Batch?>> resolved = await BatchResolver.ResolveAsync(
            document, request, tenantId, context, batches, cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<StockAssembly>(resolved.Error);
        }

        for (int index = 0; index < request.Lines.Count; index++)
        {
            StockDocumentLineInput line = request.Lines[index];
            Product product = context.Products[ProductId.From(line.ProductId)];
            UnitOfMeasure stockUnit = context.Units[product.StockUnitId];

            UnitOfMeasure entryUnit = line.UnitId is { } unitId
                ? context.Units[UnitOfMeasureId.From(unitId)]
                : stockUnit;

            // The conversion is refused rather than guessed at when the units measure
            // different things: four kilograms of something stocked in litres is not a
            // quantity anything can arrive at.
            Result<decimal> stockQuantity = UnitOfMeasure.Convert(
                line.Quantity, entryUnit, stockUnit);

            if (stockQuantity.IsFailure)
            {
                return Result.Failure<StockAssembly>(stockQuantity.Error);
            }

            Result<StockDocumentLine> added = document.AddLine(
                product, entryUnit, line.Quantity, stockQuantity.Value, line.Rate,
                resolved.Value[index], line.Remarks);

            if (added.IsFailure)
            {
                return Result.Failure<StockAssembly>(added.Error);
            }
        }

        Result details = document.SetDetails(request.ReferenceNumber, request.Narration);

        return details.IsFailure
            ? Result.Failure<StockAssembly>(details.Error)
            : Result.Success(new StockAssembly(
                document,
                resolved.Value
                    .Where(batch => batch is not null)
                    .Select(batch => batch!)
                    .DistinctBy(batch => batch.Id)
                    .ToDictionary(batch => batch.Id)));
    }

    /// <summary>Resolves a document, refusing one belonging to another firm.</summary>
    internal static async Task<Result<StockDocument>> ResolveAsync(
        IStockDocumentRepository documents,
        ITenantContext tenantContext,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<StockDocument>(Error.Forbidden(
                "StockDocument.NoFirmSelected",
                "A firm must be selected to work with stock documents."));
        }

        StockDocument? document = await documents.FindAsync(
            StockDocumentId.From(documentId), cancellationToken);

        return document is null || document.FirmId != firmId
            ? Result.Failure<StockDocument>(Error.NotFound(
                "StockDocument.NotFound", "No such stock document in the selected firm."))
            : Result.Success(document);
    }

    /// <summary>Describes what a posting did, for the caller that asked for it.</summary>
    internal static CreateStockDocumentResponse Describe(
        StockDocument document,
        IReadOnlyList<StockLedgerEntry> movements)
    {
        // The value of the goods the document handled, counted once. A transfer writes
        // two equal and opposite movements for the same goods, so summing both would
        // report double what moved; taking the incoming side reports the goods.
        decimal received = movements
            .Where(entry => entry.Quantity > 0m)
            .Sum(entry => entry.Value.Amount);

        decimal total = received > 0m
            ? received
            : movements.Sum(entry => Math.Abs(entry.Value.Amount));

        return new CreateStockDocumentResponse(
            document.Id.Value, document.Number, document.Status, movements.Count, total);
    }

    private static async Task<Result<Warehouse>> ResolveWarehouseAsync(
        IInventoryMasterRepository masters,
        FirmId firmId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        Warehouse? warehouse = await masters.FindWarehouseAsync(
            WarehouseId.From(warehouseId), cancellationToken);

        return warehouse is null || warehouse.FirmId != firmId
            ? Result.Failure<Warehouse>(Error.NotFound(
                "StockDocument.WarehouseNotFound",
                "No such warehouse in the selected firm."))
            : Result.Success(warehouse);
    }
}
