using System.Diagnostics.CodeAnalysis;

namespace ERP.SharedKernel.Primitives;

/// <summary>
/// Base class for a domain entity: an object with a lifecycle whose identity is
/// its identifier rather than its attribute values.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <remarks>
/// Two entities are equal when they are of the same type and share an
/// identifier, regardless of any other differences. A ledger whose name has
/// been corrected is still the same ledger.
/// </remarks>
[SuppressMessage(
    "Minor Code Smell",
    "S4035:Classes implementing IEquatable<T> should be sealed",
    Justification =
        "An entity base class must be inheritable - that is its entire purpose. " +
        "The asymmetric-equality trap the rule guards against is closed by " +
        "comparing GetType() rather than using an 'is' test, so a Ledger and a " +
        "Branch sharing an identifier value are never equal in either direction.")]
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : struct
{
    /// <summary>Initialises a new instance of the <see cref="Entity{TId}"/> class.</summary>
    /// <param name="id">The entity identifier.</param>
    protected Entity(TId id) => Id = id;

    /// <summary>
    /// Initialises a new instance of the <see cref="Entity{TId}"/> class for the
    /// benefit of EF Core materialisation.
    /// </summary>
    /// <remarks>
    /// EF Core needs a parameterless constructor to rehydrate an entity from a
    /// query. It is <see langword="protected"/> so application code cannot
    /// create an entity without an identifier.
    /// </remarks>
    protected Entity() => Id = default;

    /// <summary>Gets the entity identifier.</summary>
    public TId Id { get; private init; }

    /// <summary>Compares two entities for equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when both are equal.</returns>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    /// <summary>Compares two entities for inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);

    /// <inheritdoc />
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Compare the runtime type, not just TId. Two different entity types
        // that happen to share an identifier value are not the same thing.
        return GetType() == other.GetType()
               && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name} [{Id}]";
}
