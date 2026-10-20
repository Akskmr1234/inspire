using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Platform;

/// <summary>Identifies a change to a posted document waiting for approval.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct EditRequestId(Guid Value) : IStronglyTypedId<EditRequestId>
{
    /// <inheritdoc />
    public static EditRequestId From(Guid value) => new(value);

    /// <inheritdoc />
    public static EditRequestId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>The kinds of posted document whose changes can be held for approval.</summary>
public enum EditDocumentKind
{
    /// <summary>A sale or a sales return.</summary>
    SalesInvoice = 1,

    /// <summary>A purchase or a purchase return.</summary>
    PurchaseInvoice = 2,

    /// <summary>A receipt, payment, journal or contra.</summary>
    Voucher = 3,
}

/// <summary>What is being asked for.</summary>
public enum EditAction
{
    /// <summary>The document rewritten as given.</summary>
    Edit = 1,

    /// <summary>The document cancelled.</summary>
    Cancel = 2,
}

/// <summary>Where a request stands.</summary>
public enum EditRequestStatus
{
    /// <summary>Waiting for somebody who may approve it.</summary>
    Pending = 1,

    /// <summary>Approved, and the change made.</summary>
    Approved = 2,

    /// <summary>Refused; the document stands as it was.</summary>
    Rejected = 3,

    /// <summary>Taken back by whoever asked.</summary>
    Withdrawn = 4,
}

/// <summary>
/// A change to a posted document, held until somebody allowed to approve it does.
/// </summary>
/// <remarks>
/// Where the firm requires it, an edit or a cancellation of a posted document by somebody
/// without the right to approve it is not made: it is kept here, exactly as it was asked
/// for, and made only when an approver accepts it - by the same path, with the same checks,
/// as if the approver had made it themselves. Until then the document stands as it was.
/// </remarks>
public sealed class EditRequest : AggregateRoot<EditRequestId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a reason or a note may be.</summary>
    public const int MaximumTextLength = 1000;

    private EditRequest(
        EditRequestId id,
        TenantId tenantId,
        FirmId firmId,
        EditDocumentKind kind,
        Guid documentId,
        string documentNumber,
        EditAction action,
        string payload,
        string? reason,
        UserId requestedBy,
        string requestedByName,
        DateTimeOffset requestedAt)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Kind = kind;
        DocumentId = documentId;
        DocumentNumber = documentNumber;
        Action = action;
        Payload = payload;
        Reason = reason;
        RequestedBy = requestedBy;
        RequestedByName = requestedByName;
        RequestedAtUtc = requestedAt;
        Status = EditRequestStatus.Pending;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private EditRequest()
    {
        DocumentNumber = string.Empty;
        Payload = string.Empty;
        RequestedByName = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the kind of document.</summary>
    public EditDocumentKind Kind { get; private set; }

    /// <summary>Gets the document.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Gets the document's number.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>Gets what is asked for.</summary>
    public EditAction Action { get; private set; }

    /// <summary>Gets the change exactly as it was submitted, as JSON.</summary>
    public string Payload { get; private set; }

    /// <summary>Gets why the change is wanted.</summary>
    public string? Reason { get; private set; }

    /// <summary>Gets where the request stands.</summary>
    public EditRequestStatus Status { get; private set; }

    /// <summary>Gets who asked.</summary>
    public UserId RequestedBy { get; private set; }

    /// <summary>Gets their name at the time.</summary>
    public string RequestedByName { get; private set; }

    /// <summary>Gets when they asked.</summary>
    public DateTimeOffset RequestedAtUtc { get; private set; }

    /// <summary>Gets who approved or refused it.</summary>
    public UserId? DecidedBy { get; private set; }

    /// <summary>Gets their name at the time.</summary>
    public string? DecidedByName { get; private set; }

    /// <summary>Gets when.</summary>
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>Gets the approver's note, or why it was refused.</summary>
    public string? DecisionNote { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Holds a change for approval.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="kind">The kind of document.</param>
    /// <param name="documentId">The document.</param>
    /// <param name="documentNumber">Its number.</param>
    /// <param name="action">What is asked for.</param>
    /// <param name="payload">The change as submitted, as JSON.</param>
    /// <param name="reason">Why.</param>
    /// <param name="requestedBy">Who asked.</param>
    /// <param name="requestedByName">Their name.</param>
    /// <param name="at">When.</param>
    /// <returns>The request, or the reason it was refused.</returns>
    public static Result<EditRequest> Create(
        TenantId tenantId,
        FirmId firmId,
        EditDocumentKind kind,
        Guid documentId,
        string documentNumber,
        EditAction action,
        string payload,
        string? reason,
        UserId requestedBy,
        string requestedByName,
        DateTimeOffset at)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(action))
        {
            return Result.Failure<EditRequest>(Error.Validation(
                "EditRequest.Unknown", "No such document kind or action."));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Result.Failure<EditRequest>(Error.Validation(
                "EditRequest.Empty", "There is no change to hold for approval."));
        }

        return Result.Success(new EditRequest(
            EditRequestId.NewId(), tenantId, firmId, kind, documentId, documentNumber.Trim(), action, payload,
            Clip(reason), requestedBy, requestedByName, at));
    }

    /// <summary>Records that the change was approved and made.</summary>
    /// <param name="by">Who approved it.</param>
    /// <param name="name">Their name.</param>
    /// <param name="at">When.</param>
    /// <param name="note">A note.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Approve(UserId by, string name, DateTimeOffset at, string? note) =>
        Decide(EditRequestStatus.Approved, by, name, at, note);

    /// <summary>Refuses the change.</summary>
    /// <param name="by">Who refused it.</param>
    /// <param name="name">Their name.</param>
    /// <param name="at">When.</param>
    /// <param name="note">Why. Required.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Reject(UserId by, string name, DateTimeOffset at, string note) =>
        string.IsNullOrWhiteSpace(note)
            ? Result.Failure(Error.Validation("EditRequest.ReasonRequired", "Say why the change is refused."))
            : Decide(EditRequestStatus.Rejected, by, name, at, note);

    /// <summary>Takes the request back.</summary>
    /// <param name="by">Who is withdrawing it; only whoever asked may.</param>
    /// <param name="at">When.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Withdraw(UserId by, DateTimeOffset at)
    {
        if (by != RequestedBy)
        {
            return Result.Failure(Error.Forbidden(
                "EditRequest.NotYours", "Only whoever asked for a change can withdraw it."));
        }

        return Decide(EditRequestStatus.Withdrawn, by, RequestedByName, at, null);
    }

    private static string? Clip(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= MaximumTextLength ? trimmed : trimmed[..MaximumTextLength];
    }

    private Result Decide(EditRequestStatus status, UserId by, string name, DateTimeOffset at, string? note)
    {
        if (Status != EditRequestStatus.Pending)
        {
            return Result.Failure(Error.BusinessRule(
                "EditRequest.Decided", $"The change to '{DocumentNumber}' was already {Status}."));
        }

        Status = status;
        DecidedBy = by;
        DecidedByName = name;
        DecidedAtUtc = at;
        DecisionNote = Clip(note);

        return Result.Success();
    }
}
