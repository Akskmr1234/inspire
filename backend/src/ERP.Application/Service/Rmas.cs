using ERP.Application.Abstractions.Messaging;
using ERP.Domain.Service;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Service;

/// <summary>One product going back, as entered.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Quantity">How many, in its stock unit.</param>
/// <param name="Reason">What is wrong with these in particular.</param>
/// <param name="SerialNumber">The serial number or IMEI of the unit.</param>
/// <param name="BatchId">The batch, for a product tracked in batches.</param>
public sealed record RmaLineInput(
    Guid ProductId,
    decimal Quantity,
    string? Reason = null,
    string? SerialNumber = null,
    Guid? BatchId = null);

/// <summary>What a return says, as entered.</summary>
/// <param name="Reason">Why the goods are going back.</param>
/// <param name="Narration">A note.</param>
/// <param name="InvoiceReference">The invoice as the party quoted it.</param>
/// <param name="SalesInvoiceId">The sale the goods came from, for a customer's return.</param>
/// <param name="PurchaseInvoiceId">The purchase they came in on, for a return to a supplier.</param>
/// <param name="Lines">The products going back, whole.</param>
public sealed record RmaInput(
    string Reason,
    string? Narration,
    string? InvoiceReference,
    Guid? SalesInvoiceId,
    Guid? PurchaseInvoiceId,
    IReadOnlyList<RmaLineInput> Lines);

/// <summary>Asks for a return.</summary>
/// <param name="Date">The day it was asked for.</param>
/// <param name="Direction">Which way the goods go back.</param>
/// <param name="PartyLedgerId">The customer, or the supplier.</param>
/// <param name="WarehouseId">The godown the returned goods are kept in.</param>
/// <param name="Rma">What it says.</param>
public sealed record CreateRmaCommand(
    DateOnly Date,
    RmaDirection Direction,
    Guid PartyLedgerId,
    Guid WarehouseId,
    RmaInput Rma) : ICommand<Guid>, ITransactional;

/// <summary>Replaces what a return still awaiting approval says.</summary>
/// <param name="RmaId">The return.</param>
/// <param name="Rma">What it says now.</param>
public sealed record UpdateRmaCommand(Guid RmaId, RmaInput Rma) : ICommand, ITransactional;

/// <summary>The steps of a return that need nothing but a note.</summary>
public enum RmaStep
{
    /// <summary>Approve it.</summary>
    Approve = 1,

    /// <summary>Refuse it.</summary>
    Reject = 2,

    /// <summary>Withdraw it.</summary>
    Cancel = 3,

    /// <summary>The goods changed hands.</summary>
    Receive = 4,

    /// <summary>Repaired and handed back.</summary>
    Repair = 5,

    /// <summary>Close it.</summary>
    Close = 6,
}

/// <summary>Takes a step on a return.</summary>
/// <param name="RmaId">The return.</param>
/// <param name="Step">The step.</param>
/// <param name="Note">A note; the reason, for a refusal or a withdrawal.</param>
public sealed record RmaStepCommand(Guid RmaId, RmaStep Step, string? Note) : ICommand, ITransactional;

/// <summary>Records what the inspection found.</summary>
/// <param name="RmaId">The return.</param>
/// <param name="Condition">The condition the goods are in.</param>
/// <param name="Notes">What was found.</param>
/// <param name="On">The day; today where none is given.</param>
public sealed record InspectRmaCommand(Guid RmaId, RmaCondition Condition, string? Notes, DateOnly? On = null)
    : ICommand, ITransactional;

/// <summary>Keeps a photograph as evidence.</summary>
/// <param name="RmaId">The return.</param>
/// <param name="Content">The picture, as a data URL.</param>
/// <param name="Caption">What it shows.</param>
public sealed record AddRmaPhotoCommand(Guid RmaId, string Content, string? Caption) : ICommand<Guid>, ITransactional;

/// <summary>Removes a photograph.</summary>
/// <param name="RmaId">The return.</param>
/// <param name="PhotoId">The photograph.</param>
public sealed record RemoveRmaPhotoCommand(Guid RmaId, Guid PhotoId) : ICommand, ITransactional;

/// <summary>Reads one photograph.</summary>
/// <param name="RmaId">The return.</param>
/// <param name="PhotoId">The photograph.</param>
public sealed record GetRmaPhotoQuery(Guid RmaId, Guid PhotoId) : IQuery<string>;

/// <summary>Exchanges the goods.</summary>
/// <param name="RmaId">The return.</param>
/// <param name="StockWarehouseId">
/// The godown of saleable stock: where a customer's replacement comes from, or where a
/// supplier's replacement goes. Where it is the return's own godown, nothing moves.
/// </param>
/// <param name="Date">The day; today where none is given.</param>
/// <param name="Note">A note.</param>
public sealed record ReplaceRmaCommand(Guid RmaId, Guid StockWarehouseId, DateOnly? Date = null, string? Note = null)
    : ICommand<RmaOutcomeResponse>, ITransactional;

