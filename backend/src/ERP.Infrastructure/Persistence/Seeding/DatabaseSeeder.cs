using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Domain.Platform;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Persistence.Seeding;

/// <summary>Options controlling what the seeder creates.</summary>
public sealed class SeedOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Erp:Seed";

    /// <summary>Gets or sets a value indicating whether seeding runs at startup.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the tenant code used at sign-in.</summary>
    public string TenantCode { get; set; } = "inspire";

    /// <summary>Gets or sets the tenant's display name.</summary>
    public string TenantName { get; set; } = "Inspire Demo";

    /// <summary>Gets or sets the first firm's code.</summary>
    public string FirmCode { get; set; } = "MAIN";

    /// <summary>Gets or sets the first firm's name.</summary>
    public string FirmName { get; set; } = "Inspire Trading";

    /// <summary>Gets or sets the firm's base currency.</summary>
    public string BaseCurrency { get; set; } = "QAR";

    /// <summary>Gets or sets the firm's tax regime.</summary>
    public TaxRegime TaxRegime { get; set; } = TaxRegime.GccVat;

    /// <summary>Gets or sets the firm's IANA time zone.</summary>
    public string TimeZoneId { get; set; } = "Asia/Qatar";

    /// <summary>Gets or sets the administrator's sign-in name.</summary>
    public string AdministratorUserName { get; set; } = "admin";

    /// <summary>Gets or sets the administrator's email address.</summary>
    public string AdministratorEmail { get; set; } = "admin@inspire.local";

    /// <summary>
    /// Gets or sets the administrator's initial password.
    /// </summary>
    /// <remarks>
    /// Has no default on purpose. A well-known default administrator password is
    /// the single most reliable way for a system to be compromised, and a
    /// hard-coded one would follow this installation into production. Seeding
    /// refuses to create the account unless a password is supplied.
    /// </remarks>
    public string? AdministratorPassword { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the administrator must change the
    /// password at first sign-in.
    /// </summary>
    public bool RequirePasswordChange { get; set; } = true;
}

/// <summary>
/// Creates the data the application cannot start without: the permission
/// catalogue, the seeded roles, a tenant, a firm with a head-office branch, a
/// financial year, and an administrator who can sign in.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent throughout. Every step checks for what it is about to create, so
/// running it against a populated database is a no-op and running it after new
/// permissions have been added to the catalogue adds only those. It is safe on
/// every start, which is what makes it usable as a startup step rather than a
/// one-off script somebody has to remember.
/// </para>
/// <para>
/// Never updates or deletes anything an administrator may have changed. If a role
/// exists, its permissions are left exactly as configured - re-seeding must not
/// quietly undo a deliberate revocation.
/// </para>
/// </remarks>
public sealed partial class DatabaseSeeder
{
    private readonly ErpDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClock _clock;
    private readonly SeedOptions _options;
    private readonly ILogger<DatabaseSeeder> _logger;

