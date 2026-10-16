using ERP.Domain.Platform;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="FirmSetting"/>.</summary>
public sealed class FirmSettingConfiguration : IEntityTypeConfiguration<FirmSetting>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FirmSetting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("firm_settings");
        builder.HasKey(setting => setting.Id);
        builder.Property(setting => setting.Id).ValueGeneratedNever();

        builder.Property(setting => setting.Key)
            .HasMaxLength(FirmSetting.MaximumKeyLength).IsRequired();

        // Text rather than jsonb, as with the grid layouts: nothing on the server
        // queries inside a setting, and jsonb would refuse a document the client could
        // otherwise have read back and repaired.
        builder.Property(setting => setting.Value)
            .HasMaxLength(FirmSetting.MaximumValueLength).HasColumnType("text").IsRequired();

        builder.HasOne<Firm>()
            .WithMany()
            .HasForeignKey(setting => setting.FirmId)
            .OnDelete(DeleteBehavior.Cascade);

        // One document per name per firm, which is also how it is looked up.
        builder
            .HasIndex(setting => new { setting.FirmId, setting.Key })
            .IsUnique()
            .HasDatabaseName("ix_firm_settings_firm_key");

        builder.HasIndex(setting => setting.TenantId).HasDatabaseName("ix_firm_settings_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
