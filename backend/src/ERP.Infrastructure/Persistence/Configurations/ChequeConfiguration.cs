using ERP.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Cheque"/> to the <c>cheques</c> table.</summary>
public sealed class ChequeConfiguration : IEntityTypeConfiguration<Cheque>
{
    /// <summary>
    /// The statuses a cheque can still leave, as SQL for an index filter.
    /// </summary>
    /// <remarks>
    /// <see cref="ChequeStatus.Pending"/> and <see cref="ChequeStatus.Deposited"/>
    /// are 1 and 2; every terminal status is 3 or above. Expressing it as a range
    /// rather than listing the two keeps the filter correct if a further open state
    /// is ever added between them.
    /// </remarks>
    private const string OpenChequeFilter = "status < 3";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Cheque> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cheques");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.ChequeNumber)
            .HasMaxLength(Cheque.MaximumNumberLength).IsRequired();
        builder.Property(c => c.DrawnOnBank).HasMaxLength(Cheque.MaximumBankNameLength);
        builder.Property(c => c.ClosureReason).HasMaxLength(Cheque.MaximumReasonLength);

        builder.Property(c => c.InstrumentDate).IsRequired();
        builder.Property(c => c.RecordedOn).IsRequired();
        builder.Property(c => c.Direction).HasConversion<int>().IsRequired();
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();

        builder.ComplexProperty(c => c.Amount, amount =>
        {
            amount.Property(m => m.Amount)
                .HasColumnName("amount").HasPrecision(19, 4).IsRequired();
            amount.Property(m => m.Currency)
                .HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        // Derived from the status and the closure date. Storing either as well would
        // create a figure that can disagree with the two behind it.
        builder.Ignore(c => c.ClearedOn);
        builder.Ignore(c => c.IsClosed);
        builder.Ignore(c => c.IsOutstanding);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(c => c.PartyLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(c => c.BankLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(c => c.OriginVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(c => c.ClearingVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // The PDC report and the PDC calendar both ask the same question - which
        // cheques are still live, and when do they fall due - so both ride this
        // index. Cleared and bounced cheques accumulate for ever and would otherwise
        // dominate it.
        builder
            .HasIndex(c => new { c.FirmId, c.InstrumentDate })
            .HasFilter(OpenChequeFilter)
            .HasDatabaseName("ix_cheques_open_by_due_date");

        // The register reads by when a cheque was taken in, and shows closed ones
        // too - so this one is deliberately unfiltered.
        builder
            .HasIndex(c => new { c.FirmId, c.RecordedOn })
            .HasDatabaseName("ix_cheques_by_recorded_date");

        builder
            .HasIndex(c => new { c.FirmId, c.PartyLedgerId })
            .HasDatabaseName("ix_cheques_by_party");

        // A chequebook's numbers are unique: cheque 000123 cannot be written twice
        // from the same account. Scoped to live cheques so that reissuing after a
        // stopped payment is still possible, which is the one case where the same
        // number legitimately comes round again.
        builder
            .HasIndex(c => new { c.FirmId, c.BankLedgerId, c.ChequeNumber })
            .IsUnique()
            .HasFilter($"direction = 2 AND {OpenChequeFilter}")
            .HasDatabaseName("ix_cheques_issued_number");

        // The same customer cannot hand over the same live cheque twice, which is
        // what a duplicate entry looks like. Two different customers may well
        // present the same number, drawn on different banks, so this is scoped to
        // the party rather than the firm.
        //
        // Also scoped to live cheques, and for a sharper reason: a bounced cheque
        // re-presented is recorded as a new cheque bearing the same number, exactly
        // as the bank statement shows it. A constraint over all history would
        // forbid the very thing that happens next.
        builder
            .HasIndex(c => new { c.FirmId, c.PartyLedgerId, c.ChequeNumber })
            .IsUnique()
            .HasFilter($"direction = 1 AND {OpenChequeFilter}")
            .HasDatabaseName("ix_cheques_received_number");

        builder.HasIndex(c => c.TenantId).HasDatabaseName("ix_cheques_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
