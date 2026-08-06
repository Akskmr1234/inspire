using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Platform;

/// <summary>Identifies a saved grid layout.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct GridLayoutId(Guid Value) : IStronglyTypedId<GridLayoutId>
{
    /// <inheritdoc />
    public static GridLayoutId From(Guid value) => new(value);

    /// <inheritdoc />
    public static GridLayoutId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// How one user has arranged one data grid: which columns they show, in what order,
/// and how the rows are sorted.
/// </summary>
/// <remarks>
/// <para>
/// The specification asks for saved personal layouts on every grid. Personal is the
/// operative word: two people doing different jobs read the same list differently, and
/// a layout shared between them is a layout neither of them chose. So a layout belongs
/// to a user and a grid, and nobody else sees it.
/// </para>
/// <para>
/// The arrangement itself is stored as opaque JSON rather than as columns and widths
/// modelled here. The grid's own capabilities will grow - grouping, freezing, per-column
/// filters are all named in the specification - and every one of those would otherwise
/// be a schema migration. The server does not read this document, only hand it back to
/// the client that wrote it, so there is nothing here to keep in step.
/// </para>
/// <para>
/// This does mean the server cannot validate the shape. That is the trade, and it is
/// the right way round: a layout that fails to parse costs a user their column order,
/// which the client can recover from by falling back to the default, and no other part
/// of the system depends on it.
/// </para>
/// </remarks>
public sealed class GridLayout : AggregateRoot<GridLayoutId>, ITenantScoped, IAuditable
{
    /// <summary>The longest a grid key may be.</summary>
    public const int MaximumGridKeyLength = 100;

    /// <summary>The largest layout document accepted, in characters.</summary>
    /// <remarks>
    /// Generous for what this holds - a few dozen column entries - and small enough
    /// that nobody can use a personal preference as free storage.
    /// </remarks>
    public const int MaximumStateLength = 20_000;

    private GridLayout(
        GridLayoutId id,
        TenantId tenantId,
        UserId userId,
        string gridKey,
        string state)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        GridKey = gridKey;
        State = state;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private GridLayout()
    {
        GridKey = string.Empty;
        State = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the user whose layout this is.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets the grid the layout belongs to, for example <c>ledgers</c>.</summary>
    /// <remarks>
    /// Chosen by the client rather than derived from a route, so that two grids on one
    /// screen can be told apart and one grid keeps its layout if the screen it lives on
    /// is ever moved.
    /// </remarks>
    public string GridKey { get; private set; }

    /// <summary>Gets the arrangement, as the JSON document the client wrote.</summary>
    public string State { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Records how a user has arranged a grid.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="userId">The user whose layout this is.</param>
    /// <param name="gridKey">The grid it belongs to.</param>
    /// <param name="state">The arrangement, as JSON.</param>
    /// <returns>The layout, or a validation failure.</returns>
    public static Result<GridLayout> Create(
        TenantId tenantId,
        UserId userId,
        string gridKey,
        string state)
    {
        Result validation = Validate(gridKey, state);

        return validation.IsFailure
            ? Result.Failure<GridLayout>(validation.Error)
            : Result.Success(new GridLayout(
                GridLayoutId.NewId(), tenantId, userId,
                gridKey.Trim().ToLowerInvariant(), state.Trim()));
    }

    /// <summary>Replaces the stored arrangement.</summary>
    /// <param name="state">The new arrangement, as JSON.</param>
    /// <returns>Success, or a validation failure.</returns>
    /// <remarks>
    /// A wholesale replacement rather than a merge. The client holds the authoritative
    /// arrangement while the user is working, and sending back a partial one would make
    /// "I have hidden that column" and "I did not mention that column" the same message.
    /// </remarks>
    public Result Replace(string state)
    {
        Result validation = ValidateState(state);

        if (validation.IsFailure)
        {
            return validation;
        }

        State = state.Trim();

        return Result.Success();
    }

    private static Result Validate(string gridKey, string state)
    {
        if (string.IsNullOrWhiteSpace(gridKey))
        {
            return Result.Failure(Error.Validation(
                "GridLayout.GridKeyRequired", "A grid key is required."));
        }

        return gridKey.Trim().Length > MaximumGridKeyLength
            ? Result.Failure(Error.Validation(
                "GridLayout.GridKeyTooLong",
                $"A grid key cannot exceed {MaximumGridKeyLength} characters."))
            : ValidateState(state);
    }

    private static Result ValidateState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return Result.Failure(Error.Validation(
                "GridLayout.StateRequired", "A layout must carry an arrangement."));
        }

        return state.Trim().Length > MaximumStateLength
            ? Result.Failure(Error.Validation(
                "GridLayout.StateTooLong",
                $"A layout cannot exceed {MaximumStateLength} characters."))
            : Result.Success();
    }
}
