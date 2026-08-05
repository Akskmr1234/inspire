namespace ERP.SharedKernel.Primitives;

/// <summary>
/// A domain identifier wrapping a <see cref="Guid"/> in its own type.
/// </summary>
/// <typeparam name="TSelf">The implementing identifier type.</typeparam>
/// <remarks>
/// <para>
/// The point is to make identifier confusion a compile error. In a system with
/// this many related entities, a signature such as
/// <c>Post(Guid ledgerId, Guid branchId, Guid voucherId)</c> invites silently
/// passing arguments in the wrong order, and the mistake surfaces later as
/// corrupted books. With distinct types, transposing two arguments will not
/// compile.
/// </para>
/// <para>
/// The static abstract members let a single generic EF Core value converter and
/// a single JSON converter serve every identifier type, rather than one pair per
/// entity.
/// </para>
/// </remarks>
public interface IStronglyTypedId<out TSelf>
    where TSelf : IStronglyTypedId<TSelf>
{
    /// <summary>Gets the underlying value.</summary>
    Guid Value { get; }

    /// <summary>Wraps an existing <see cref="Guid"/>, typically read from the database.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The typed identifier.</returns>
    static abstract TSelf From(Guid value);

    /// <summary>
    /// Creates a fresh identifier using a UUID version 7, whose embedded
    /// timestamp keeps primary-key index inserts sequential in PostgreSQL
    /// instead of randomly distributed.
    /// </summary>
    /// <returns>A new identifier.</returns>
    static abstract TSelf NewId();
}
