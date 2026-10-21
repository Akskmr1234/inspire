using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Inventory.Stock;

/// <summary>One product asked for on a transfer request.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Quantity">How much, in the unit given.</param>
/// <param name="UnitId">The unit, or null for the product's stock unit.</param>
/// <param name="Remarks">A remark for the line.</param>
public sealed record StockTransferLineInput(
    Guid ProductId,
    decimal Quantity,
    Guid? UnitId = null,
    string? Remarks = null);

/// <summary>The quantity agreed for one product, in its stock unit.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Quantity">The quantity agreed; nought turns the line down.</param>
public sealed record StockTransferApprovalInput(Guid ProductId, decimal Quantity);

/// <summary>Asks another warehouse for goods.</summary>
/// <param name="Date">The date they are asked for.</param>
/// <param name="FromWarehouseId">The warehouse that holds them.</param>
/// <param name="ToWarehouseId">The warehouse that wants them.</param>
/// <param name="Lines">What is wanted.</param>
/// <param name="Narration">A note for the branch that will send them.</param>
public sealed record CreateStockTransferRequestCommand(
    DateOnly Date,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    IReadOnlyList<StockTransferLineInput> Lines,
    string? Narration = null) : ICommand<StockTransferResponse>, ITransactional;

/// <summary>Changes a request nobody has acted on yet.</summary>
/// <param name="StockTransferRequestId">The request.</param>
/// <param name="Date">The date the goods are asked for.</param>
/// <param name="FromWarehouseId">The warehouse that holds them.</param>
/// <param name="ToWarehouseId">The warehouse that wants them.</param>
/// <param name="Lines">What is wanted, whole.</param>
/// <param name="Narration">A note for the branch that will send them.</param>
public sealed record UpdateStockTransferRequestCommand(
    Guid StockTransferRequestId,
    DateOnly Date,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    IReadOnlyList<StockTransferLineInput> Lines,
    string? Narration = null) : ICommand<StockTransferResponse>, ITransactional;

/// <summary>Agrees to a request.</summary>
/// <param name="StockTransferRequestId">The request.</param>
/// <param name="Lines">
/// The quantities agreed where they differ from what was asked for; null approves
/// everything as asked.
/// </param>
public sealed record ApproveStockTransferCommand(
    Guid StockTransferRequestId,
    IReadOnlyList<StockTransferApprovalInput>? Lines = null)
    : ICommand<StockTransferResponse>, ITransactional;

/// <summary>Turns a request down.</summary>
/// <param name="StockTransferRequestId">The request.</param>
/// <param name="Reason">Why.</param>
public sealed record RejectStockTransferCommand(Guid StockTransferRequestId, string Reason)
    : ICommand<StockTransferResponse>, ITransactional;

/// <summary>Withdraws a request before anything has moved.</summary>
/// <param name="StockTransferRequestId">The request.</param>
/// <param name="Reason">Why.</param>
public sealed record CancelStockTransferCommand(Guid StockTransferRequestId, string Reason)
    : ICommand<StockTransferResponse>, ITransactional;

/// <summary>Sends the approved goods: out of the source and onto the road.</summary>
/// <param name="StockTransferRequestId">The request.</param>
/// <param name="Date">The dispatch date.</param>
/// <param name="Lines">
/// What went, with the batches and serial numbers picked from the shelf - the same lines a
/// direct transfer takes.
/// </param>
/// <param name="Narration">A note for the dispatch.</param>
public sealed record DispatchStockTransferCommand(
    Guid StockTransferRequestId,
    DateOnly Date,
    IReadOnlyList<StockDocumentLineInput> Lines,
    string? Narration = null) : ICommand<StockTransferResponse>, ITransactional;

/// <summary>Takes the dispatched goods into the destination.</summary>
/// <param name="StockTransferRequestId">The request.</param>
/// <param name="Date">The date they arrived.</param>
public sealed record ReceiveStockTransferCommand(Guid StockTransferRequestId, DateOnly Date)
    : ICommand<StockTransferResponse>, ITransactional;

/// <summary>A request as an action left it.</summary>
/// <param name="StockTransferRequestId">The request.</param>
/// <param name="Number">Its number.</param>
/// <param name="Status">Where it now stands.</param>
/// <param name="DocumentNumber">The transfer a dispatch or a receipt raised, where one did.</param>
public sealed record StockTransferResponse(
    Guid StockTransferRequestId,
    string Number,
    StockTransferStatus Status,
    string? DocumentNumber = null);

/// <summary>Lists transfer requests.</summary>
/// <param name="From">The earliest request date.</param>
/// <param name="To">The latest request date.</param>
/// <param name="Status">One state, or null for all.</param>
/// <param name="WarehouseId">Either end of the transfer, or null for all.</param>
public sealed record ListStockTransferRequestsQuery(
    DateOnly From,
    DateOnly To,
    StockTransferStatus? Status = null,
    Guid? WarehouseId = null) : IQuery<IReadOnlyList<StockTransferSummary>>;

