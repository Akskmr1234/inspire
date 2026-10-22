using ERP.Domain.Accounting;
using ERP.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Country"/> to the <c>countries</c> table.</summary>
public sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("countries");
        builder.HasKey(country => country.Id);
        builder.Property(country => country.Id).ValueGeneratedNever();

        builder.Property(country => country.Code).HasMaxLength(Country.MaximumCodeLength).IsRequired();
        builder.Property(country => country.Name).HasMaxLength(Country.MaximumNameLength).IsRequired();
        builder.Property(country => country.NameArabic).HasMaxLength(Country.MaximumNameLength);
        builder.Property(country => country.CurrencyCode).HasMaxLength(3);
        builder.Property(country => country.CurrencyName).HasMaxLength(Country.MaximumCurrencyNameLength);
        builder.Property(country => country.IsActive).IsRequired();

        // A party records its country by code, so two of one code would be one country
        // with two names.
        builder.HasIndex(country => new { country.FirmId, country.Code })
            .IsUnique()
            .HasDatabaseName("ix_countries_code");

        builder.HasIndex(country => country.TenantId).HasDatabaseName("ix_countries_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="Region"/> to the <c>regions</c> table.</summary>
public sealed class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("regions");
        builder.HasKey(region => region.Id);
        builder.Property(region => region.Id).ValueGeneratedNever();

        builder.Property(region => region.Code).HasMaxLength(Region.MaximumCodeLength).IsRequired();
        builder.Property(region => region.Name).HasMaxLength(Region.MaximumNameLength).IsRequired();
        builder.Property(region => region.NameArabic).HasMaxLength(Region.MaximumNameLength);
        builder.Property(region => region.IsActive).IsRequired();

        builder.HasOne<Country>()
            .WithMany()
            .HasForeignKey(region => region.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique across the firm, not just the country: a party records the code alone,
        // and under GST it is compared with the firm's own.
        builder.HasIndex(region => new { region.FirmId, region.Code })
            .IsUnique()
            .HasDatabaseName("ix_regions_code");

        builder.HasIndex(region => region.TenantId).HasDatabaseName("ix_regions_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="Territory"/> to the <c>territories</c> table.</summary>
public sealed class TerritoryConfiguration : IEntityTypeConfiguration<Territory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Territory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("territories");
        builder.HasKey(territory => territory.Id);
        builder.Property(territory => territory.Id).ValueGeneratedNever();

        builder.Property(territory => territory.Kind).HasConversion<int>().IsRequired();
        builder.Property(territory => territory.Name)
            .HasMaxLength(Territory.MaximumNameLength).IsRequired();
        builder.Property(territory => territory.IsActive).IsRequired();

        builder.HasIndex(territory => new { territory.FirmId, territory.Kind, territory.Name })
            .IsUnique()
            .HasDatabaseName("ix_territories_name");

        builder.HasIndex(territory => territory.TenantId).HasDatabaseName("ix_territories_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="PartyPhoto"/> to the <c>party_photos</c> table.</summary>
public sealed class PartyPhotoConfiguration : IEntityTypeConfiguration<PartyPhoto>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PartyPhoto> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("party_photos");
        builder.HasKey(photo => photo.Id);
        builder.Property(photo => photo.Id).ValueGeneratedNever();

        builder.Property(photo => photo.Content).HasMaxLength(PartyPhoto.MaximumLength).IsRequired();

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(photo => photo.LedgerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Each of a party's pictures in a slot of its own; the first is the one a card shows.
        builder.Property(photo => photo.Slot).HasDefaultValue(1).IsRequired();
        builder.HasIndex(photo => new { photo.LedgerId, photo.Slot })
            .IsUnique()
            .HasDatabaseName("ix_party_photos_ledger");
        builder.HasIndex(photo => photo.TenantId).HasDatabaseName("ix_party_photos_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
