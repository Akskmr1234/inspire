using ERP.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="AccountGroup"/> to the <c>account_groups</c> table.</summary>
public sealed class AccountGroupConfiguration : IEntityTypeConfiguration<AccountGroup>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AccountGroup> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("account_groups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Code).HasMaxLength(20).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(200).IsRequired();
        builder.Property(g => g.NameArabic).HasMaxLength(200);
        builder.Property(g => g.Schedule).HasMaxLength(100);
        builder.Property(g => g.Nature).HasConversion<int>().IsRequired();
        builder.Property(g => g.IsSystemGroup).IsRequired();
        builder.Property(g => g.IsDeleted).IsRequired();

        // Derived from Nature. Storing them would let a row disagree with itself
        // about which statement it belongs on.
        builder.Ignore(g => g.IncreasesWithDebit);
        builder.Ignore(g => g.IsBalanceSheetGroup);

        builder
            .HasIndex(g => new { g.FirmId, g.Code })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_account_groups_firm_code");

        builder.HasIndex(g => g.TenantId).HasDatabaseName("ix_account_groups_tenant");

        // Self-reference forming the chart-of-accounts tree. Restrict rather than
        // cascade: deleting a parent must not silently take an entire branch of the
        // chart, and every ledger beneath it, with it.
        builder
            .HasOne<AccountGroup>()
            .WithMany()
            .HasForeignKey(g => g.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="Ledger"/> to the <c>ledgers</c> table.</summary>
public sealed class LedgerConfiguration : IEntityTypeConfiguration<Ledger>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Ledger> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ledgers");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Code).HasMaxLength(30).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.NameArabic).HasMaxLength(200);
        builder.Property(l => l.Kind).HasConversion<int>().IsRequired();
        builder.Property(l => l.OpeningBalanceSide).HasConversion<int>().IsRequired();
        builder.Property(l => l.TaxRegistrationNumber).HasMaxLength(50);
        builder.Property(l => l.StateCode).HasMaxLength(10);
        builder.Property(l => l.Phone).HasMaxLength(32);
        builder.Property(l => l.MobileNumber).HasMaxLength(32);
        builder.Property(l => l.Email).HasMaxLength(256);
        builder.Property(l => l.AddressLine1).HasMaxLength(200);
        builder.Property(l => l.AddressLine2).HasMaxLength(200);
        builder.Property(l => l.IsActive).IsRequired();
        builder.Property(l => l.IsBillWise).IsRequired();
        builder.Property(l => l.IsDeleted).IsRequired();

        builder.Ignore(l => l.IsCashOrBank);
        builder.Ignore(l => l.IsParty);

        builder
            .HasIndex(l => new { l.FirmId, l.Code })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_ledgers_firm_code");

        builder.HasIndex(l => l.TenantId).HasDatabaseName("ix_ledgers_tenant");

        // Sales and service screens look a customer up by mobile number as the
        // primary means of identification, so it is indexed even though it is not
        // unique - two family members may legitimately share one.
        builder
            .HasIndex(l => new { l.FirmId, l.MobileNumber })
            .HasDatabaseName("ix_ledgers_firm_mobile")
            .HasFilter("mobile_number IS NOT NULL");

        // Report and lookup screens filter by kind constantly: only cash and bank
        // ledgers in a cash book, only customers in an outstanding report.
        builder
            .HasIndex(l => new { l.FirmId, l.Kind })
            .HasDatabaseName("ix_ledgers_firm_kind");

        builder
            .HasOne<AccountGroup>()
            .WithMany()
            .HasForeignKey(l => l.AccountGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="Voucher"/> and its lines.</summary>
public sealed class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Voucher> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("vouchers");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Number).HasMaxLength(50).IsRequired();
        builder.Property(v => v.Date).IsRequired();
        builder.Property(v => v.Type).HasConversion<int>().IsRequired();
        builder.Property(v => v.Status).HasConversion<int>().IsRequired();
        builder.Property(v => v.ReferenceNumber).HasMaxLength(50);
        builder.Property(v => v.Narration).HasMaxLength(2000);
        builder.Property(v => v.PaymentMode).HasMaxLength(30);
        builder.Property(v => v.CancellationReason).HasMaxLength(500);
        builder.Property(v => v.IsDeleted).IsRequired();

        // Exchange rates need more precision than money. A rate of 0.000123 is
        // legitimate, and rounding it to four places would misstate every
        // conversion made through it.
        builder.Property(v => v.ExchangeRate).HasPrecision(19, 8).IsRequired();

        // Computed by summing the lines. Persisting a total invites it to disagree
        // with the lines it claims to summarise - the exact failure the balance
        // invariant exists to prevent.
        builder.Ignore(v => v.TotalDebit);
        builder.Ignore(v => v.TotalCredit);
        builder.Ignore(v => v.Difference);
        builder.Ignore(v => v.IsBalanced);
        builder.Ignore(v => v.IsEditable);

        // A voucher number must be unique per branch per financial year, which is
        // exactly how the numbering series is scoped. Enforced in the database as
        // well as the series generator, because two concurrent postings can each
        // believe they hold the next number.
        builder
            .HasIndex(v => new { v.BranchId, v.FinancialYearId, v.Type, v.Number })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_vouchers_branch_year_type_number");

        builder.HasIndex(v => v.TenantId).HasDatabaseName("ix_vouchers_tenant");

        // Every report is bounded by a date range within a firm - day book, cash
        // book, trial balance, profit and loss. This is the workhorse index.
        builder
            .HasIndex(v => new { v.FirmId, v.Date, v.Status })
            .HasDatabaseName("ix_vouchers_firm_date_status");

        builder
            .HasMany(v => v.Lines)
            .WithOne()
            .HasForeignKey(l => l.VoucherId)
            // Cascade here, unlike everywhere else: a line has no meaning apart
            // from its voucher, and they are one aggregate. Soft delete means this
            // fires only if a voucher is ever genuinely purged.
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(v => v.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_lines");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="VoucherLine"/> to the <c>voucher_lines</c> table.</summary>
public sealed class VoucherLineConfiguration : IEntityTypeConfiguration<VoucherLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VoucherLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("voucher_lines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Side).HasConversion<int>().IsRequired();
        builder.Property(l => l.LineNumber).IsRequired();
        builder.Property(l => l.Narration).HasMaxLength(500);

        // Money carries both an amount and its currency, so it maps to two columns
        // rather than one. A complex property keeps them together in the model
        // while storing them separately - which matters because a bare decimal in
        // the database is the very ambiguity Money exists to remove.
        builder.ComplexProperty(l => l.Amount, amount =>
        {
            amount.Property(m => m.Amount)
                .HasColumnName("amount").HasPrecision(19, 4).IsRequired();
            amount.Property(m => m.Currency)
                .HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.ComplexProperty(l => l.BaseAmount, baseAmount =>
        {
            baseAmount.Property(m => m.Amount)
                .HasColumnName("base_amount").HasPrecision(19, 4).IsRequired();
            baseAmount.Property(m => m.Currency)
                .HasColumnName("base_currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        // Projections over Side and Amount, not stored. Two nullable columns of
        // which one is always empty would invite a row with both populated.
        builder.Ignore(l => l.DebitAmount);
        builder.Ignore(l => l.CreditAmount);
        builder.Ignore(l => l.SignedBaseAmount);

        builder.HasIndex(l => l.TenantId).HasDatabaseName("ix_voucher_lines_tenant");

        // A ledger report reads every posting against one ledger, in date order.
        // The date lives on the voucher, so this index gets the rows and the join
        // orders them.
        builder
            .HasIndex(l => l.LedgerId)
            .HasDatabaseName("ix_voucher_lines_ledger");

        builder
            .HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(l => l.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}