/// <summary>Reads one transfer request.</summary>
/// <param name="StockTransferRequestId">The request.</param>
public sealed record GetStockTransferRequestQuery(Guid StockTransferRequestId)
    : IQuery<StockTransferDetail>;

/// <summary>A transfer request as the list shows it.</summary>
/// <param name="Id">The request.</param>
/// <param name="Number">Its number.</param>
/// <param name="Date">The date the goods were asked for.</param>
/// <param name="FromWarehouseId">The warehouse that holds them.</param>
/// <param name="FromWarehouseName">The sending warehouse's name.</param>
/// <param name="ToWarehouseId">The warehouse that wants them.</param>
/// <param name="ToWarehouseName">The receiving warehouse's name.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="LineCount">How many products it asks for.</param>
/// <param name="RequestedQuantity">The total asked for, in stock units.</param>
/// <param name="DispatchedQuantity">The total sent, in stock units.</param>
/// <param name="Narration">The requester's note.</param>
/// <param name="DispatchNumber">The transfer that sent the goods.</param>
/// <param name="ReceiptNumber">The transfer that received them.</param>
public sealed record StockTransferSummary(
    Guid Id,
    string Number,
    DateOnly Date,
    Guid FromWarehouseId,
    string FromWarehouseName,
    Guid ToWarehouseId,
    string ToWarehouseName,
    StockTransferStatus Status,
    int LineCount,
    decimal RequestedQuantity,
    decimal DispatchedQuantity,
    string? Narration,
    string? DispatchNumber,
    string? ReceiptNumber);

/// <summary>One product on a transfer request, as the detail screen shows it.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductName">Its description.</param>
/// <param name="UnitId">The unit it was asked for in.</param>
/// <param name="UnitCode">That unit's code.</param>
/// <param name="StockUnitCode">The product's stock unit.</param>
/// <param name="Quantity">The quantity asked for, in that unit.</param>
/// <param name="StockQuantity">The quantity asked for, in the stock unit.</param>
/// <param name="ApprovedQuantity">The quantity agreed, in the stock unit.</param>
/// <param name="DispatchedQuantity">The quantity sent, in the stock unit.</param>
/// <param name="AvailableAtSource">What the sending warehouse holds today, in the stock unit.</param>
/// <param name="TracksBatches">Whether the dispatch has to name batches.</param>
/// <param name="TracksSerialNumbers">Whether the dispatch may name serial numbers.</param>
/// <param name="Remarks">The line's remark.</param>
public sealed record StockTransferLineDetail(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid UnitId,
    string UnitCode,
    string StockUnitCode,
    decimal Quantity,
    decimal StockQuantity,
    decimal ApprovedQuantity,
    decimal DispatchedQuantity,
    decimal AvailableAtSource,
    bool TracksBatches,
    bool TracksSerialNumbers,
    string? Remarks);

/// <summary>A transfer request with its lines and its history.</summary>
/// <param name="Summary">What the list shows.</param>
/// <param name="Reason">Why it was rejected or cancelled.</param>
/// <param name="CreatedAtUtc">When it was asked for.</param>
/// <param name="DecidedAtUtc">When it was approved or rejected.</param>
/// <param name="DispatchDocumentId">The transfer that sent the goods.</param>
/// <param name="DispatchedAtUtc">When they were sent.</param>
/// <param name="ReceiptDocumentId">The transfer that received them.</param>
/// <param name="ReceivedAtUtc">When they arrived.</param>
/// <param name="Lines">The products.</param>
public sealed record StockTransferDetail(
    StockTransferSummary Summary,
    string? Reason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    Guid? DispatchDocumentId,
    DateTimeOffset? DispatchedAtUtc,
    Guid? ReceiptDocumentId,
    DateTimeOffset? ReceivedAtUtc,
    IReadOnlyList<StockTransferLineDetail> Lines);

/// <summary>Reads transfer requests for the list and the detail screens.</summary>
public interface IStockTransferReader
{
    /// <summary>Lists requests in a date range.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="from">The earliest date.</param>
    /// <param name="to">The latest date.</param>
    /// <param name="status">One state, or null for all.</param>
    /// <param name="warehouseId">Either end, or null for all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requests, newest first.</returns>
    Task<IReadOnlyList<StockTransferSummary>> ListAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        StockTransferStatus? status,
        WarehouseId? warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one request.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="requestId">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request, or <see langword="null"/>.</returns>
    Task<StockTransferDetail?> FindAsync(
        FirmId firmId,
        StockTransferRequestId requestId,
        CancellationToken cancellationToken = default);
}

