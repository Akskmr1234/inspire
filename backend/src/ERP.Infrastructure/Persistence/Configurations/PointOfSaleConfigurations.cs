using ERP.Domain.Identity;
using ERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="PosShift"/>.</summary>
public sealed class PosShiftConfiguration : IEntityTypeConfiguration<PosShift>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PosShift> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("pos_shifts");
        builder.HasKey(shift => shift.Id);
        builder.Property(shift => shift.Id).ValueGeneratedNever();

        builder.Property(shift => shift.OpeningCash).HasPrecision(18, 4);
        builder.Property(shift => shift.ExpectedCash).HasPrecision(18, 4);
        builder.Property(shift => shift.CountedCash).HasPrecision(18, 4);
        builder.Property(shift => shift.Notes).HasMaxLength(PosShift.MaximumNotesLength);
        builder.Ignore(shift => shift.IsOpen);

        // The counter asks for the cashier's open shift on every visit.
        builder.HasIndex(shift => new { shift.FirmId, shift.UserId, shift.ClosedAtUtc })
            .HasDatabaseName("ix_pos_shifts_firm_user");
        builder.HasIndex(shift => new { shift.FirmId, shift.BusinessDate })
            .HasDatabaseName("ix_pos_shifts_firm_date");

        // One open shift per cashier per firm.
        builder.HasIndex(shift => new { shift.FirmId, shift.UserId })
            .IsUnique()
            .HasFilter("closed_at_utc IS NULL")
            .HasDatabaseName("ix_pos_shifts_one_open");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(shift => shift.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="PosDayClose"/>.</summary>
public sealed class PosDayCloseConfiguration : IEntityTypeConfiguration<PosDayClose>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PosDayClose> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("pos_day_closes");
        builder.HasKey(close => close.Id);
        builder.Property(close => close.Id).ValueGeneratedNever();

        foreach (string money in new[]
        {
            nameof(PosDayClose.SalesTotal), nameof(PosDayClose.ReturnsTotal),
            nameof(PosDayClose.CashTotal), nameof(PosDayClose.CardTotal),
            nameof(PosDayClose.OnlineTotal), nameof(PosDayClose.CreditTotal),
        })
        {
            builder.Property<decimal>(money).HasPrecision(18, 4);
        }

        builder.Property(close => close.Notes).HasMaxLength(PosDayClose.MaximumNotesLength);

        // A day is closed once.
        builder.HasIndex(close => new { close.FirmId, close.BusinessDate })
            .IsUnique()
            .HasDatabaseName("ix_pos_day_closes_firm_date");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
