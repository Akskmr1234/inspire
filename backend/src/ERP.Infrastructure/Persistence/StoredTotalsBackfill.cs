using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Fills in the stored totals of transactions saved before the totals were kept on their
/// masters.
/// </summary>
/// <remarks>
/// <para>
/// Runs once, in the background, a little after the API starts; a few documents at a
/// time, tenant by tenant. The figures come from the domain - the same lines, charges and
/// rounding the screens use - and are written straight to the rows, so a document's
/// last-modified stamp and its audit trail are left as they were.
/// </para>
/// <para>
/// Never fatal. A document it cannot add up keeps its totals empty and is tried again on
/// the next start; nothing that reads the totals depends on them being there.
/// </para>
/// </remarks>
public sealed partial class StoredTotalsBackfill : BackgroundService
{
    private const int BatchSize = 100;

    private const string SetTotals =
        " SET taxable_total = {1}, tax_total = {2}, charge_total = {3}, gross_total = {4},"
        + " rounding_difference = {5}, total = {6} WHERE id = {0}";

    private readonly IServiceScopeFactory _scopes;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<StoredTotalsBackfill> _logger;

    /// <summary>Initialises a new instance of the <see cref="StoredTotalsBackfill"/> class.</summary>
    /// <param name="scopes">Creates a scope, and a database context, for each batch.</param>
    /// <param name="tenantContext">The ambient tenant scope, set for each tenant in turn.</param>
    /// <param name="logger">The logger.</param>
    public StoredTotalsBackfill(
        IServiceScopeFactory scopes,
        ITenantContext tenantContext,
        ILogger<StoredTotalsBackfill> logger)
    {
        _scopes = scopes;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Fills in every missing total.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many documents were filled in.</returns>
    public async Task<int> FillAllAsync(CancellationToken cancellationToken = default)
    {
        List<TenantId> tenants;

        using (IServiceScope scope = _scopes.CreateScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ErpDbContext>().Tenants
                .AsNoTracking()
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellationToken);
        }

        int filled = 0;

        foreach (TenantId tenant in tenants)
        {
            using IDisposable scope = _tenantContext.BeginScope(tenant);

            filled += await FillAsync(
                context => context.SalesInvoices.Include(invoice => invoice.Lines).Include(invoice => invoice.Charges),
                invoice => invoice.Id.Value,
                cancellationToken);
            filled += await FillAsync(
                context => context.PurchaseInvoices.Include(invoice => invoice.Lines).Include(invoice => invoice.Charges),
                invoice => invoice.Id.Value,
                cancellationToken);
            filled += await FillAsync(
                context => context.SalesOrders.Include(order => order.Lines).Include(order => order.Charges),
                order => order.Id.Value,
                cancellationToken);
            filled += await FillAsync(
                context => context.PurchaseOrders.Include(order => order.Lines).Include(order => order.Charges),
                order => order.Id.Value,
                cancellationToken);
        }

        return filled;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            int filled = await FillAllAsync(stoppingToken);

            if (filled > 0)
            {
                LogFilled(filled);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; the next start carries on.
        }
#pragma warning disable CA1031 // Filling in stored totals is housekeeping; it must never take the API down.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogFailed(exception);
        }
    }

    private async Task<int> FillAsync<T>(
        Func<ErpDbContext, IQueryable<T>> source,
        Func<T, Guid> idOf,
        CancellationToken cancellationToken)
        where T : class
    {
        HashSet<Guid> skipped = [];
        int filled = 0;

        while (true)
        {
            using IServiceScope scope = _scopes.CreateScope();
            ErpDbContext context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

            List<T> batch = [.. (await source(context)
                .AsNoTracking()
                .AsSplitQuery()
                .Where(row => EF.Property<decimal?>(row, StoredTotals.Total) == null)
                .OrderBy(row => EF.Property<object>(row, "Id"))
                .Take(BatchSize + skipped.Count)
                .ToListAsync(cancellationToken))
                .Where(row => !skipped.Contains(idOf(row)))
                .Take(BatchSize)];

            if (batch.Count == 0)
            {
                return filled;
            }

            foreach (T document in batch)
            {
                decimal[] totals;

                try
                {
                    totals = StoredTotals.For(document) ?? throw new InvalidOperationException("Not a totalled document.");
                }
                catch (InvalidOperationException exception)
                {
                    // Mixed currencies, say: left empty, and said so.
                    skipped.Add(idOf(document));
                    LogSkipped(typeof(T).Name, idOf(document), exception.Message);
                    continue;
                }

                // Written to the table the document is kept in: a sales return's own, say,
                // rather than through the hierarchy, which a bulk update cannot target.
                string table = context.Model.FindEntityType(document.GetType())?.GetTableName()
                    ?? throw new InvalidOperationException($"{document.GetType().Name} is not mapped to a table.");
                string sql = UpdateFor(table);

                await context.Database.ExecuteSqlRawAsync(
                    sql,
                    [idOf(document), totals[0], totals[1], totals[2], totals[3], totals[4], totals[5]],
                    cancellationToken);

                filled++;
            }
        }
    }

    /// <summary>The statement that writes a master's totals, for each table a master is kept in.</summary>
    private static string UpdateFor(string table) => table switch
    {
        "sales_invoices" => "UPDATE sales_invoices" + SetTotals,
        "sales_returns" => "UPDATE sales_returns" + SetTotals,
        "purchase_invoices" => "UPDATE purchase_invoices" + SetTotals,
        "purchase_returns" => "UPDATE purchase_returns" + SetTotals,
        "sales_orders" => "UPDATE sales_orders" + SetTotals,
        "purchase_orders" => "UPDATE purchase_orders" + SetTotals,
        _ => throw new InvalidOperationException($"'{table}' does not keep transaction totals."),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored the totals of {Count} transactions on their masters")]
    private partial void LogFilled(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not add up {Kind} {Id}: {Reason}")]
    private partial void LogSkipped(string kind, Guid id, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Filling in stored totals stopped; the next start tries again")]
    private partial void LogFailed(Exception exception);
}