/// <summary>The refusals the transfer rules share.</summary>
internal static class StockTransferRules
{
    /// <summary>The refusal for a direct transfer where the firm asks for requests.</summary>
    /// <returns>The error.</returns>
    internal static Error RequestRequired() => Error.BusinessRule(
        "StockTransfer.RequestRequired",
        "This firm moves stock between warehouses only on an approved transfer request. "
        + "Raise a request, have it approved, then dispatch it.");
}

/// <summary>Validates <see cref="CreateStockTransferRequestCommand"/>.</summary>
public sealed class CreateStockTransferRequestCommandValidator
    : AbstractValidator<CreateStockTransferRequestCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateStockTransferRequestCommandValidator"/> class.</summary>
    public CreateStockTransferRequestCommandValidator()
    {
        RuleFor(c => c.Date).NotEqual(default(DateOnly)).WithMessage("A request date is required.");
        RuleFor(c => c.FromWarehouseId).NotEqual(Guid.Empty);
        RuleFor(c => c.ToWarehouseId).NotEqual(Guid.Empty);
        RuleFor(c => c.Lines).NotEmpty().WithMessage("Ask for at least one product.");
        RuleForEach(c => c.Lines).SetValidator(new StockTransferLineInputValidator());
        RuleFor(c => c.Narration).MaximumLength(StockTransferRequest.MaximumNarrationLength);
    }
}

/// <summary>Validates <see cref="UpdateStockTransferRequestCommand"/>.</summary>
public sealed class UpdateStockTransferRequestCommandValidator
    : AbstractValidator<UpdateStockTransferRequestCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateStockTransferRequestCommandValidator"/> class.</summary>
    public UpdateStockTransferRequestCommandValidator()
    {
        RuleFor(c => c.StockTransferRequestId).NotEqual(Guid.Empty);
        RuleFor(c => c.Date).NotEqual(default(DateOnly)).WithMessage("A request date is required.");
        RuleFor(c => c.FromWarehouseId).NotEqual(Guid.Empty);
        RuleFor(c => c.ToWarehouseId).NotEqual(Guid.Empty);
        RuleFor(c => c.Lines).NotEmpty().WithMessage("Ask for at least one product.");
        RuleForEach(c => c.Lines).SetValidator(new StockTransferLineInputValidator());
        RuleFor(c => c.Narration).MaximumLength(StockTransferRequest.MaximumNarrationLength);
    }
}

/// <summary>Validates one requested line.</summary>
public sealed class StockTransferLineInputValidator : AbstractValidator<StockTransferLineInput>
{
    /// <summary>Initialises a new instance of the <see cref="StockTransferLineInputValidator"/> class.</summary>
    public StockTransferLineInputValidator()
    {
        RuleFor(l => l.ProductId).NotEqual(Guid.Empty).WithMessage("Each line must name a product.");
        RuleFor(l => l.Quantity).GreaterThan(0m).WithMessage("Ask for a quantity above zero.");
        RuleFor(l => l.Remarks).MaximumLength(StockTransferRequest.MaximumNarrationLength);
    }
}

/// <summary>Validates <see cref="RejectStockTransferCommand"/>.</summary>
public sealed class RejectStockTransferCommandValidator : AbstractValidator<RejectStockTransferCommand>
{
    /// <summary>Initialises a new instance of the <see cref="RejectStockTransferCommandValidator"/> class.</summary>
    public RejectStockTransferCommandValidator()
    {
        RuleFor(c => c.StockTransferRequestId).NotEqual(Guid.Empty);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(StockTransferRequest.MaximumNarrationLength);
    }
}

/// <summary>Validates <see cref="CancelStockTransferCommand"/>.</summary>
public sealed class CancelStockTransferCommandValidator : AbstractValidator<CancelStockTransferCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CancelStockTransferCommandValidator"/> class.</summary>
    public CancelStockTransferCommandValidator()
    {
        RuleFor(c => c.StockTransferRequestId).NotEqual(Guid.Empty);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(StockTransferRequest.MaximumNarrationLength);
    }
}

/// <summary>Validates <see cref="DispatchStockTransferCommand"/>.</summary>
public sealed class DispatchStockTransferCommandValidator
    : AbstractValidator<DispatchStockTransferCommand>
{
    /// <summary>Initialises a new instance of the <see cref="DispatchStockTransferCommandValidator"/> class.</summary>
    public DispatchStockTransferCommandValidator()
    {
        RuleFor(c => c.StockTransferRequestId).NotEqual(Guid.Empty);
        RuleFor(c => c.Date).NotEqual(default(DateOnly)).WithMessage("A dispatch date is required.");
        RuleFor(c => c.Lines).NotEmpty().WithMessage("A dispatch has to send something.");
        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty);
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.Rate).Equal(0m)
                .WithMessage("A transfer is valued at what the goods already cost.");
        });
        RuleFor(c => c.Narration).MaximumLength(StockDocument.MaximumNarrationLength);
    }
}
