using System.Linq.Expressions;
using System.Reflection;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Platform;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence.Conversion;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// The application database context.
/// </summary>
/// <remarks>
/// <para>
/// Two global query filters are applied automatically to every entity that opts
/// in by implementing the corresponding marker interface:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="ITenantScoped"/> restricts rows to the current tenant.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="ISoftDeletable"/> hides rows flagged as deleted.
/// </description>
/// </item>
/// </list>
/// <para>
/// The filters are attached by reflection rather than declared per entity. With
/// several hundred tables coming, "remember to add the tenant filter" is a rule
/// that will eventually be broken, and the consequence of breaking it is one
/// customer reading another's financial data. Applying it from the interface
/// makes it structural: implementing <see cref="ITenantScoped"/> is the only
/// thing a developer has to get right.
/// </para>
/// </remarks>
public class ErpDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ErpDbContext"/> class.</summary>
    /// <param name="options">The context options.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public ErpDbContext(DbContextOptions<ErpDbContext> options, ITenantContext tenantContext)
        : base(options) => _tenantContext = tenantContext;

    /// <summary>
    /// Gets the tenant registry - the one unfiltered table in the schema.
    /// </summary>
    /// <remarks>
    /// Not tenant-scoped and carries no row-level-security policy, because this is
    /// the table that resolves which tenant a request belongs to. Use it only to
    /// look a tenant up by code or identifier; never as a way to reach tenant data.
    /// </remarks>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Gets the firms.</summary>
    public DbSet<Firm> Firms => Set<Firm>();

    /// <summary>Gets the branches.</summary>
    public DbSet<Branch> Branches => Set<Branch>();

    /// <summary>Gets the financial years.</summary>
    public DbSet<FinancialYear> FinancialYears => Set<FinancialYear>();

    /// <summary>Gets the outstanding bills.</summary>
    public DbSet<Bill> Bills => Set<Bill>();

    /// <summary>
    /// Gets the settlements made against bills.
    /// </summary>
    /// <remarks>
    /// Exposed for reporting, for the same reason as <see cref="VoucherLines"/>: an
    /// outstanding report as at a past date has to sum the allocations made up to
    /// that date across every bill, and doing so through each bill's navigation
    /// would load history the report then discards. Writes still go through
    /// <see cref="Bill"/>, which owns the over-allocation rule.
    /// </remarks>
    public DbSet<BillAllocation> BillAllocations => Set<BillAllocation>();

    /// <summary>Gets the sales invoices.</summary>
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();

    /// <summary>Gets their lines.</summary>
    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();

    /// <summary>Gets the units those lines sold.</summary>
    public DbSet<SalesInvoiceLineSerial> SalesInvoiceLineSerials =>
        Set<SalesInvoiceLineSerial>();

    /// <summary>Gets the charges carried beside the goods.</summary>
    public DbSet<SalesInvoiceCharge> SalesInvoiceCharges => Set<SalesInvoiceCharge>();

    /// <summary>Gets the tax charged on each line, head by head.</summary>
    public DbSet<SalesInvoiceLineTax> SalesInvoiceLineTaxes => Set<SalesInvoiceLineTax>();

    /// <summary>Gets the sales orders.</summary>
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();

    /// <summary>Gets their lines, each carrying how much of it has gone out.</summary>
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();

    /// <summary>Gets the charges quoted beside the goods.</summary>
    public DbSet<SalesOrderCharge> SalesOrderCharges => Set<SalesOrderCharge>();

    /// <summary>Gets the tax quoted on each line, head by head.</summary>
    public DbSet<SalesOrderLineTax> SalesOrderLineTaxes => Set<SalesOrderLineTax>();

    /// <summary>Gets the purchase invoices.</summary>
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();

    /// <summary>Gets their lines.</summary>
    public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines => Set<PurchaseInvoiceLine>();

    /// <summary>Gets the units those lines brought in.</summary>
    public DbSet<PurchaseInvoiceLineSerial> PurchaseInvoiceLineSerials =>
        Set<PurchaseInvoiceLineSerial>();

    /// <summary>Gets the charges carried beside the goods.</summary>
    public DbSet<PurchaseInvoiceCharge> PurchaseInvoiceCharges =>
        Set<PurchaseInvoiceCharge>();

    /// <summary>Gets the input tax charged on each line, head by head.</summary>
    public DbSet<PurchaseInvoiceLineTax> PurchaseInvoiceLineTaxes =>
        Set<PurchaseInvoiceLineTax>();

    /// <summary>Gets the charges documents may carry: §9's additional-ledger matrix.</summary>
    public DbSet<AdditionalLedger> AdditionalLedgers => Set<AdditionalLedger>();

    /// <summary>Gets the accounts each firm's tax heads post to, in each direction.</summary>
    public DbSet<TaxAccountMap> TaxAccountMaps => Set<TaxAccountMap>();

    /// <summary>Gets the individual head-to-ledger choices those maps hold.</summary>
    public DbSet<TaxAccountAssignment> TaxAccountAssignments => Set<TaxAccountAssignment>();

    /// <summary>Gets the cheque register.</summary>
    public DbSet<Cheque> Cheques => Set<Cheque>();

    /// <summary>Gets the navigation menu, one tree per firm.</summary>
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    /// <summary>Gets the saved data-grid layouts, one per user per grid.</summary>
    public DbSet<GridLayout> GridLayouts => Set<GridLayout>();

    /// <summary>Gets the dashboards, with their panels and role assignments.</summary>
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();

    /// <summary>Gets the units things are counted, weighed, or measured in.</summary>
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    /// <summary>Gets the product categories and sub-classes.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Gets the brands products are sold under.</summary>
    public DbSet<Brand> Brands => Set<Brand>();

    /// <summary>Gets the places stock is held.</summary>
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    /// <summary>Gets the product master.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>
    /// Gets the barcodes products are found by.
    /// </summary>
    /// <remarks>
    /// Exposed for the till, which looks a product up by scanned barcode and has no
    /// interest in loading the product to do it. Writes still go through
    /// <see cref="Product"/>, which owns the no-duplicates rule.
    /// </remarks>
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();

    /// <summary>Gets the documents that move stock.</summary>
    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();

    /// <summary>Gets the lines of those documents.</summary>
    public DbSet<StockDocumentLine> StockDocumentLines => Set<StockDocumentLine>();

    /// <summary>
    /// Gets what is on hand of each product in each warehouse, and what it cost.
    /// </summary>
    /// <remarks>
    /// A running figure rather than a sum over the ledger, unlike a ledger balance in
    /// accounting. Valuing a sales line has to answer "what does the next issue cost"
    /// on every line of every invoice, and replaying a product's movement history to
    /// answer it would make invoicing quadratic in the life of the business.
    /// </remarks>
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    /// <summary>Gets the stock ledger: every movement, written once and never changed.</summary>
    public DbSet<StockLedgerEntry> StockLedgerEntries => Set<StockLedgerEntry>();

    /// <summary>Gets the accounts each firm's stock movements post to.</summary>
    public DbSet<InventoryAccountMap> InventoryAccountMaps => Set<InventoryAccountMap>();

    /// <summary>Gets the individual account choices those maps hold.</summary>
    public DbSet<InventoryAccountAssignment> InventoryAccountAssignments =>
        Set<InventoryAccountAssignment>();

    /// <summary>Gets the serialised units: one row per physical thing.</summary>
    public DbSet<SerialNumber> SerialNumbers => Set<SerialNumber>();

    /// <summary>Gets the units each document line names.</summary>
    public DbSet<StockDocumentLineSerial> StockDocumentLineSerials =>
        Set<StockDocumentLineSerial>();

    /// <summary>Gets the batches goods are held in.</summary>
    /// <remarks>
    /// Per product rather than per warehouse: a batch number, an expiry date and what
    /// the goods were bought at are facts about the goods, wherever they are sitting.
    /// </remarks>
    public DbSet<Batch> Batches => Set<Batch>();

    /// <summary>Gets what is on hand of each batch in each warehouse.</summary>
    /// <remarks>
    /// The batch-level counterpart of <see cref="StockBalances"/>, and kept in step
    /// with it movement by movement: a product's position is the sum of its batches'
    /// positions, in quantity and in value both.
    /// </remarks>
    public DbSet<BatchBalance> BatchBalances => Set<BatchBalance>();

    /// <summary>Gets the permission catalogue.</summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>Gets the roles.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Gets the users.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Gets the issued refresh tokens.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Gets the document numbering series.</summary>
    public DbSet<NumberingSeries> NumberingSeries => Set<NumberingSeries>();

    /// <summary>Gets the account groups forming the chart of accounts.</summary>
    public DbSet<AccountGroup> AccountGroups => Set<AccountGroup>();

    /// <summary>Gets the ledgers.</summary>
    public DbSet<Ledger> Ledgers => Set<Ledger>();

    /// <summary>Gets the accounting vouchers.</summary>
    public DbSet<Voucher> Vouchers => Set<Voucher>();

    /// <summary>
    /// Gets the voucher lines.
    /// </summary>
    /// <remarks>
    /// Exposed for reporting, which reads postings across many vouchers - a trial
    /// balance or ledger report has no interest in voucher headers. Writes still go
    /// through <see cref="Voucher"/>, which owns the balance invariant.
    /// </remarks>
    public DbSet<VoucherLine> VoucherLines => Set<VoucherLine>();

    /// <summary>
    /// Gets the tenant that global query filters compare against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Falls back to <see cref="Guid.Empty"/> when no tenant has been resolved,
    /// which matches no rows. This is a deliberate difference from
    /// <see cref="ITenantContext.TenantId"/>, which throws.
    /// </para>
    /// <para>
    /// The data layer fails closed and the application layer fails loud. A query
    /// running without a tenant should return nothing rather than everything;
    /// application code that explicitly asks "which tenant am I?" without an
    /// answer has a configuration fault worth surfacing immediately.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Typed as <see cref="TenantId"/> rather than <see cref="Guid"/> so the query
    /// filter can compare whole identifiers. Comparing
    /// <c>e.TenantId.Value == someGuid</c> does not translate to SQL: the value
    /// converter already maps <see cref="TenantId"/> to a single column, so
    /// reaching into <c>.Value</c> asks EF Core to navigate inside a scalar.
    /// </remarks>
    public TenantId CurrentTenant =>
        _tenantContext.IsResolved ? _tenantContext.TenantId : default;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpDbContext).Assembly);

        ApplyGlobalQueryFilters(modelBuilder);
        ApplySnakeCaseNames(modelBuilder);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Registering converters by type means every property of these types is
        // mapped automatically, on every entity, for ever. The alternative -
        // configuring each property individually - fails silently by omission.
        configurationBuilder.Properties<TenantId>().HaveConversion<TenantIdConverter>();
        configurationBuilder.Properties<FirmId>().HaveConversion<FirmIdConverter>();
        configurationBuilder.Properties<BranchId>().HaveConversion<BranchIdConverter>();
        configurationBuilder.Properties<UserId>().HaveConversion<UserIdConverter>();
        configurationBuilder.Properties<FinancialYearId>()
            .HaveConversion<FinancialYearIdConverter>();
        configurationBuilder.Properties<RoleId>().HaveConversion<RoleIdConverter>();
        configurationBuilder.Properties<PermissionId>().HaveConversion<PermissionIdConverter>();
        configurationBuilder.Properties<BillId>().HaveConversion<BillIdConverter>();
        configurationBuilder.Properties<BillAllocationId>()
            .HaveConversion<BillAllocationIdConverter>();
        configurationBuilder.Properties<ChequeId>().HaveConversion<ChequeIdConverter>();
        configurationBuilder.Properties<MenuItemId>().HaveConversion<MenuItemIdConverter>();
        configurationBuilder.Properties<GridLayoutId>()
            .HaveConversion<GridLayoutIdConverter>();
        configurationBuilder.Properties<DashboardId>()
            .HaveConversion<DashboardIdConverter>();
        configurationBuilder.Properties<DashboardWidgetId>()
            .HaveConversion<DashboardWidgetIdConverter>();
        configurationBuilder.Properties<UnitOfMeasureId>()
            .HaveConversion<UnitOfMeasureIdConverter>();
        configurationBuilder.Properties<CategoryId>().HaveConversion<CategoryIdConverter>();
        configurationBuilder.Properties<BrandId>().HaveConversion<BrandIdConverter>();
        configurationBuilder.Properties<WarehouseId>()
            .HaveConversion<WarehouseIdConverter>();
        configurationBuilder.Properties<ProductId>().HaveConversion<ProductIdConverter>();
        configurationBuilder.Properties<ProductBarcodeId>()
            .HaveConversion<ProductBarcodeIdConverter>();
        configurationBuilder.Properties<StockDocumentId>()
            .HaveConversion<StockDocumentIdConverter>();
        configurationBuilder.Properties<StockDocumentLineId>()
            .HaveConversion<StockDocumentLineIdConverter>();
        configurationBuilder.Properties<StockBalanceId>()
            .HaveConversion<StockBalanceIdConverter>();
        configurationBuilder.Properties<StockLedgerEntryId>()
            .HaveConversion<StockLedgerEntryIdConverter>();
        configurationBuilder.Properties<BatchId>()
            .HaveConversion<BatchIdConverter>();
        configurationBuilder.Properties<SalesInvoiceId>()
            .HaveConversion<SalesInvoiceIdConverter>();
        configurationBuilder.Properties<SalesInvoiceLineId>()
            .HaveConversion<SalesInvoiceLineIdConverter>();
        configurationBuilder.Properties<SalesInvoiceChargeId>()
            .HaveConversion<SalesInvoiceChargeIdConverter>();
        configurationBuilder.Properties<SalesOrderId>()
            .HaveConversion<SalesOrderIdConverter>();
        configurationBuilder.Properties<SalesOrderLineId>()
            .HaveConversion<SalesOrderLineIdConverter>();
        configurationBuilder.Properties<SalesOrderChargeId>()
            .HaveConversion<SalesOrderChargeIdConverter>();
        configurationBuilder.Properties<PurchaseInvoiceId>()
            .HaveConversion<PurchaseInvoiceIdConverter>();
        configurationBuilder.Properties<PurchaseInvoiceLineId>()
            .HaveConversion<PurchaseInvoiceLineIdConverter>();
        configurationBuilder.Properties<PurchaseInvoiceChargeId>()
            .HaveConversion<PurchaseInvoiceChargeIdConverter>();
        configurationBuilder.Properties<AdditionalLedgerId>()
            .HaveConversion<AdditionalLedgerIdConverter>();
        configurationBuilder.Properties<InventoryAccountMapId>()
            .HaveConversion<InventoryAccountMapIdConverter>();
        configurationBuilder.Properties<TaxAccountMapId>()
            .HaveConversion<TaxAccountMapIdConverter>();
        configurationBuilder.Properties<SerialNumberId>()
            .HaveConversion<SerialNumberIdConverter>();
        configurationBuilder.Properties<BatchBalanceId>()
            .HaveConversion<BatchBalanceIdConverter>();
        configurationBuilder.Properties<RefreshTokenId>()
            .HaveConversion<RefreshTokenIdConverter>();
        configurationBuilder.Properties<AccountGroupId>()
            .HaveConversion<AccountGroupIdConverter>();
        configurationBuilder.Properties<LedgerId>().HaveConversion<LedgerIdConverter>();
        configurationBuilder.Properties<VoucherId>().HaveConversion<VoucherIdConverter>();
        configurationBuilder.Properties<VoucherLineId>()
            .HaveConversion<VoucherLineIdConverter>();
        configurationBuilder.Properties<NumberingSeriesId>()
            .HaveConversion<NumberingSeriesIdConverter>();

        configurationBuilder.Properties<CurrencyCode>()
            .HaveConversion<CurrencyCodeConverter>()
            .HaveMaxLength(3)
            .AreFixedLength();

        // Money amounts and quantities: 19 total digits with 4 decimals. Four
        // decimals rather than two because unit rates, exchange rates, and
        // three-decimal Gulf currencies all need more precision than the
        // presentation scale.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);

        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }

    /// <summary>
    /// Attaches the tenant and soft-delete filters to every entity that opts in.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            Type clrType = entityType.ClrType;

            bool isTenantScoped = typeof(ITenantScoped).IsAssignableFrom(clrType);
            bool isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);

            if (!isTenantScoped && !isSoftDeletable)
            {
                continue;
            }

            ParameterExpression entity = Expression.Parameter(clrType, "e");
            Expression? predicate = null;

            if (isTenantScoped)
            {
                // e.TenantId == this.CurrentTenant
                //
                // Both sides are TenantId. Comparing the whole identifier lets the
                // registered value converter turn it into a plain uuid comparison;
                // reaching into e.TenantId.Value instead fails to translate,
                // because the converter has already collapsed the type to a single
                // column and there is nothing inside it to navigate to.
                //
                // Reading the tenant through a context property, rather than
                // baking a value into the model, is what lets one cached model
                // serve every tenant: EF Core lifts the member access into a
                // query parameter and evaluates it per execution.
                MemberExpression tenantId = Expression.Property(
                    entity, nameof(ITenantScoped.TenantId));

                MemberExpression currentTenant = Expression.Property(
                    Expression.Constant(this), nameof(CurrentTenant));

                predicate = Expression.Equal(tenantId, currentTenant);
            }

            if (isSoftDeletable)
            {
                UnaryExpression notDeleted = Expression.Not(
                    Expression.Property(entity, nameof(ISoftDeletable.IsDeleted)));

                predicate = predicate is null
                    ? notDeleted
                    : Expression.AndAlso(predicate, notDeleted);
            }

            entityType.SetQueryFilter(Expression.Lambda(predicate!, entity));
        }
    }

    /// <summary>
    /// Renames tables, columns, keys, and indexes to snake_case.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <remarks>
    /// PostgreSQL folds unquoted identifiers to lower case, so a
    /// <c>TaxRegistrationNumber</c> column has to be double-quoted in every piece
    /// of hand-written SQL or it will not be found. The report builder, the
    /// row-level-security policies, and day-to-day debugging all involve raw SQL
    /// here, so the convention pays for itself.
    /// </remarks>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.GetTableName() is { } tableName)
            {
                entityType.SetTableName(ToSnakeCase(tableName));
            }

            foreach (IMutableProperty property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (IMutableKey key in entityType.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (IMutableForeignKey foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            }

            foreach (IMutableIndex index in entityType.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    /// <summary>Converts a PascalCase identifier to snake_case.</summary>
    /// <param name="name">The identifier.</param>
    /// <returns>The snake_case form.</returns>
    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        System.Text.StringBuilder builder = new(name.Length + 8);

        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];

            if (char.IsUpper(current))
            {
                // Insert a separator before an upper-case letter, but not at the
                // start and not in the middle of an existing run - so "TenantId"
                // becomes "tenant_id" while "GSTIN" stays "gstin" rather than
                // "g_s_t_i_n".
                bool previousIsLower = i > 0 && char.IsLower(name[i - 1]);
                bool startsNewWord = i > 0
                    && i + 1 < name.Length
                    && char.IsUpper(name[i - 1])
                    && char.IsLower(name[i + 1]);

                if (previousIsLower || startsNewWord)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
