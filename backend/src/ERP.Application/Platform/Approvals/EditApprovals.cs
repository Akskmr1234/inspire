using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Platform.Settings;
using ERP.Domain.Accounting;
using ERP.Domain.Platform;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Platform.Approvals;

/// <summary>Asks whether a change to a document has to wait for approval.</summary>
/// <param name="Kind">The kind of document.</param>
/// <param name="DocumentId">The document.</param>
public sealed record EditGateQuery(EditDocumentKind Kind, Guid DocumentId) : IQuery<EditGate>;

/// <summary>Whether a change must wait for approval.</summary>
/// <param name="RequiresApproval">True when the firm requires approval, the document is posted, and the user may not approve it.</param>
public sealed record EditGate(bool RequiresApproval);

/// <summary>Holds a change to a posted document for approval.</summary>
/// <param name="Kind">The kind of document.</param>
/// <param name="DocumentId">The document.</param>
/// <param name="Action">Edit or cancel.</param>
/// <param name="Payload">The change exactly as submitted, as JSON.</param>
/// <param name="Reason">Why the change is wanted.</param>
public sealed record RequestEditApprovalCommand(
    EditDocumentKind Kind,
    Guid DocumentId,
    EditAction Action,
    string Payload,
    string? Reason) : ICommand<EditRequestSummary>, ITransactional;

/// <summary>Lists the changes waiting for, or past, approval that the user may see.</summary>
/// <param name="Status">Only requests standing here.</param>
public sealed record ListEditRequestsQuery(EditRequestStatus? Status = null)
    : IQuery<IReadOnlyList<EditRequestSummary>>;

/// <summary>Reads one request with the change it holds.</summary>
/// <param name="EditRequestId">The request.</param>
public sealed record GetEditRequestQuery(Guid EditRequestId) : IQuery<EditRequestDetail>;

/// <summary>Settles a request: approved (once the change is made), rejected, or withdrawn.</summary>
/// <param name="EditRequestId">The request.</param>
/// <param name="Decision">Approved, Rejected or Withdrawn.</param>
/// <param name="Note">A note; why, for a rejection.</param>
public sealed record DecideEditRequestCommand(Guid EditRequestId, EditRequestStatus Decision, string? Note)
    : ICommand, ITransactional;

/// <summary>A request as the list shows it.</summary>
/// <param name="Id">The request.</param>
/// <param name="Kind">The kind of document.</param>
/// <param name="DocumentId">The document.</param>
/// <param name="DocumentNumber">Its number.</param>
/// <param name="Action">Edit or cancel.</param>
/// <param name="Reason">Why.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="RequestedByName">Who asked.</param>
/// <param name="RequestedAtUtc">When they asked.</param>
/// <param name="DecidedByName">Who decided.</param>
/// <param name="DecidedAtUtc">When it was decided.</param>
/// <param name="DecisionNote">The decider's note.</param>
/// <param name="CanDecide">Whether the user may approve or refuse it.</param>
/// <param name="IsOwn">Whether the user asked for it, and so may withdraw it.</param>
public sealed record EditRequestSummary(
    Guid Id,
    EditDocumentKind Kind,
    Guid DocumentId,
    string DocumentNumber,
    EditAction Action,
    string? Reason,
    EditRequestStatus Status,
    string RequestedByName,
    DateTimeOffset RequestedAtUtc,
    string? DecidedByName,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote,
    bool CanDecide,
    bool IsOwn);

/// <summary>A request with the change it holds.</summary>
/// <param name="Summary">What the list shows.</param>
/// <param name="Payload">The change as submitted, as JSON.</param>
public sealed record EditRequestDetail(EditRequestSummary Summary, string Payload);

/// <summary>Reads and writes changes held for approval.</summary>
public interface IEditRequestRepository
{
    /// <summary>Finds a request.</summary>
    /// <param name="id">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request, or <see langword="null"/>.</returns>
    Task<EditRequest?> FindAsync(EditRequestId id, CancellationToken cancellationToken = default);

