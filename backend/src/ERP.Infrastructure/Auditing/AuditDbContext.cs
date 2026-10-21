using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Infrastructure.Auditing;

/// <summary>One change to a master or a transaction, as the audit trail keeps it.</summary>
/// <remarks>
/// Written once and never changed: the table refuses updates and deletes at the database,
/// whoever asks, so a trail that says what happened cannot later be made to say otherwise.
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>Gets or sets the entry's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the tenant the change was made in.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the firm the changed record belongs to, where it belongs to one.</summary>
    public Guid? FirmId { get; set; }

    /// <summary>Gets or sets when the change was made.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Gets or sets who made it.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets their sign-in name at the time.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the module the record belongs to: Sales, Ledger, Product.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Gets or sets the kind of record, as the code names it.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Gets or sets the record's identifier.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>Gets or sets what people call the record: its number, code or name.</summary>
    public string? RecordLabel { get; set; }

    /// <summary>Gets or sets what was done: Create, Update or Delete.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Gets or sets the values before the change, as JSON.</summary>
    public string? OldValues { get; set; }

    /// <summary>Gets or sets the values after the change, as JSON.</summary>
    public string? NewValues { get; set; }

    /// <summary>Gets or sets the save the change was part of, tying together what one action did.</summary>
    public Guid BatchId { get; set; }
}

/// <summary>The audit trail's own database.</summary>
/// <remarks>
/// <para>
/// Kept apart from the application's database on purpose: a restore, a reset or a careless
/// statement against the books leaves the record of who did what untouched, and the trail
/// can be given to an auditor without giving them the books.
/// </para>
/// <para>
/// Everything lives in an <c>audit</c> schema, so the same model works in a database of its
/// own or, where the server will not let one be created, beside the books.
/// </para>
/// </remarks>
public sealed class AuditDbContext : DbContext
{
    /// <summary>The schema the trail lives in.</summary>
    public const string Schema = "audit";

    /// <summary>Initialises a new instance of the <see cref="AuditDbContext"/> class.</summary>
    /// <param name="options">The options.</param>
    public AuditDbContext(DbContextOptions<AuditDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the entries.</summary>
    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.ToTable("audit_entries");
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entry.Property(row => row.TenantId).HasColumnName("tenant_id");
            entry.Property(row => row.FirmId).HasColumnName("firm_id");
            entry.Property(row => row.OccurredAtUtc).HasColumnName("occurred_at_utc");
            entry.Property(row => row.UserId).HasColumnName("user_id");
            entry.Property(row => row.UserName).HasColumnName("user_name").HasMaxLength(200).IsRequired();
            entry.Property(row => row.Module).HasColumnName("module").HasMaxLength(100).IsRequired();
            entry.Property(row => row.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
            entry.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(100).IsRequired();
            entry.Property(row => row.RecordLabel).HasColumnName("record_label").HasMaxLength(300);
            entry.Property(row => row.Action).HasColumnName("action").HasMaxLength(20).IsRequired();
            entry.Property(row => row.OldValues).HasColumnName("old_values").HasColumnType("jsonb");
            entry.Property(row => row.NewValues).HasColumnName("new_values").HasColumnType("jsonb");
            entry.Property(row => row.BatchId).HasColumnName("batch_id");

            entry.HasIndex(row => new { row.TenantId, row.OccurredAtUtc })
                .HasDatabaseName("ix_audit_entries_when");
            entry.HasIndex(row => new { row.TenantId, row.Module, row.OccurredAtUtc })
                .HasDatabaseName("ix_audit_entries_module");
            entry.HasIndex(row => new { row.TenantId, row.RecordId })
                .HasDatabaseName("ix_audit_entries_record");
            entry.HasIndex(row => new { row.TenantId, row.UserId, row.OccurredAtUtc })
                .HasDatabaseName("ix_audit_entries_user");
        });
    }
}

/// <summary>Builds the audit context for the EF Core tooling.</summary>
public sealed class AuditDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "S2068:Hard-coded credentials are security-sensitive",
        Justification =
            "Not a credential to any real system: the local development default, used only " +
            "so 'dotnet ef migrations add' can pick a provider. Overridable via " +
            "ERP_AUDIT_MIGRATIONS_CONNECTION, and never used by the running application.")]
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=inspire_erp_audit;Username=postgres;Password=postgres";

    /// <inheritdoc />
    public AuditDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(
                Environment.GetEnvironmentVariable("ERP_AUDIT_MIGRATIONS_CONNECTION") ?? DefaultConnection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema))
            .Options);
}