    /// <summary>Initialises a new instance of the <see cref="DatabaseSeeder"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="passwordHasher">The password hasher.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The seed options.</param>
    /// <param name="logger">The logger.</param>
    public DatabaseSeeder(
        ErpDbContext context,
        ITenantContext tenantContext,
        IPasswordHasher passwordHasher,
        IClock clock,
        IOptions<SeedOptions> options,
        ILogger<DatabaseSeeder> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _context = context;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Runs every seeding step.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A summary of what was created.</returns>
    public async Task<Result<SeedSummary>> SeedAsync(CancellationToken cancellationToken = default)
    {
        // The permission catalogue is not tenant-scoped, so it is seeded outside
        // any tenant scope.
        int permissionsAdded = await SeedPermissionsAsync(cancellationToken);

        Tenant tenant = await EnsureTenantAsync(cancellationToken);

        // Everything from here is tenant-scoped. Without this scope the interceptor
        // would refuse the inserts and the query filters would hide the rows we are
        // checking for, so each run would try to create them again.
        using IDisposable scope = _tenantContext.BeginScope(tenant.Id);

        int rolesAdded = await SeedRolesAsync(tenant.Id, cancellationToken);
        Firm firm = await EnsureFirmAsync(tenant.Id, cancellationToken);
        await EnsureFinancialYearAsync(tenant.Id, firm, cancellationToken);
        int ledgersAdded = await EnsureChartOfAccountsAsync(firm, cancellationToken);
        int menuEntriesAdded = await EnsureMenuAsync(firm, cancellationToken);
        await EnsureDashboardsAsync(firm, cancellationToken);

        Result<bool> administrator = await EnsureAdministratorAsync(
            tenant, firm, cancellationToken);

        if (administrator.IsFailure)
        {
            return Result.Failure<SeedSummary>(administrator.Error);
        }

        SeedSummary summary = new(
            tenant.Code,
            firm.Code,
            permissionsAdded,
            rolesAdded,
            ledgersAdded,
            menuEntriesAdded,
            administrator.Value);

        LogSeedComplete(
            _logger, summary.TenantCode, summary.PermissionsAdded, summary.RolesAdded,
            summary.LedgersAdded, summary.AdministratorCreated);

        return Result.Success(summary);
    }

    /// <summary>Adds any catalogue permissions not already present.</summary>
    private async Task<int> SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        HashSet<string> existing = await _context.Permissions
            .Select(p => p.Code)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        List<Permission> toAdd = [];

        foreach (PermissionCatalogue.ResourceDefinition resource in PermissionCatalogue.Resources)
        {
            foreach (PermissionVerb verb in resource.Verbs)
            {
                string code = Permission.BuildCode(resource.Module, resource.Resource, verb);

                if (existing.Contains(code))
                {
                    continue;
                }

                Result<Permission> permission = Permission.Create(
                    resource.Module,
                    resource.Resource,
                    verb,
                    $"{verb} {resource.Description}");

                if (permission.IsSuccess)
                {
                    toAdd.Add(permission.Value);
                    existing.Add(code);
                }
            }
        }

        if (toAdd.Count > 0)
        {
            _context.Permissions.AddRange(toAdd);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return toAdd.Count;
    }

    /// <summary>Creates the tenant if it does not already exist.</summary>
    private async Task<Tenant> EnsureTenantAsync(CancellationToken cancellationToken)
    {
        string code = _options.TenantCode.Trim().ToLowerInvariant();

        Tenant? existing = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        Tenant tenant = Tenant.Create(code, _options.TenantName, SubscriptionStatus.Active).Value;

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync(cancellationToken);

        return tenant;
    }

    /// <summary>Creates the seeded roles and their grants.</summary>
    private async Task<int> SeedRolesAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        HashSet<string> existing = await _context.Roles
            .Select(r => r.Name)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);