/// <summary>Settles the return with a draft credit note to the customer, or debit note to the supplier.</summary>
/// <param name="RmaId">The return.</param>
/// <param name="Date">The note's date; today where none is given.</param>
/// <param name="Note">A note.</param>
public sealed record RefundRmaCommand(Guid RmaId, DateOnly? Date = null, string? Note = null)
    : ICommand<RmaOutcomeResponse>, ITransactional;

/// <summary>The document an outcome raised.</summary>
/// <param name="DocumentId">The document, where one was raised.</param>
/// <param name="Number">Its number.</param>
public sealed record RmaOutcomeResponse(Guid? DocumentId, string? Number);

/// <summary>Lists returns.</summary>
/// <param name="Direction">Only those going this way.</param>
/// <param name="Status">Only those standing here.</param>
/// <param name="Search">Text in the number, the party, the invoice reference or a serial number.</param>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
public sealed record ListRmasQuery(
    RmaDirection? Direction = null,
    RmaStatus? Status = null,
    string? Search = null,
    DateOnly? From = null,
    DateOnly? To = null) : IQuery<IReadOnlyList<RmaSummary>>;

/// <summary>Reads one return.</summary>
/// <param name="RmaId">The return.</param>
public sealed record GetRmaQuery(Guid RmaId) : IQuery<RmaDetail>;

/// <summary>A return as the list shows it.</summary>
/// <param name="Id">The return.</param>
/// <param name="Number">Its number.</param>
/// <param name="Date">The day it was asked for.</param>
/// <param name="Direction">Which way the goods go back.</param>
/// <param name="PartyLedgerId">The customer or supplier.</param>
/// <param name="PartyName">Their name.</param>
/// <param name="InvoiceReference">The invoice quoted, or the one linked.</param>
/// <param name="Reason">Why.</param>
/// <param name="Quantity">How many units are going back.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Condition">What the inspection found.</param>
/// <param name="OutcomeNumber">The credit note, debit note or transfer that settled it.</param>
public sealed record RmaSummary(
    Guid Id,
    string Number,
    DateOnly Date,
    RmaDirection Direction,
    Guid PartyLedgerId,
    string PartyName,
    string? InvoiceReference,
    string Reason,
    decimal Quantity,
    RmaStatus Status,
    RmaCondition? Condition,
    string? OutcomeNumber);

/// <summary>A product going back, as the return shows it.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductName">Its description.</param>
/// <param name="StockUnitCode">Its stock unit.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Reason">What is wrong with them.</param>
/// <param name="SerialNumber">The serial number or IMEI.</param>
/// <param name="TracksBatches">Whether it is tracked in batches.</param>
/// <param name="BatchId">The batch.</param>
/// <param name="BatchNumber">Its number.</param>
public sealed record RmaLineDetail(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string StockUnitCode,
    decimal Quantity,
    string? Reason,
    string? SerialNumber,
    bool TracksBatches,
    Guid? BatchId,
    string? BatchNumber);

/// <summary>A photograph, without the picture itself.</summary>
/// <param name="Id">The photograph.</param>
/// <param name="Caption">What it shows.</param>
public sealed record RmaPhotoSummary(Guid Id, string? Caption);

/// <summary>One step in the history.</summary>
/// <param name="Status">The status the step left it in.</param>
/// <param name="At">When.</param>
/// <param name="UserName">Who.</param>
/// <param name="Note">Their note.</param>
public sealed record RmaEventDetail(RmaStatus Status, DateTimeOffset At, string UserName, string? Note);

/// <summary>A return in full.</summary>
/// <param name="Summary">What the list shows.</param>
/// <param name="WarehouseId">The godown the returned goods are kept in.</param>
/// <param name="WarehouseName">The godown's name.</param>
/// <param name="SalesInvoiceId">The sale linked.</param>
/// <param name="PurchaseInvoiceId">The purchase linked.</param>
/// <param name="LinkedInvoiceNumber">The linked invoice's number.</param>
/// <param name="Narration">The note.</param>
/// <param name="InspectionNotes">What the inspection found.</param>
/// <param name="InspectedOn">When it was inspected.</param>
/// <param name="ReplacementDocumentId">The transfer that exchanged the goods.</param>
/// <param name="CreditNoteId">The credit note that refunded the customer.</param>
/// <param name="DebitNoteId">The debit note that charged the supplier back.</param>
/// <param name="Lines">The products going back.</param>
/// <param name="Photos">The photographs kept.</param>
/// <param name="History">What has happened, oldest first.</param>
public sealed record RmaDetail(
    RmaSummary Summary,
    Guid WarehouseId,
    string WarehouseName,
    Guid? SalesInvoiceId,
    Guid? PurchaseInvoiceId,
    string? LinkedInvoiceNumber,
    string? Narration,
    string? InspectionNotes,
    DateOnly? InspectedOn,
    Guid? ReplacementDocumentId,
    Guid? CreditNoteId,
    Guid? DebitNoteId,
    IReadOnlyList<RmaLineDetail> Lines,
    IReadOnlyList<RmaPhotoSummary> Photos,
    IReadOnlyList<RmaEventDetail> History);

