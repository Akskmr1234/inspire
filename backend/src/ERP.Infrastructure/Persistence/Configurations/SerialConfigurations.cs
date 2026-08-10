using ERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="SerialNumber"/> to the <c>serial_numbers</c> table.</summary>
public sealed class SerialNumberConfiguration : IEntityTypeConfiguration<SerialNumber>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SerialNumber> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("serial_numbers");
        builder.HasKey(serial => serial.Id);
        builder.Property(serial => serial.Id).ValueGeneratedNever();

        builder.Property(serial => serial.Number)
            .HasMaxLength(SerialNumber.MaximumNumberLength).IsRequired();

        builder.Property(serial => serial.Status).HasConversion<int>().IsRequired();

        builder.Property(serial => serial.UnitCost)
            .HasPrecision(19, StockBalance.CostScale).IsRequired();

        builder.Ignore(serial => serial.IsAvailable);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(serial => serial.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(serial => serial.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(serial => serial.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(serial => serial.OriginDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(serial => serial.LastDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // One number per product, enforced by the database. Two rows for the same unit
        // would be two service histories and two warranties for one machine, and a
        // sale of it would take whichever the query happened to read.
        builder
            .HasIndex(serial => new { serial.FirmId, serial.ProductId, serial.Number })
            .IsUnique()
            .HasDatabaseName("ix_serial_numbers_number");

        // The sales screen's own question: which units of this product are on this
        // shelf and free to go out.
        builder
            .HasIndex(serial => new
            {
                serial.FirmId,
                serial.ProductId,
                serial.WarehouseId,
                serial.Status,
            })
            .HasDatabaseName("ix_serial_numbers_available");

        // The service desk's question, asked from the number on the case with no
        // product in hand: whose is this, and is it under warranty.
        builder
            .HasIndex(serial => new { serial.FirmId, serial.Number })
            .HasDatabaseName("ix_serial_numbers_lookup");

        builder.HasIndex(serial => serial.TenantId)
            .HasDatabaseName("ix_serial_numbers_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>
/// Maps <see cref="StockDocumentLineSerial"/> to the <c>stock_document_line_serials</c>
/// table.
/// </summary>
public sealed class StockDocumentLineSerialConfiguration
    : IEntityTypeConfiguration<StockDocumentLineSerial>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockDocumentLineSerial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_document_line_serials");

        // The pair is the key. A line naming the same unit twice is a mistake the
        // database refuses rather than a row it stores, and a surrogate key here would
        // buy nothing but the chance to store it.
        builder.HasKey(link => new { link.StockDocumentLineId, link.SerialNumberId });

        builder.HasOne<SerialNumber>()
            .WithMany()
            .HasForeignKey(link => link.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(link => link.SerialNumberId)
            .HasDatabaseName("ix_stock_document_line_serials_serial");

        builder.HasIndex(link => link.TenantId)
            .HasDatabaseName("ix_stock_document_line_serials_tenant");
    }
}
