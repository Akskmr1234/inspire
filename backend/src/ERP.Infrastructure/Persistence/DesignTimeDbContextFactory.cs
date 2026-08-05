using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Constructs an <see cref="ErpDbContext"/> for the <c>dotnet ef</c> tooling.
/// </summary>
/// <remarks>
/// The tooling builds a context outside the application's dependency-injection
/// container, so it cannot supply an <see cref="ITenantContext"/>. Migrations only
/// need the model's shape, never its data, so a stub that reports no tenant is
/// sufficient - and safer than one that pretends to have a real tenant.
/// <para>
/// The connection string here is used solely to pick the provider when generating
/// a migration. It is never connected to unless the developer explicitly runs
/// <c>database update</c>, and it can be overridden with the
/// <c>ERP_MIGRATIONS_CONNECTION</c> environment variable.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ErpDbContext>
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "S2068:Hard-coded credentials are security-sensitive",
        Justification =
            "Not a credential to any real system. This is the conventional local " +
            "development default, used only so 'dotnet ef migrations add' can pick " +
            "a provider without requiring every developer to set an environment " +
            "variable first. It is never used by the running application, which " +
            "reads its connection string from configuration, and it is overridable " +
            "via ERP_MIGRATIONS_CONNECTION.")]
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=inspire_erp;Username=postgres;Password=postgres";

    /// <inheritdoc />
    public ErpDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ERP_MIGRATIONS_CONNECTION")
            ?? DefaultConnection;

        DbContextOptions<ErpDbContext> options =
            new DbContextOptionsBuilder<ErpDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new ErpDbContext(options, new DesignTimeTenantContext());
    }

    /// <summary>A tenant context that reports no tenant, for design-time use only.</summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public TenantId TenantId => throw new InvalidOperationException(
            "No tenant is available at design time. This context exists only so the " +
            "EF Core tooling can build the model.");

        public FirmId? FirmId => null;

        public BranchId? BranchId => null;

        public bool IsResolved => false;

        public IDisposable BeginScope(
            TenantId tenantId,
            FirmId? firmId = null,
            BranchId? branchId = null) =>
            throw new NotSupportedException(
                "Tenant scopes cannot be established at design time.");
    }
}
