using ERP.Domain.Platform;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ImportLink"/> to the <c>import_links</c> table.</summary>
public sealed class ImportLinkConfiguration : IEntityTypeConfiguration<ImportLink>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ImportLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("import_links");

        // One link per old record per firm, which is also how every import looks one up.
        builder.HasKey(link => new { link.FirmId, link.Source, link.Kind, link.LegacyKey });

        builder.Property(link => link.Source).HasMaxLength(ImportLink.MaximumKeyLength);
        builder.Property(link => link.Kind).HasMaxLength(ImportLink.MaximumKeyLength);
        builder.Property(link => link.LegacyKey).HasMaxLength(ImportLink.MaximumKeyLength);

        builder.HasOne<Firm>()
            .WithMany()
            .HasForeignKey(link => link.FirmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(link => link.TenantId).HasDatabaseName("ix_import_links_tenant");
    }
}
