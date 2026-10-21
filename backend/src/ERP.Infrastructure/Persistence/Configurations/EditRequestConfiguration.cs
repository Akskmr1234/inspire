using ERP.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="EditRequest"/> to the <c>edit_requests</c> table.</summary>
public sealed class EditRequestConfiguration : IEntityTypeConfiguration<EditRequest>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EditRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("edit_requests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();

        builder.Property(request => request.Kind).HasConversion<int>().IsRequired();
        builder.Property(request => request.Action).HasConversion<int>().IsRequired();
        builder.Property(request => request.Status).HasConversion<int>().IsRequired();
        builder.Property(request => request.DocumentNumber).HasMaxLength(50).IsRequired();
        builder.Property(request => request.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(request => request.Reason).HasMaxLength(EditRequest.MaximumTextLength);
        builder.Property(request => request.RequestedByName).HasMaxLength(200).IsRequired();
        builder.Property(request => request.DecidedByName).HasMaxLength(200);
        builder.Property(request => request.DecisionNote).HasMaxLength(EditRequest.MaximumTextLength);

        // One change at a time per document: a second, made against a version the first
        // is about to replace, would be approved against something that no longer exists.
        builder.HasIndex(request => request.DocumentId)
            .IsUnique()
            .HasFilter("status = 1")
            .HasDatabaseName("ix_edit_requests_pending");

        builder.HasIndex(request => new { request.FirmId, request.Status, request.RequestedAtUtc })
            .HasDatabaseName("ix_edit_requests_firm");

        builder.HasIndex(request => request.TenantId).HasDatabaseName("ix_edit_requests_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