    /// <summary>Says whether a document already has a change waiting.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The waiting request, or <see langword="null"/>.</returns>
    Task<EditRequest?> FindPendingAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Lists a firm's requests, newest first.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="status">Only those standing here.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requests.</returns>
    Task<IReadOnlyList<EditRequest>> ListAsync(
        FirmId firmId,
        EditRequestStatus? status,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a request.</summary>
    /// <param name="request">The request to add.</param>
    void Add(EditRequest request);
}

/// <summary>Handles holding changes for approval and settling them.</summary>
public sealed class EditApprovalHandlers
    : IQueryHandler<EditGateQuery, EditGate>,
      ICommandHandler<RequestEditApprovalCommand, EditRequestSummary>,
      IQueryHandler<ListEditRequestsQuery, IReadOnlyList<EditRequestSummary>>,
      IQueryHandler<GetEditRequestQuery, EditRequestDetail>,
      ICommandHandler<DecideEditRequestCommand>
{
    private readonly IEditRequestRepository _requests;
    private readonly ISalesInvoiceRepository _sales;
    private readonly IPurchaseInvoiceRepository _purchases;
    private readonly IVoucherRepository _vouchers;
    private readonly IFirmPolicyReader _policies;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="EditApprovalHandlers"/> class.</summary>
    /// <param name="requests">The request repository.</param>
    /// <param name="sales">The sales invoice repository.</param>
    /// <param name="purchases">The purchase invoice repository.</param>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="policies">The firm's policies.</param>
    /// <param name="permissions">The permission checker.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The acting user.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public EditApprovalHandlers(
        IEditRequestRepository requests,
        ISalesInvoiceRepository sales,
        IPurchaseInvoiceRepository purchases,
        IVoucherRepository vouchers,
        IFirmPolicyReader policies,
        IPermissionChecker permissions,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _requests = requests;
        _sales = sales;
        _purchases = purchases;
        _vouchers = vouchers;
        _policies = policies;
        _permissions = permissions;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>The permission that lets somebody make, or approve, a change to a kind of document.</summary>
    /// <param name="kind">The kind of document.</param>
    /// <returns>The permission code.</returns>
    public static string ApprovePermission(EditDocumentKind kind) => kind switch
    {
        EditDocumentKind.SalesInvoice => "sales:invoice:approve",
        EditDocumentKind.PurchaseInvoice => "purchase:invoice:approve",
        _ => "accounting:voucher:approve",
    };

    /// <inheritdoc />
    public async Task<Result<EditGate>> Handle(EditGateQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId
            || !(await _policies.GetAsync(firmId, cancellationToken)).EditsRequireApproval)
        {
            return Result.Success(new EditGate(false));
        }

        // A draft is still being written: nothing has moved, so there is nothing to approve.
        Posted? posted = await PostedAsync(request.Kind, request.DocumentId, firmId, cancellationToken);

        if (posted is null)
        {
            return Result.Success(new EditGate(false));
        }

        bool mayApprove = await _permissions.HasPermissionAsync(
            _currentUser.UserId, ApprovePermission(request.Kind), cancellationToken);

        return Result.Success(new EditGate(!mayApprove));
    }

    /// <inheritdoc />
    public async Task<Result<EditRequestSummary>> Handle(
        RequestEditApprovalCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<EditRequestSummary>(NoFirm());
        }

        Posted? posted = await PostedAsync(request.Kind, request.DocumentId, firmId, cancellationToken);

        if (posted is null)
        {
            return Result.Failure<EditRequestSummary>(Error.NotFound(
                "EditRequest.DocumentNotFound", "No such posted document in the selected firm."));
        }

        if (await _requests.FindPendingAsync(request.DocumentId, cancellationToken) is { } waiting)
        {
            return Result.Failure<EditRequestSummary>(Error.Conflict(
                "EditRequest.AlreadyPending",
                $"A change to '{waiting.DocumentNumber}' asked for by {waiting.RequestedByName} is already "
                + "waiting for approval. It has to be approved, refused or withdrawn first."));
        }

        Result<EditRequest> created = EditRequest.Create(
            _tenantContext.TenantId,
            firmId,
            request.Kind,
            request.DocumentId,
            posted.Number,
            request.Action,
            request.Payload,
            request.Reason,
            _currentUser.UserId,
            _currentUser.UserName ?? string.Empty,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<EditRequestSummary>(created.Error);
        }

        _requests.Add(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Summarise(created.Value, canDecide: false));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EditRequestSummary>>> Handle(
        ListEditRequestsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<EditRequestSummary>>(NoFirm());
        }

        IReadOnlyDictionary<EditDocumentKind, bool> granted = await GrantedAsync(cancellationToken);
        IReadOnlyList<EditRequest> requests = await _requests.ListAsync(firmId, request.Status, cancellationToken);

