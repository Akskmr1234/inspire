using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Inventory.Masters;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;

namespace ERP.Application.Service;

/// <summary>Handles asking for a return and taking it through approval and inspection.</summary>
public sealed class RmaHandlers
    : ICommandHandler<CreateRmaCommand, Guid>,
      ICommandHandler<UpdateRmaCommand>,
      ICommandHandler<RmaStepCommand>,
      ICommandHandler<InspectRmaCommand>,
      ICommandHandler<AddRmaPhotoCommand, Guid>,
      ICommandHandler<RemoveRmaPhotoCommand>,
      IQueryHandler<GetRmaPhotoQuery, string>,
      IQueryHandler<ListRmasQuery, IReadOnlyList<RmaSummary>>,
      IQueryHandler<GetRmaQuery, RmaDetail>
{
    private readonly IRmaRepository _rmas;
    private readonly IRmaReader _reader;
    private readonly ILedgerRepository _ledgers;
    private readonly IInventoryMasterRepository _masters;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly ISalesInvoiceRepository _sales;
    private readonly IPurchaseInvoiceRepository _purchases;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="RmaHandlers"/> class.</summary>
    /// <param name="rmas">The return repository.</param>
    /// <param name="reader">The return reader.</param>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="products">The product repository.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="sales">The sales invoice repository.</param>
    /// <param name="purchases">The purchase invoice repository.</param>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="financialYears">The financial-year repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public RmaHandlers(
        IRmaRepository rmas,
        IRmaReader reader,
        ILedgerRepository ledgers,
        IInventoryMasterRepository masters,
        IProductRepository products,
        IBatchRepository batches,
        ISalesInvoiceRepository sales,
        IPurchaseInvoiceRepository purchases,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _rmas = rmas;
        _reader = reader;
        _ledgers = ledgers;
        _masters = masters;
        _products = products;
        _batches = batches;
        _sales = sales;
        _purchases = purchases;
        _numbering = numbering;
        _financialYears = financialYears;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(CreateRmaCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId || _tenantContext.BranchId is not { } branchId)
        {
            return Result.Failure<Guid>(Error.Forbidden(
                "Rma.NoFirmOrBranchSelected", "A firm and a branch must be selected to raise a return."));
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(firmId, request.Date, cancellationToken);
        Ledger? party = await _ledgers.FindAsync(LedgerId.From(request.PartyLedgerId), cancellationToken);
        Warehouse? warehouse = await _masters.FindWarehouseAsync(
            WarehouseId.From(request.WarehouseId), cancellationToken);

        if (year is null)
        {
            return Result.Failure<Guid>(Error.BusinessRule(
                "Rma.NoFinancialYear", $"No financial year covers {request.Date:yyyy-MM-dd}."));
        }

        if (party is null || warehouse is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Rma.PartyOrGodownNotFound", "No such customer, supplier or godown in the selected firm."));
        }

        Result<string> number = await StockDocumentWriter.ReserveAsync(
            _numbering, _tenantContext.TenantId, DocumentTypes.Rma, firmId, branchId, year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<Guid>(number.Error);
        }

        Result<Rma> created = Rma.Create(
            _tenantContext.TenantId, firmId, year, number.Value, request.Date, request.Direction, party,
            warehouse, Actor());

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        Result filled = await FillAsync(created.Value, request.Rma, cancellationToken);

        if (filled.IsFailure)
        {
            return Result.Failure<Guid>(filled.Error);
        }

        _rmas.Add(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(UpdateRmaCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Rma> found = await RmaAccess.FindAsync(_rmas, _tenantContext, request.RmaId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Result filled = await FillAsync(found.Value, request.Rma, cancellationToken);

        if (filled.IsFailure)
        {
            return filled;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> Handle(RmaStepCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Rma> found = await RmaAccess.FindAsync(_rmas, _tenantContext, request.RmaId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Rma rma = found.Value;
        RmaActor actor = Actor();

        Result stepped = request.Step switch
        {
            RmaStep.Approve => rma.Approve(actor, request.Note),
            RmaStep.Reject => rma.Reject(actor, request.Note ?? string.Empty),
            RmaStep.Cancel => rma.Cancel(actor, request.Note ?? string.Empty),
            RmaStep.Receive => rma.Receive(actor, request.Note),
            RmaStep.Repair => rma.Repair(actor, request.Note),
            RmaStep.Close => rma.Close(actor, request.Note),
            _ => Result.Failure(Error.Validation("Rma.UnknownStep", "No such step.")),
        };

        if (stepped.IsFailure)
        {
            return stepped;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> Handle(InspectRmaCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Rma> found = await RmaAccess.FindAsync(_rmas, _tenantContext, request.RmaId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Result inspected = found.Value.Inspect(
            request.Condition,
            request.Notes,
            request.On ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            Actor());

        if (inspected.IsFailure)
        {
            return inspected;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(AddRmaPhotoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Rma> found = await RmaAccess.FindAsync(_rmas, _tenantContext, request.RmaId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<Guid>(found.Error);
        }

        Result<RmaPhoto> added = found.Value.AddPhoto(request.Content, request.Caption);

        if (added.IsFailure)
        {
            return Result.Failure<Guid>(added.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(added.Value.Id.Value);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(RemoveRmaPhotoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Rma> found = await RmaAccess.FindAsync(_rmas, _tenantContext, request.RmaId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Result removed = found.Value.RemovePhoto(RmaPhotoId.From(request.PhotoId));

        if (removed.IsFailure)
        {
            return removed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<string>> Handle(GetRmaPhotoQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<string>(RmaAccess.NoFirm());
        }

        string? content = await _reader.FindPhotoAsync(
            firmId, RmaId.From(request.RmaId), RmaPhotoId.From(request.PhotoId), cancellationToken);

        return content is null
            ? Result.Failure<string>(Error.NotFound("Rma.PhotoNotFound", "No such photograph on this return."))
            : Result.Success(content);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RmaSummary>>> Handle(
        ListRmasQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is not { } firmId
            ? Result.Failure<IReadOnlyList<RmaSummary>>(RmaAccess.NoFirm())
            : Result.Success(await _reader.ListAsync(firmId, request, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<RmaDetail>> Handle(GetRmaQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<RmaDetail>(RmaAccess.NoFirm());
        }

        RmaDetail? found = await _reader.FindAsync(firmId, RmaId.From(request.RmaId), cancellationToken);

        return found is null ? Result.Failure<RmaDetail>(RmaAccess.NotFound()) : Result.Success(found);
    }

    private RmaActor Actor() => RmaAccess.Actor(_currentUser, _clock);

    private async Task<Result> FillAsync(Rma rma, RmaInput input, CancellationToken cancellationToken)
    {
        SalesInvoiceId? sale = null;
        PurchaseInvoiceId? purchase = null;

        // A linked invoice has to be this party's, posted, and an invoice rather than a
        // return: it is what a refund is raised against.
        if (input.SalesInvoiceId is { } saleId)
        {
            SalesInvoice? invoice = await _sales.FindAsync(SalesInvoiceId.From(saleId), cancellationToken);

            if (invoice is null || invoice.FirmId != rma.FirmId || invoice.CustomerLedgerId != rma.PartyLedgerId
                || invoice.Kind != SalesDocumentKind.Invoice || invoice.Status != SalesInvoiceStatus.Posted)
            {
                return Result.Failure(Error.Validation(
                    "Rma.InvoiceNotTheParty's", "The sale named is not a posted invoice to this customer."));
            }

            sale = invoice.Id;
        }

        if (input.PurchaseInvoiceId is { } purchaseId)
        {
            PurchaseInvoice? invoice = await _purchases.FindAsync(
                PurchaseInvoiceId.From(purchaseId), cancellationToken);

            if (invoice is null || invoice.FirmId != rma.FirmId || invoice.SupplierLedgerId != rma.PartyLedgerId
                || invoice.Kind != PurchaseDocumentKind.Invoice || invoice.Status != PurchaseInvoiceStatus.Posted)
            {
                return Result.Failure(Error.Validation(
                    "Rma.InvoiceNotTheParty's", "The purchase named is not a posted invoice from this supplier."));
            }

            purchase = invoice.Id;
        }

        Result described = rma.Describe(input.Reason, input.Narration, input.InvoiceReference, sale, purchase);

        if (described.IsFailure)
        {
            return described;
        }

        IReadOnlyDictionary<ProductId, Product> products = await _products.GetManyAsync(
            [.. input.Lines.Select(line => ProductId.From(line.ProductId)).Distinct()], cancellationToken);

        IReadOnlyDictionary<BatchId, Batch> batches = await _batches.GetManyAsync(
            [.. input.Lines.Where(line => line.BatchId is not null).Select(line => BatchId.From(line.BatchId!.Value)).Distinct()],
            cancellationToken);

        Result cleared = rma.ClearLines();

        if (cleared.IsFailure)
        {
            return cleared;
        }

        foreach (RmaLineInput line in input.Lines)
        {
            if (!products.TryGetValue(ProductId.From(line.ProductId), out Product? product))
            {
                return Result.Failure(Error.NotFound("Rma.ProductNotFound", "A product on the return no longer exists."));
            }

            Batch? batch = line.BatchId is { } batchId ? batches.GetValueOrDefault(BatchId.From(batchId)) : null;

            Result<RmaLine> added = rma.AddLine(product, line.Quantity, line.Reason, line.SerialNumber, batch);

            if (added.IsFailure)
            {
                return added;
            }
        }

        return Result.Success();
    }
}

/// <summary>What the return handlers share.</summary>
internal static class RmaAccess
{
    /// <summary>Who is acting, and when, for a return's history.</summary>
    /// <param name="user">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The actor.</returns>
    internal static RmaActor Actor(ICurrentUser user, IClock clock) =>
        new(user.UserId, user.UserName ?? string.Empty, clock.UtcNow);

    /// <summary>Loads a return of the selected firm.</summary>
    /// <param name="rmas">The return repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="id">The return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The return, or the reason it could not be had.</returns>
    internal static async Task<Result<Rma>> FindAsync(
        IRmaRepository rmas,
        ITenantContext tenantContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Rma>(NoFirm());
        }

        Rma? rma = await rmas.FindAsync(RmaId.From(id), cancellationToken);

        return rma is null || rma.FirmId != firmId ? Result.Failure<Rma>(NotFound()) : Result.Success(rma);
    }

    /// <summary>The refusal when no firm is selected.</summary>
    /// <returns>The error.</returns>
    internal static Error NoFirm() =>
        Error.Forbidden("Rma.NoFirmSelected", "A firm must be selected to work with returns.");

    /// <summary>The refusal when the return is not the firm's.</summary>
    /// <returns>The error.</returns>
    internal static Error NotFound() =>
        Error.NotFound("Rma.NotFound", "No such return in the selected firm.");
}
