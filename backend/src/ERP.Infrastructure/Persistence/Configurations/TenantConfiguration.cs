using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Tenant"/> to the <c>tenants</c> table.</summary>
/// <remarks>
/// The one table in the schema with no tenant discriminator and no row-level
/// security policy, because it is the table that resolves which tenant a request
/// belongs to. See <see cref="Tenant"/> for why that is safe.
/// </remarks>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Code).HasMaxLength(40).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.SubscriptionStatus).HasConversion<int>().IsRequired();
        builder.Property(t => t.IsActive).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        // Globally unique: the code is what a user types as "Company" at sign-in,
        // so two tenants sharing one would make sign-in ambiguous.
        builder
            .HasIndex(t => t.Code)
            .IsUnique()
            .HasDatabaseName("ix_tenants_code");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
