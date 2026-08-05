namespace ERP.SharedKernel.Primitives;

/// <summary>
/// Something that has happened in the domain, recorded by an aggregate and
/// dispatched after the transaction commits.
/// </summary>
/// <remarks>
/// <para>
/// Events are raised inside aggregate methods but published only once
/// <c>SaveChanges</c> succeeds. That ordering is deliberate: a notification must
/// never be sent, nor a downstream projection updated, for a transaction that
/// subsequently rolled back. An accountant seeing "payment received" for a
/// voucher that was never persisted is a serious defect.
/// </para>
/// <para>
/// Deliberately declared here with no MediatR dependency - the Shared Kernel
/// stays framework-free. Infrastructure adapts these to MediatR notifications
/// when dispatching.
/// </para>
/// </remarks>
public interface IDomainEvent
{
    /// <summary>
    /// Gets the identity of this occurrence, used to make handlers idempotent
    /// and to correlate an event with its outbox row.
    /// </summary>
    Guid EventId { get; }

    /// <summary>Gets the instant the event occurred, in UTC.</summary>
    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>
/// Base record for domain events. Supplies the identity and timestamp so
/// concrete events only declare what actually happened.
/// </summary>
/// <remarks>
/// Uses a UUID version 7 for <see cref="EventId"/>. Version 7 embeds a
/// timestamp in its high bits, so values sort chronologically. That keeps
/// B-tree index writes append-only in PostgreSQL instead of scattering across
/// the index, which matters for the append-heavy outbox and audit tables.
/// </remarks>
public abstract record DomainEvent : IDomainEvent
{
    /// <summary>Initialises a new instance of the <see cref="DomainEvent"/> class.</summary>
    protected DomainEvent()
    {
        EventId = Guid.CreateVersion7();
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public Guid EventId { get; init; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAtUtc { get; init; }
}
