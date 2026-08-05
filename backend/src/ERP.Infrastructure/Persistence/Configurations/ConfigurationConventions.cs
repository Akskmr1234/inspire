using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping applied identically to every entity, so it is written once rather
/// than repeated in each configuration and eventually forgotten in one of them.
/// </summary>
public static class ConfigurationConventions
{
    /// <summary>
    /// Applies the audit-stamp and soft-delete mapping shared by all entities.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    public static void ApplyAuditConventions<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (typeof(IAuditable).IsAssignableFrom(typeof(TEntity)))
        {
            builder.Property(nameof(IAuditable.CreatedAtUtc)).IsRequired();
            builder.Property(nameof(IAuditable.CreatedBy)).IsRequired();
        }

        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
        {
            // Soft-deleted rows are excluded by a global query filter, so almost
            // every query carries "is_deleted = false". Indexing it lets
            // PostgreSQL skip deleted rows rather than filter them after reading.
            builder.HasIndex(nameof(ISoftDeletable.IsDeleted));
        }
    }

    /// <summary>
    /// Applies the aggregate-root mapping: audit conventions, the PostgreSQL
    /// concurrency token, and exclusion of the in-memory domain-event list.
    /// </summary>
    /// <typeparam name="TEntity">The aggregate type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <remarks>
    /// Members are named rather than selected with a lambda so this takes a
    /// single type parameter. Constraining to <c>AggregateRoot&lt;TId&gt;</c>
    /// would require a second parameter that C# cannot infer - inference does not
    /// flow through generic constraints - forcing every caller to spell out both
    /// types.
    /// </remarks>
    public static void ApplyAggregateConventions<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        ApplyAuditConventions(builder);

        // Domain events live only in memory between a change and its dispatch
        // after commit. Persisting them would duplicate the outbox.
        builder.Ignore(nameof(AggregateRoot<Guid>.DomainEvents));

        // Map the concurrency token onto PostgreSQL's own xmin system column.
        // The database maintains it, so there is no application-managed version
        // number that someone can forget to increment - and two users editing the
        // same voucher produce a 409 rather than one silently overwriting the
        // other.
        //
        // ONE MANUAL STEP FOLLOWS FROM THIS. EF Core does not know xmin already
        // exists, so every scaffolded migration that creates a table for an
        // aggregate emits a line like:
        //
        //     xmin = table.Column<uint>(type: "xid", rowVersion: true, ...)
        //
        // Delete that line from the migration. PostgreSQL refuses to create a
        // column named xmin ("conflicts with a system column name"), so leaving it
        // in place makes the migration fail to apply - loudly, and caught by
        // ERP.Infrastructure.Tests, rather than silently.
        builder.Property(nameof(AggregateRoot<Guid>.Version))
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
