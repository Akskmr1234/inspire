using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Platform;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Platform.Grids;

/// <summary>Reads back how the signed-in user has arranged one grid.</summary>
/// <param name="GridKey">The grid, for example <c>ledgers</c>.</param>
public sealed record GetGridLayoutQuery(string GridKey) : IQuery<GridLayoutResponse>;

/// <summary>A saved arrangement.</summary>
/// <param name="GridKey">The grid it belongs to.</param>
/// <param name="State">
/// The arrangement as JSON, or null when the user has never saved one and should be
/// shown the grid's default.
/// </param>
public sealed record GridLayoutResponse(string GridKey, string? State);

/// <summary>Records how the signed-in user has arranged one grid.</summary>
/// <param name="GridKey">The grid.</param>
/// <param name="State">The arrangement, as JSON.</param>
public sealed record SaveGridLayoutCommand(string GridKey, string State) : ICommand;

/// <summary>Forgets a saved arrangement, returning the grid to its default.</summary>
/// <param name="GridKey">The grid.</param>
public sealed record ResetGridLayoutCommand(string GridKey) : ICommand;

/// <summary>Validates a <see cref="SaveGridLayoutCommand"/>.</summary>
public sealed class SaveGridLayoutCommandValidator : AbstractValidator<SaveGridLayoutCommand>
{
    /// <summary>Initialises a new instance of the <see cref="SaveGridLayoutCommandValidator"/> class.</summary>
    public SaveGridLayoutCommandValidator()
    {
        RuleFor(c => c.GridKey).NotEmpty().MaximumLength(GridLayout.MaximumGridKeyLength);
        RuleFor(c => c.State).NotEmpty().MaximumLength(GridLayout.MaximumStateLength);
    }
}

/// <summary>Reads and writes saved grid layouts.</summary>
public interface IGridLayoutRepository
{
    /// <summary>Finds a user's arrangement for one grid.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="gridKey">The grid.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The layout, or <see langword="null"/> when none has been saved.</returns>
    Task<GridLayout?> FindAsync(
        UserId userId,
        string gridKey,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a layout.</summary>
    /// <param name="layout">The layout to add.</param>
    void Add(GridLayout layout);

    /// <summary>Removes a layout.</summary>
    /// <param name="layout">The layout to remove.</param>
    void Remove(GridLayout layout);
}

/// <summary>Handles <see cref="GetGridLayoutQuery"/>.</summary>
public sealed class GetGridLayoutQueryHandler
    : IQueryHandler<GetGridLayoutQuery, GridLayoutResponse>
{
    private readonly IGridLayoutRepository _layouts;
    private readonly ICurrentUser _currentUser;

    /// <summary>Initialises a new instance of the <see cref="GetGridLayoutQueryHandler"/> class.</summary>
    /// <param name="layouts">The layout repository.</param>
    /// <param name="currentUser">The signed-in user.</param>
    public GetGridLayoutQueryHandler(
        IGridLayoutRepository layouts,
        ICurrentUser currentUser)
    {
        _layouts = layouts;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<GridLayoutResponse>> Handle(
        GetGridLayoutQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_currentUser.IsAuthenticated)
        {
            return Result.Failure<GridLayoutResponse>(Error.Forbidden(
                "GridLayout.NotSignedIn", "A layout belongs to a signed-in user."));
        }

        string gridKey = request.GridKey.Trim().ToLowerInvariant();

        GridLayout? layout = await _layouts.FindAsync(
            _currentUser.UserId, gridKey, cancellationToken);

        // Never having saved one is the ordinary case, not an error. The client asks
        // on every mount and falls back to the grid's own defaults.
        return Result.Success(new GridLayoutResponse(gridKey, layout?.State));
    }
}

/// <summary>Handles <see cref="SaveGridLayoutCommand"/>.</summary>
public sealed class SaveGridLayoutCommandHandler : ICommandHandler<SaveGridLayoutCommand>
{
    private readonly IGridLayoutRepository _layouts;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SaveGridLayoutCommandHandler"/> class.</summary>
    /// <param name="layouts">The layout repository.</param>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SaveGridLayoutCommandHandler(
        IGridLayoutRepository layouts,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _layouts = layouts;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SaveGridLayoutCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_currentUser.IsAuthenticated)
        {
            return Result.Failure(Error.Forbidden(
                "GridLayout.NotSignedIn", "A layout belongs to a signed-in user."));
        }

        string gridKey = request.GridKey.Trim().ToLowerInvariant();

        // Saving is an upsert rather than a create: the user is arranging one grid,
        // and asking a screen to know whether this is the first time it has done so
        // would be asking it to track something it has no reason to care about.
        GridLayout? existing = await _layouts.FindAsync(
            _currentUser.UserId, gridKey, cancellationToken);

        if (existing is not null)
        {
            Result replaced = existing.Replace(request.State);

            if (replaced.IsFailure)
            {
                return replaced;
            }
        }
        else
        {
            Result<GridLayout> created = GridLayout.Create(
                _tenantContext.TenantId, _currentUser.UserId, gridKey, request.State);

            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            _layouts.Add(created.Value);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="ResetGridLayoutCommand"/>.</summary>
public sealed class ResetGridLayoutCommandHandler : ICommandHandler<ResetGridLayoutCommand>
{
    private readonly IGridLayoutRepository _layouts;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ResetGridLayoutCommandHandler"/> class.</summary>
    /// <param name="layouts">The layout repository.</param>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ResetGridLayoutCommandHandler(
        IGridLayoutRepository layouts,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
    {
        _layouts = layouts;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        ResetGridLayoutCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_currentUser.IsAuthenticated)
        {
            return Result.Failure(Error.Forbidden(
                "GridLayout.NotSignedIn", "A layout belongs to a signed-in user."));
        }

        GridLayout? layout = await _layouts.FindAsync(
            _currentUser.UserId, request.GridKey.Trim().ToLowerInvariant(), cancellationToken);

        // Resetting a grid that was never customised is what the user asked for and
        // already true, so it succeeds rather than reporting a missing layout.
        if (layout is null)
        {
            return Result.Success();
        }

        _layouts.Remove(layout);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