/// <summary>Reads returns for the screens.</summary>
public interface IRmaReader
{
    /// <summary>Lists a firm's returns, newest first.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="query">The filters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The returns.</returns>
    Task<IReadOnlyList<RmaSummary>> ListAsync(
        FirmId firmId,
        ListRmasQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one return.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="id">The return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The return, or <see langword="null"/>.</returns>
    Task<RmaDetail?> FindAsync(FirmId firmId, RmaId id, CancellationToken cancellationToken = default);

    /// <summary>Reads one photograph's picture.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="id">The return.</param>
    /// <param name="photoId">The photograph.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The picture as a data URL, or <see langword="null"/>.</returns>
    Task<string?> FindPhotoAsync(
        FirmId firmId,
        RmaId id,
        RmaPhotoId photoId,
        CancellationToken cancellationToken = default);
}

/// <summary>Validates what a return says.</summary>
public sealed class RmaInputValidator : AbstractValidator<RmaInput>
{
    /// <summary>Initialises a new instance of the <see cref="RmaInputValidator"/> class.</summary>
    public RmaInputValidator()
    {
        RuleFor(r => r.Reason).NotEmpty().WithMessage("Say why the goods are going back.")
            .MaximumLength(Rma.MaximumTextLength);
        RuleFor(r => r.Lines).NotEmpty().WithMessage("A return needs at least one product.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty);
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.Reason).MaximumLength(Rma.MaximumTextLength);
        });
    }
}

/// <summary>Validates <see cref="CreateRmaCommand"/>.</summary>
public sealed class CreateRmaCommandValidator : AbstractValidator<CreateRmaCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateRmaCommandValidator"/> class.</summary>
    public CreateRmaCommandValidator()
    {
        RuleFor(c => c.Direction).IsInEnum();
        RuleFor(c => c.PartyLedgerId).NotEqual(Guid.Empty);
        RuleFor(c => c.WarehouseId).NotEqual(Guid.Empty);
        RuleFor(c => c.Rma).NotNull().SetValidator(new RmaInputValidator());
    }
}

/// <summary>Validates <see cref="UpdateRmaCommand"/>.</summary>
public sealed class UpdateRmaCommandValidator : AbstractValidator<UpdateRmaCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateRmaCommandValidator"/> class.</summary>
    public UpdateRmaCommandValidator()
    {
        RuleFor(c => c.RmaId).NotEqual(Guid.Empty);
        RuleFor(c => c.Rma).NotNull().SetValidator(new RmaInputValidator());
    }
}

/// <summary>Validates <see cref="RmaStepCommand"/>.</summary>
public sealed class RmaStepCommandValidator : AbstractValidator<RmaStepCommand>
{
    /// <summary>Initialises a new instance of the <see cref="RmaStepCommandValidator"/> class.</summary>
    public RmaStepCommandValidator()
    {
        RuleFor(c => c.RmaId).NotEqual(Guid.Empty);
        RuleFor(c => c.Step).IsInEnum();
        RuleFor(c => c.Note).MaximumLength(Rma.MaximumTextLength);
    }
}

/// <summary>Validates <see cref="InspectRmaCommand"/>.</summary>
public sealed class InspectRmaCommandValidator : AbstractValidator<InspectRmaCommand>
{
    /// <summary>Initialises a new instance of the <see cref="InspectRmaCommandValidator"/> class.</summary>
    public InspectRmaCommandValidator()
    {
        RuleFor(c => c.RmaId).NotEqual(Guid.Empty);
        RuleFor(c => c.Condition).IsInEnum();
        RuleFor(c => c.Notes).MaximumLength(Rma.MaximumTextLength);
    }
}

/// <summary>Validates <see cref="AddRmaPhotoCommand"/>.</summary>
public sealed class AddRmaPhotoCommandValidator : AbstractValidator<AddRmaPhotoCommand>
{
    /// <summary>Initialises a new instance of the <see cref="AddRmaPhotoCommandValidator"/> class.</summary>
    public AddRmaPhotoCommandValidator()
    {
        RuleFor(c => c.RmaId).NotEqual(Guid.Empty);
        RuleFor(c => c.Content).NotEmpty().MaximumLength(RmaPhoto.MaximumLength);
    }
}