        // An approver sees what they may decide; anybody sees what they asked for themselves.
        return Result.Success<IReadOnlyList<EditRequestSummary>>(
        [
            .. requests
                .Select(row => (Row: row, CanDecide: granted[row.Kind]))
                .Where(pair => pair.CanDecide || pair.Row.RequestedBy == _currentUser.UserId)
                .Select(pair => Summarise(pair.Row, pair.CanDecide)),
        ]);
    }

    /// <inheritdoc />
    public async Task<Result<EditRequestDetail>> Handle(GetEditRequestQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<EditRequest> found = await FindAsync(request.EditRequestId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<EditRequestDetail>(found.Error);
        }

        bool canDecide = await _permissions.HasPermissionAsync(
            _currentUser.UserId, ApprovePermission(found.Value.Kind), cancellationToken);

        return canDecide || found.Value.RequestedBy == _currentUser.UserId
            ? Result.Success(new EditRequestDetail(Summarise(found.Value, canDecide), found.Value.Payload))
            : Result.Failure<EditRequestDetail>(NotFound());
    }

    /// <inheritdoc />
    public async Task<Result> Handle(DecideEditRequestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<EditRequest> found = await FindAsync(request.EditRequestId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        EditRequest edit = found.Value;
        string name = _currentUser.UserName ?? string.Empty;

        if (request.Decision is EditRequestStatus.Approved or EditRequestStatus.Rejected
            && !await _permissions.HasPermissionAsync(
                _currentUser.UserId, ApprovePermission(edit.Kind), cancellationToken))
        {
            return Result.Failure(Error.Forbidden(
                "EditRequest.MayNotDecide", "You may not approve or refuse changes to this kind of document."));
        }

        Result decided = request.Decision switch
        {
            EditRequestStatus.Approved => edit.Approve(_currentUser.UserId, name, _clock.UtcNow, request.Note),
            EditRequestStatus.Rejected => edit.Reject(_currentUser.UserId, name, _clock.UtcNow, request.Note ?? string.Empty),
            EditRequestStatus.Withdrawn => edit.Withdraw(_currentUser.UserId, _clock.UtcNow),
            _ => Result.Failure(Error.Validation("EditRequest.UnknownDecision", "Approve, reject or withdraw.")),
        };

        if (decided.IsFailure)
        {
            return decided;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Which kinds of document the user may approve changes to.</summary>
    private async Task<IReadOnlyDictionary<EditDocumentKind, bool>> GrantedAsync(CancellationToken cancellationToken)
    {
        Dictionary<EditDocumentKind, bool> granted = [];

        foreach (EditDocumentKind kind in Enum.GetValues<EditDocumentKind>())
        {
            granted[kind] = await _permissions.HasPermissionAsync(
                _currentUser.UserId, ApprovePermission(kind), cancellationToken);
        }

        return granted;
    }

    private static Error NoFirm() =>
        Error.Forbidden("EditRequest.NoFirmSelected", "A firm must be selected to work with approvals.");

    private static Error NotFound() =>
        Error.NotFound("EditRequest.NotFound", "No such change waiting for approval in the selected firm.");

    private EditRequestSummary Summarise(EditRequest request, bool canDecide) =>
        new(
            request.Id.Value,
            request.Kind,
            request.DocumentId,
            request.DocumentNumber,
            request.Action,
            request.Reason,
            request.Status,
            request.RequestedByName,
            request.RequestedAtUtc,
            request.DecidedByName,
            request.DecidedAtUtc,
            request.DecisionNote,
            canDecide && request.Status == EditRequestStatus.Pending,
            request.RequestedBy == _currentUser.UserId);

    private async Task<Result<EditRequest>> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<EditRequest>(NoFirm());
        }

        EditRequest? request = await _requests.FindAsync(EditRequestId.From(id), cancellationToken);

        return request is null || request.FirmId != firmId
            ? Result.Failure<EditRequest>(NotFound())
            : Result.Success(request);
    }

    /// <summary>The document's number, where it is a posted document of the firm.</summary>
    private async Task<Posted?> PostedAsync(
        EditDocumentKind kind,
        Guid id,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case EditDocumentKind.SalesInvoice:
                SalesInvoice? sale = await _sales.FindAsync(SalesInvoiceId.From(id), cancellationToken);
                return sale is { Status: SalesInvoiceStatus.Posted } && sale.FirmId == firmId
                    ? new Posted(sale.Number)
                    : null;
            case EditDocumentKind.PurchaseInvoice:
                PurchaseInvoice? purchase = await _purchases.FindAsync(PurchaseInvoiceId.From(id), cancellationToken);
                return purchase is { Status: PurchaseInvoiceStatus.Posted } && purchase.FirmId == firmId
                    ? new Posted(purchase.Number)
                    : null;
            default:
                Voucher? voucher = await _vouchers.FindAsync(VoucherId.From(id), cancellationToken);
                return voucher is { Status: VoucherStatus.Posted } && voucher.FirmId == firmId
                    ? new Posted(voucher.Number)
                    : null;
        }
    }

    private sealed record Posted(string Number);
}
