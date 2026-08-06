using ERP.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Bill"/> to the <c>bills</c> table.</summary>
public sealed class BillConfiguration : IEntityTypeConfiguration<Bill>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Bill> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("bills");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.BillNumber).HasMaxLength(50).IsRequired();
        builder.Property(b => b.BillDate).IsRequired();
        builder.Property(b => b.DueDate).IsRequired();
        builder.Property(b => b.Type).HasConversion<int>().IsRequired();
        builder.Property(b => b.Status).HasConversion<int>().IsRequired();

        builder.ComplexProperty(b => b.OriginalAmount, amount =>
        {
            amount.Property(m => m.Amount)
                .HasColumnName("original_amount").HasPrecision(19, 4).IsRequired();
            amount.Property(m => m.Currency)
                .HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.ComplexProperty(b => b.SettledAmount, amount =>
        {
            amount.Property(m => m.Amount)
                .HasColumnName("settled_amount").HasPrecision(19, 4).IsRequired();
            amount.Property(m => m.Currency)
                .HasColumnName("settled_currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        // Derived from the two stored amounts. Storing it as well would create a
        // third figure that can disagree with the other two.
        builder.Ignore(b => b.OutstandingAmount);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(b => b.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        // The outstanding report's driving query: unsettled bills for one party.
        // Filtered so the index holds only the rows that report ever looks at -
        // settled bills accumulate for ever and would otherwise dominate it.
        builder
            .HasIndex(b => new { b.FirmId, b.LedgerId, b.Status })
            .HasFilter("status <> 3")
            .HasDatabaseName("ix_bills_open_by_party");

        // Aging buckets scan by due date across a firm.
        builder
            .HasIndex(b => new { b.FirmId, b.DueDate })
            .HasFilter("status <> 3")
            .HasDatabaseName("ix_bills_open_by_due_date");

        // A party cannot have the same bill reference twice: it is how the two
        // sides of the relationship identify the document to each other.
        builder
            .HasIndex(b => new { b.FirmId, b.LedgerId, b.BillNumber })
            .IsUnique()
            .HasDatabaseName("ix_bills_party_reference");

        builder.HasIndex(b => b.TenantId).HasDatabaseName("ix_bills_tenant");

        builder.HasMany(b => b.Allocations)
            .WithOne()
            .HasForeignKey(a => a.BillId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(b => b.Allocations)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_allocations");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="BillAllocation"/> to the <c>bill_allocations</c> table.</summary>
public sealed class BillAllocationConfiguration : IEntityTypeConfiguration<BillAllocation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BillAllocation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("bill_allocations");

        // A key of its own rather than (BillId, VoucherId): one voucher may settle
        // the same bill on more than one line, and a composite key would silently
        // reject the second. The value is assigned by the domain - an unassigned
        // shadow key would leave every allocation with the same empty identifier,
        // and the second one saved would collide with the first.
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.AllocatedOn).IsRequired();

        builder.ComplexProperty(a => a.Amount, amount =>
        {
            amount.Property(m => m.Amount)
                .HasColumnName("amount").HasPrecision(19, 4).IsRequired();
            amount.Property(m => m.Currency)
                .HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(a => a.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cancelling a voucher releases every allocation it made, which needs the
        // voucher indexed rather than scanned.
        builder.HasIndex(a => a.VoucherId).HasDatabaseName("ix_bill_allocations_voucher");
        builder.HasIndex(a => a.TenantId).HasDatabaseName("ix_bill_allocations_tenant");
    }
}
