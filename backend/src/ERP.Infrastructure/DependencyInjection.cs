using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Reports;
using ERP.Application.Inventory.Masters;
using ERP.Application.Inventory.Products;
using ERP.Application.Platform.Dashboards;
using ERP.Application.Platform.Grids;
using ERP.Application.Platform.Menus;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Interceptors;
using ERP.Infrastructure.Persistence.Reporting;
using ERP.Infrastructure.Persistence.Repositories;
using ERP.Infrastructure.Persistence.Seeding;
using ERP.Infrastructure.Security;
using ERP.Infrastructure.Tenancy;
using ERP.Infrastructure.Time;
using ERP.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Infrastructure;

/// <summary>Registers the infrastructure layer.</summary>
public static class DependencyInjection
{
    /// <summary>Adds persistence, tenancy, and time services.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the <c>Postgres</c> connection string is missing. Failing at
    /// startup is far better than failing on the first query, when the cause is no
    /// longer obvious.
    /// </exception>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "The 'Postgres' connection string is not configured.");

        // Both ambient contexts keep their state in a static AsyncLocal, so the
        // instance itself carries nothing. Singleton registration reflects that
        // honestly; a scoped registration would imply per-request state that does
        // not exist.
        services.AddSingleton<AmbientTenantContext>();
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());

        services.AddSingleton<AmbientCurrentUser>();
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<AmbientCurrentUser>());

        services.AddSingleton<IClock, SystemClock>();

        services.AddMemoryCache(options => options.SizeLimit = 50_000);
        services.AddScoped<IPermissionChecker, CachedPermissionChecker>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .ValidateOnStart();

        services.AddScoped<DatabaseSeeder>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IVoucherRepository, VoucherRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IBillRepository, BillRepository>();
        services.AddScoped<IChequeRepository, ChequeRepository>();
        services.AddScoped<IFirmRepository, FirmRepository>();
        services.AddScoped<IFinancialYearRepository, FinancialYearRepository>();
        services.AddScoped<INumberingSeriesRepository, NumberingSeriesRepository>();
        services.AddScoped<ITrialBalanceReader, TrialBalanceReader>();
        services.AddScoped<ILedgerStatementReader, LedgerStatementReader>();
        services.AddScoped<IDayBookReader, DayBookReader>();
        services.AddScoped<IVoucherReportReader, VoucherReportReader>();
        services.AddScoped<ITransactionSummaryReader, TransactionSummaryReader>();
        services.AddScoped<ICashFlowReader, CashFlowReader>();
        services.AddScoped<IMenuReader, MenuReader>();
        services.AddScoped<IMenuAdministrationReader, MenuAdministrationReader>();
        services.AddScoped<IMenuItemRepository, MenuItemRepository>();
        services.AddScoped<IGridLayoutRepository, GridLayoutRepository>();
        services.AddScoped<IDashboardReader, DashboardReader>();
        services.AddScoped<IDashboardMetricReader, DashboardMetricReader>();
        services.AddScoped<ICustomWidgetExecutor, CustomWidgetExecutor>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IInventoryMasterRepository, InventoryMasterRepository>();
        services.AddScoped<IInventoryMasterReader, InventoryMasterReader>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductReader, ProductReader>();
        services.AddScoped<IOutstandingBillsReader, OutstandingBillsReader>();
        services.AddScoped<IChequeReportReader, ChequeReportReader>();

        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<TenantConnectionInterceptor>();

        services.AddDbContext<ErpDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ErpDbContext).Assembly.FullName);

                // Transient network faults are a fact of cloud database hosting;
                // retrying them is cheaper than surfacing them to a user
                // mid-invoice.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });

            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditingInterceptor>(),
                serviceProvider.GetRequiredService<TenantConnectionInterceptor>());
        });

        return services;
    }
}
