using ERP.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="GridLayout"/> to the <c>grid_layouts</c> table.</summary>
public sealed class GridLayoutConfiguration : IEntityTypeConfiguration<GridLayout>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GridLayout> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("grid_layouts");
        builder.HasKey(layout => layout.Id);
        builder.Property(layout => layout.Id).ValueGeneratedNever();

        builder.Property(layout => layout.GridKey)
            .HasMaxLength(GridLayout.MaximumGridKeyLength).IsRequired();

        // The arrangement itself. Stored as text rather than jsonb: nothing on the
        // server ever queries inside it, and jsonb would buy indexing and containment
        // operators at the cost of the database refusing to store a document the
        // client could otherwise have got back and repaired.
        builder.Property(layout => layout.State)
            .HasMaxLength(GridLayout.MaximumStateLength).IsRequired();

        builder.HasOne<Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(layout => layout.UserId)
            // A user who leaves takes their personal layouts with them. Nothing else
            // refers to one, and keeping them would be keeping preferences for an
            // account that can no longer sign in.
            .OnDelete(DeleteBehavior.Cascade);

        // One layout per user per grid, which is also how it is looked up: the screen
        // asks "my arrangement for this grid" and gets a row or nothing.
        builder
            .HasIndex(layout => new { layout.UserId, layout.GridKey })
            .IsUnique()
            .HasDatabaseName("ix_grid_layouts_user_grid");

        builder.HasIndex(layout => layout.TenantId).HasDatabaseName("ix_grid_layouts_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