        // Loaded once and matched in memory: the catalogue produces a few hundred
        // codes, and a query per grant pattern would be needlessly chatty.
        Dictionary<string, PermissionId> permissionsByCode = await _context.Permissions
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, cancellationToken);

        int added = 0;

        foreach (PermissionCatalogue.RoleDefinition definition in PermissionCatalogue.Roles)
        {
            if (existing.Contains(definition.Name))
            {
                // Left exactly as configured. Re-seeding must never undo an
                // administrator's deliberate revocation.
                continue;
            }

            Result<Role> created = Role.Create(
                tenantId,
                definition.Name,
                definition.Description,
                firmId: null,
                isSystemRole: true,
                grantsAllPermissions: definition.GrantsEverything);

            if (created.IsFailure)
            {
                continue;
            }

            Role role = created.Value;

            if (!definition.GrantsEverything)
            {
                role.ReplacePermissions(ResolveGrants(definition, permissionsByCode));
            }

            _context.Roles.Add(role);
            added++;
        }

        if (added > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return added;
    }

    /// <summary>Expands a role's grant patterns into concrete permission identifiers.</summary>
    private static List<PermissionId> ResolveGrants(
        PermissionCatalogue.RoleDefinition definition,
        Dictionary<string, PermissionId> permissionsByCode)
    {
        HashSet<PermissionId> granted = [];

        foreach (PermissionCatalogue.GrantPattern pattern in definition.Grants)
        {
            IEnumerable<PermissionCatalogue.ResourceDefinition> matches =
                PermissionCatalogue.Resources.Where(r =>
                    string.Equals(r.Module, pattern.Module, StringComparison.Ordinal)
                    && (pattern.ResourceNames is null
                        || pattern.ResourceNames.Contains(r.Resource, StringComparer.Ordinal)));

            foreach (PermissionCatalogue.ResourceDefinition resource in matches)
            {
                // Intersect the pattern's verbs with the ones the resource actually
                // supports, so a broad grant never creates a permission the
                // software will not check.
                foreach (PermissionVerb verb in pattern.Verbs.Intersect(resource.Verbs))
                {
                    string code = Permission.BuildCode(resource.Module, resource.Resource, verb);

                    if (permissionsByCode.TryGetValue(code, out PermissionId id))
                    {
                        granted.Add(id);
                    }
                }
            }
        }

        return [.. granted];
    }

    /// <summary>Creates the first firm and its head office.</summary>
    private async Task<Firm> EnsureFirmAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        string code = _options.FirmCode.Trim().ToUpperInvariant();

        Firm? existing = await _context.Firms
            .Include(f => f.Branches)
            .FirstOrDefaultAsync(f => f.Code == code, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        CurrencyCode currency = CurrencyCode.Create(_options.BaseCurrency).Value;

        Firm firm = Firm.Create(
            tenantId, code, _options.FirmName, currency,
            _options.TaxRegime, _options.TimeZoneId).Value;

        firm.AddBranch("HO", "Head Office", isHeadOffice: true);

        _context.Firms.Add(firm);
        await _context.SaveChangesAsync(cancellationToken);

        return firm;
    }

    /// <summary>Creates a financial year covering today, if none does.</summary>
    /// <remarks>
    /// Defaults to the calendar year containing today. That is a guess - statutory
    /// years differ by jurisdiction, and the specification's own example runs from
    /// October 2021 to December 2026 - but a demo installation needs some open
    /// period to post into, and an administrator can add the real one alongside it.
    /// </remarks>
    private async Task EnsureFinancialYearAsync(
        TenantId tenantId,
        Firm firm,
        CancellationToken cancellationToken)
    {
        List<FinancialYear> existing = await _context.FinancialYears
            .Where(y => y.FirmId == firm.Id)
            .ToListAsync(cancellationToken);

        DateOnly today = _clock.TodayIn(TimeZoneInfo.FindSystemTimeZoneById(firm.TimeZoneId));

        if (existing.Exists(y => y.Contains(today)))
        {
            return;
        }

        Result<FinancialYear> year = FinancialYear.Create(
            tenantId,
            firm.Id,
            today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new DateOnly(today.Year, 1, 1),
            new DateOnly(today.Year, 12, 31),
            existing);

        if (year.IsSuccess)
        {
            _context.FinancialYears.Add(year.Value);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Seeds the account-group tree and the ledgers the software itself references.
    /// </summary>
    /// <param name="firm">The firm to seed the chart for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many ledgers were created.</returns>
    /// <remarks>
    /// Idempotent by code, so adding a template entry later seeds only that entry
    /// and re-running never disturbs a chart an accountant has since reshaped.
    /// </remarks>
    private async Task<int> EnsureChartOfAccountsAsync(
        Firm firm,
        CancellationToken cancellationToken)
    {
        Dictionary<string, AccountGroup> groups = await _context.AccountGroups
            .Where(g => g.FirmId == firm.Id)
            .ToDictionaryAsync(g => g.Code, StringComparer.Ordinal, cancellationToken);

        foreach (ChartOfAccountsTemplate.GroupTemplate root in ChartOfAccountsTemplate.Roots)
        {
            if (groups.ContainsKey(root.Code))
            {
                continue;
            }

            Result<AccountGroup> created = AccountGroup.CreateRoot(
                firm.TenantId, firm.Id, root.Code, root.Name, root.Nature,
                isSystemGroup: true);

            if (created.IsSuccess)
            {
                created.Value.SetSchedule(root.Schedule);
                _context.AccountGroups.Add(created.Value);
                groups[root.Code] = created.Value;
            }
        }

        foreach (ChartOfAccountsTemplate.ChildGroupTemplate child in
            ChartOfAccountsTemplate.Children)
        {
            if (groups.ContainsKey(child.Code)
                || !groups.TryGetValue(child.ParentCode, out AccountGroup? parent))
            {
                continue;
            }

            // CreateChild takes the nature from the parent, so a child can never
            // contradict the root it hangs from.
            Result<AccountGroup> created = AccountGroup.CreateChild(
                parent, child.Code, child.Name);

            if (created.IsSuccess)
            {
                _context.AccountGroups.Add(created.Value);
                groups[child.Code] = created.Value;
            }
        }

        // Groups are saved before the ledgers so the foreign key resolves, and so a
        // failure part-way leaves a usable tree rather than orphaned ledgers.
        await _context.SaveChangesAsync(cancellationToken);

        HashSet<string> existingLedgers = await _context.Ledgers
            .Where(l => l.FirmId == firm.Id)
            .Select(l => l.Code)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        List<ChartOfAccountsTemplate.LedgerTemplate> templates =
        [
            .. ChartOfAccountsTemplate.CommonLedgers,
            .. ChartOfAccountsTemplate.TaxLedgersFor(firm.TaxRegime),
        ];

        int added = 0;

        foreach (ChartOfAccountsTemplate.LedgerTemplate template in templates)
        {
            if (existingLedgers.Contains(template.Code)
                || !groups.TryGetValue(template.GroupCode, out AccountGroup? group))
            {
                continue;
            }

            Result<Ledger> created = Ledger.Create(
                group, template.Code, template.Name, template.Kind, firm.BaseCurrency);

            if (created.IsSuccess)
            {
                _context.Ledgers.Add(created.Value);
                existingLedgers.Add(template.Code);
                added++;
            }
        }

        if (added > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        await SeedInventoryAccountsAsync(firm, cancellationToken);
        await SeedTaxAccountsAsync(firm, cancellationToken);
        await SeedAdditionalLedgersAsync(firm, cancellationToken);

        return added;
    }

    /// <summary>Maps the charges of section 9 onto the documents that carry them.</summary>
    /// <param name="firm">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// Every charge is mapped, and only <c>Round Off</c> is defaulted - the business's
    /// answer of 2026-08-10. The others are there to be picked on the documents that
    /// carry them; loading five zero lines onto every invoice would be five things to
    /// look past on the ones with no freight, no packing and no discount.
    /// <para>
    /// Only where the firm has none. A firm whose administrator has withdrawn a charge
    /// or reordered them keeps their arrangement.
    /// </para>
    /// </remarks>
    private async Task SeedAdditionalLedgersAsync(Firm firm, CancellationToken cancellationToken)
    {
        bool exists = await _context.AdditionalLedgers
            .AnyAsync(charge => charge.FirmId == firm.Id, cancellationToken);

        if (exists)
        {
            return;
        }

        Dictionary<string, Ledger> ledgers = await _context.Ledgers
            .Where(ledger => ledger.FirmId == firm.Id)
            .ToDictionaryAsync(ledger => ledger.Code, StringComparer.Ordinal, cancellationToken);

        // Which charges belong on which documents, and which way each moves the total.
        // A discount deducts; everything else adds. Round Off adds because the rounding
        // difference is signed - it is the only one whose direction is decided by the
        // arithmetic rather than by what the charge means.
        (string Code, bool IsAddition, bool IsDefault)[] charges =
        [
            ("ROUND-OFF", true, true),
            ("FREIGHT", true, false),
            ("PACKING", true, false),
            ("DELIVERY", true, false),
            ("DISC-ALLOWED", false, false),
        ];

        ChargeableDocument[] documents =
        [
            ChargeableDocument.Sales,
            ChargeableDocument.SalesOrder,
            ChargeableDocument.SalesReturn,
            ChargeableDocument.Purchase,
            ChargeableDocument.PurchaseOrder,
            ChargeableDocument.PurchaseReturn,
        ];

        int order = 0;

        foreach ((string code, bool isAddition, bool isDefault) in charges)
        {
            if (!ledgers.TryGetValue(code, out Ledger? ledger))
            {
                continue;
            }

            foreach (ChargeableDocument document in documents)
            {
                Result<AdditionalLedger> mapped = AdditionalLedger.Map(
                    firm.TenantId, firm.Id, document, ledger, isAddition, order);

                if (mapped.IsFailure)
                {
                    continue;
                }

                mapped.Value.SetDefault(isDefault);
                _context.AdditionalLedgers.Add(mapped.Value);
            }

            order++;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Points a firm's stock movements at the seeded accounts.</summary>
    /// <param name="firm">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// So a fresh installation is not born unable to post stock. The answer to open
    /// question 8a made the map compulsory - a movement posted into an account nobody
    /// chose is worse than one refused - and a firm that had to visit a settings screen
    /// before its first receipt would read as broken rather than as careful.
    /// <para>
    /// Only where the firm has no map at all. A firm whose administrator has repointed
    /// one of these at their own chart keeps their choice: seeding fills gaps, it does
    /// not correct people.
    /// </para>
    /// </remarks>
    private async Task SeedInventoryAccountsAsync(Firm firm, CancellationToken cancellationToken)
    {
        // Loaded rather than merely counted, because seeding fills gaps: a firm whose
        // map predates a new kind of posting - cost of goods sold, when sales arrived -
        // would otherwise be unable to post anything until somebody noticed.
        InventoryAccountMap? map = await _context.InventoryAccountMaps
            .Include(existing => existing.Accounts)
            .FirstOrDefaultAsync(existing => existing.FirmId == firm.Id, cancellationToken);

        Dictionary<string, Ledger> ledgers = await _context.Ledgers
            .Where(ledger => ledger.FirmId == firm.Id)
            .ToDictionaryAsync(ledger => ledger.Code, StringComparer.Ordinal, cancellationToken);

        (StockAccount Account, string Code)[] defaults =
        [
            (StockAccount.Inventory, "STOCK"),
            (StockAccount.Consumption, "CONSUMPTION"),
            (StockAccount.Loss, "STOCK-LOSS"),
            (StockAccount.OpeningEquity, "OPENING-STOCK"),
            (StockAccount.Variance, "STOCK-VARIANCE"),
            (StockAccount.CostOfGoodsSold, "COGS"),
            (StockAccount.SalesRevenue, "SALES"),
        ];

        bool isNew = map is null;
        map ??= InventoryAccountMap.Create(firm.TenantId, firm.Id);

        foreach ((StockAccount account, string code) in defaults)
        {
            // Only what is missing. A firm that has repointed one of these at their own
            // chart keeps their choice; seeding fills gaps, it does not correct people.
            bool chosen = map.Accounts.Any(entry => entry.Account == account);

            if (!chosen && ledgers.TryGetValue(code, out Ledger? ledger))
            {
                map.Assign(account, ledger);
            }
        }

        if (isNew)
        {
            _context.InventoryAccountMaps.Add(map);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Points a firm's tax heads at the tax ledgers seeded for its regime.</summary>
    /// <param name="firm">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// Per regime, because that is what makes the map answerable at all: a VAT firm has
    /// two heads and a GST firm has eight, and the chart seeded for each carries exactly
    /// the ledgers its own heads post to. Asking a Qatar firm where its CGST goes would
    /// be asking about a tax it does not pay.
    /// <para>
    /// Food cess and CST are deliberately left unassigned. Neither is seeded a ledger by
    /// either regime's chart, so a firm that charges one chooses the account itself -
    /// and until it does, the first document charging that head is refused by name,
    /// which is the behaviour the map was built for.
    /// </para>
    /// <para>
    /// Gap-filling, like the inventory map: a firm whose accountant has repointed output
    /// VAT at their own ledger keeps that choice, and a firm whose map predates a head
    /// gains only the head it was missing.
    /// </para>
    /// </remarks>
    private async Task SeedTaxAccountsAsync(Firm firm, CancellationToken cancellationToken)
    {
        TaxAccountMap? map = await _context.TaxAccountMaps
            .Include(existing => existing.Accounts)
            .FirstOrDefaultAsync(existing => existing.FirmId == firm.Id, cancellationToken);

        Dictionary<string, Ledger> ledgers = await _context.Ledgers
            .Where(ledger => ledger.FirmId == firm.Id)
            .ToDictionaryAsync(ledger => ledger.Code, StringComparer.Ordinal, cancellationToken);

        (TaxComponentType Component, TaxDirection Direction, string Code)[] defaults =
            DefaultTaxAccountsFor(firm.TaxRegime);

        if (defaults.Length == 0)
        {
            return;
        }

        bool isNew = map is null;
        map ??= TaxAccountMap.Create(firm.TenantId, firm.Id);

        foreach ((TaxComponentType component, TaxDirection direction, string code) in defaults)
        {
            bool chosen = map.Accounts.Any(entry =>
                entry.Component == component && entry.Direction == direction);

            if (!chosen && ledgers.TryGetValue(code, out Ledger? ledger))
            {
                map.Assign(component, direction, ledger);
            }
        }

        if (isNew)
        {
            _context.TaxAccountMaps.Add(map);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The heads a regime charges, and the seeded ledger each one posts to.</summary>
    /// <param name="regime">The firm's statutory tax system.</param>
    /// <returns>The head, direction and ledger code of each default.</returns>
    /// <remarks>
    /// The codes are the ones <see cref="ChartOfAccountsTemplate.TaxLedgersFor"/> seeds
    /// for the same regime. They are read here only to choose a default at seeding time;
    /// a posting reads the map, never a code, so a firm that renames or recodes one of
    /// these afterwards keeps posting to the account it chose.
    /// </remarks>
    private static (TaxComponentType Component, TaxDirection Direction, string Code)[]
        DefaultTaxAccountsFor(TaxRegime regime) => regime switch
        {
            TaxRegime.GccVat =>
            [
                (TaxComponentType.Vat, TaxDirection.Output, "VAT-OUTPUT"),
                (TaxComponentType.Vat, TaxDirection.Input, "VAT-INPUT"),
            ],

            TaxRegime.IndiaGst =>
            [
                (TaxComponentType.Cgst, TaxDirection.Output, "CGST-OUTPUT"),
                (TaxComponentType.Sgst, TaxDirection.Output, "SGST-OUTPUT"),
                (TaxComponentType.Igst, TaxDirection.Output, "IGST-OUTPUT"),
                (TaxComponentType.Cess, TaxDirection.Output, "CESS-OUTPUT"),
                (TaxComponentType.Cgst, TaxDirection.Input, "CGST-INPUT"),
                (TaxComponentType.Sgst, TaxDirection.Input, "SGST-INPUT"),
                (TaxComponentType.Igst, TaxDirection.Input, "IGST-INPUT"),
                (TaxComponentType.Cess, TaxDirection.Input, "CESS-INPUT"),
            ],

            _ => [],
        };

    /// <summary>Creates any menu entries the firm does not already have.</summary>
    /// <param name="firm">The firm the menu belongs to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many entries were added.</returns>
    /// <remarks>
    /// Keyed on the catalogue code, so a firm whose administrator has renamed,
    /// reordered, or hidden an entry keeps their version: this adds what is missing and
    /// touches nothing that is present. That matters more here than elsewhere in the
    /// seeder, because the menu is the one seeded structure users are expected to
    /// rearrange, and a reseed that reset it would undo their work on every deploy.
    /// </remarks>
    private async Task<int> EnsureMenuAsync(Firm firm, CancellationToken cancellationToken)
    {
        // The entries themselves rather than just their codes: a heading that already
        // exists is the parent the new entries beneath it must attach to, so the
        // recursion needs the object, and fetching it per level would be a query per
        // heading for no benefit on a few dozen rows.
        Dictionary<string, MenuItem> existing = await _context.MenuItems
            .Where(item => item.FirmId == firm.Id)
            .ToDictionaryAsync(item => item.Code, StringComparer.Ordinal, cancellationToken);

        int added = AddMenuLevel(MenuCatalogue.Default, parent: null, firm, existing);

        if (added > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return added;
    }

    /// <summary>Adds one level of the catalogue, and everything beneath it.</summary>
    /// <param name="blueprints">The entries at this level.</param>
    /// <param name="parent">The entry they sit beneath, or null at the top.</param>
    /// <param name="firm">The firm.</param>
    /// <param name="existing">The entries the firm already has, by code.</param>
    /// <returns>How many entries were added at this level and below.</returns>
    /// <remarks>
    /// Sort order is the catalogue's own ordering multiplied out, leaving gaps between
    /// entries. An administrator inserting something between two of them then has room
    /// to do it without renumbering the level.
    /// </remarks>
    private int AddMenuLevel(
        IReadOnlyList<MenuBlueprint> blueprints,
        MenuItem? parent,
        Firm firm,
        Dictionary<string, MenuItem> existing)
    {
        int added = 0;

        for (int index = 0; index < blueprints.Count; index++)
        {
            MenuBlueprint blueprint = blueprints[index];
            int sortOrder = (index + 1) * 10;

            // A heading that already exists is not re-created, but its children still
            // have to be checked - a release adding one screen beneath an existing
            // heading is the ordinary case.
            if (!existing.TryGetValue(blueprint.Code, out MenuItem? item))
            {
                Result<MenuItem> created = parent is null
                    ? MenuItem.CreateRoot(
                        firm.TenantId, firm.Id, blueprint.Code, blueprint.Label,
                        blueprint.Module, sortOrder, isSystem: true)
                    : MenuItem.CreateChild(
                        parent, blueprint.Code, blueprint.Label, sortOrder,
                        blueprint.Module, isSystem: true);

                if (created.IsFailure)
                {
                    continue;
                }

                item = created.Value;
                item.SetArabicLabel(blueprint.LabelArabic);
                item.RequirePermission(blueprint.RequiredPermission);
                item.SetRoute(blueprint.Route);

                _context.MenuItems.Add(item);
                existing[blueprint.Code] = item;
                added++;
            }

            if (blueprint.Children is { Count: > 0 } children)
            {
                added += AddMenuLevel(children, item, firm, existing);
            }
        }

        return added;
    }

    /// <summary>Creates any dashboards the firm does not already have.</summary>
    /// <param name="firm">The firm the dashboards belong to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// Keyed on the dashboard code and left alone once present, for the same reason the
    /// menu is: these are arrangements users are expected to change, and a reseed that
    /// reset them would undo that work on every deploy. A role named in the catalogue
    /// but missing from the database is skipped rather than treated as an error - roles
    /// can be renamed, and a dashboard reaching one audience short is a better outcome
    /// than a seeder that refuses to finish.
    /// </remarks>
    private async Task EnsureDashboardsAsync(Firm firm, CancellationToken cancellationToken)
    {
        HashSet<string> existing = await _context.Dashboards
            .Where(dashboard => dashboard.FirmId == firm.Id)
            .Select(dashboard => dashboard.Code)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        Dictionary<string, RoleId> rolesByName = await _context.Roles
            .Select(role => new { role.Name, role.Id })
            .ToDictionaryAsync(role => role.Name, role => role.Id, StringComparer.Ordinal, cancellationToken);

        bool added = false;

        for (int index = 0; index < DashboardCatalogue.Default.Count; index++)
        {
            DashboardBlueprint blueprint = DashboardCatalogue.Default[index];

            if (existing.Contains(blueprint.Code))
            {
                continue;
            }

            Result<Dashboard> created = Dashboard.Create(
                firm.TenantId, firm.Id, blueprint.Code, blueprint.Name,
                (index + 1) * 10, isSystem: true);

            if (created.IsFailure)
            {
                continue;
            }

            Dashboard dashboard = created.Value;
            dashboard.SetArabicName(blueprint.NameArabic);

            for (int position = 0; position < blueprint.Widgets.Count; position++)
            {
                WidgetBlueprint widget = blueprint.Widgets[position];

                Result<DashboardWidget> panel = dashboard.AddWidget(
                    widget.MetricCode, widget.Title, widget.Kind,
                    (position + 1) * 10, widget.Span);

                if (panel.IsSuccess)
                {
                    panel.Value.SetArabicTitle(widget.TitleArabic);
                }
            }

            foreach (string roleName in blueprint.RoleNames)
            {
                if (rolesByName.TryGetValue(roleName, out RoleId roleId))
                {
                    dashboard.AssignToRole(roleId);
                }
            }

            _context.Dashboards.Add(dashboard);
            added = true;
        }

        if (added)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Creates the administrator account.</summary>
    /// <returns>Whether an account was created.</returns>
    private async Task<Result<bool>> EnsureAdministratorAsync(
        Tenant tenant,
        Firm firm,
        CancellationToken cancellationToken)
    {
        string userName = _options.AdministratorUserName.Trim().ToLowerInvariant();

        bool exists = await _context.Users.AnyAsync(
            u => u.UserName == userName, cancellationToken);

        if (exists)
        {
            return Result.Success(false);
        }

        if (string.IsNullOrWhiteSpace(_options.AdministratorPassword))
        {
            // Refusing beats inventing a default. A well-known seeded password is
            // the most dependable way for an installation to be compromised, and it
            // would follow this database into production.
            return Result.Failure<bool>(Error.Validation(
                "Seed.AdministratorPasswordRequired",
                $"No administrator password was configured. Set " +
                $"'{SeedOptions.SectionName}:AdministratorPassword' (or the " +
                $"Erp__Seed__AdministratorPassword environment variable) to a value " +
                $"of at least {PasswordPolicy.MinimumLength} characters."));
        }

        Result policy = PasswordPolicy.Validate(_options.AdministratorPassword);

        if (policy.IsFailure)
        {
            return Result.Failure<bool>(policy.Error);
        }

        Role? superAdministrator = await _context.Roles
            .FirstOrDefaultAsync(r => r.GrantsAllPermissions, cancellationToken);

        if (superAdministrator is null)
        {
            return Result.Failure<bool>(Error.Unexpected(
                "Seed.SuperAdministratorMissing",
                "The Super Administrator role was not seeded, so no administrator " +
                "could be created."));
        }

        string passwordHash = _passwordHasher.Hash(_options.AdministratorPassword);

        Result<User> created = User.Create(
            tenant.Id,
            userName,
            _options.AdministratorEmail,
            "System Administrator",
            passwordHash);

        if (created.IsFailure)
        {
            return Result.Failure<bool>(created.Error);
        }

        User administrator = created.Value;
        administrator.AssignRole(superAdministrator.Id);

        // Firm access is granted explicitly even for a super administrator, so the
        // firm switcher has something to offer at first sign-in.
        foreach (Branch branch in firm.Branches)
        {
            administrator.GrantFirmAccess(firm.Id, branch.Id);
        }

        if (_options.RequirePasswordChange)
        {
            // Treats the configured password as temporary: sign-in succeeds and
            // returns MustChangePassword, so the client can force a change before
            // anything else. The hash is unchanged; only the flag differs.
            administrator.ResetPassword(passwordHash, _clock.UtcNow);
        }

        _context.Users.Add(administrator);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Information,
        Message = "Seeding complete for tenant {TenantCode}: {PermissionsAdded} " +
                  "permission(s), {RolesAdded} role(s), {LedgersAdded} ledger(s) added, " +
                  "administrator created: {AdministratorCreated}")]
    private static partial void LogSeedComplete(
        ILogger logger,
        string tenantCode,
        int permissionsAdded,
        int rolesAdded,
        int ledgersAdded,
        bool administratorCreated);
}

/// <summary>What a seeding run created.</summary>
/// <param name="TenantCode">The tenant code to sign in with.</param>
/// <param name="FirmCode">The firm code.</param>
/// <param name="PermissionsAdded">How many permissions were new.</param>
/// <param name="RolesAdded">How many roles were new.</param>
/// <param name="LedgersAdded">How many chart-of-accounts ledgers were new.</param>
/// <param name="MenuEntriesAdded">How many navigation menu entries were new.</param>
/// <param name="AdministratorCreated">Whether an administrator account was created.</param>
public sealed record SeedSummary(
    string TenantCode,
    string FirmCode,
    int PermissionsAdded,
    int RolesAdded,
    int LedgersAdded,
    int MenuEntriesAdded,
    bool AdministratorCreated);
