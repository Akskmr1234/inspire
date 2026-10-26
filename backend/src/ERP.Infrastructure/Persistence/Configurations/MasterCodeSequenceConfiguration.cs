using ERP.Domain.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="MasterCodeSequence"/> to the <c>master_code_sequences</c> table.</summary>
public sealed class MasterCodeSequenceConfiguration : IEntityTypeConfiguration<MasterCodeSequence>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MasterCodeSequence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("master_code_sequences");
        builder.HasKey(sequence => sequence.Id);

        builder.Property(sequence => sequence.Id).ValueGeneratedNever();
        builder.Property(sequence => sequence.Master)
            .HasMaxLength(MasterCodeSequence.MaximumMasterLength)
            .IsRequired();
        builder.Property(sequence => sequence.LastIssued).IsRequired();
        builder.Ignore(sequence => sequence.Next);

        // One counter per master and firm: two would hand out the same codes twice.
        builder.HasIndex(sequence => new { sequence.FirmId, sequence.Master })
            .IsUnique()
            .HasDatabaseName("ix_master_code_sequences_master");

        builder.HasIndex(sequence => sequence.TenantId).HasDatabaseName("ix_master_code_sequences_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
