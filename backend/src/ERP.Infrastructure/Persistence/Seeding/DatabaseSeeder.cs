using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Identity;
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

        return added;
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
/// <param name="AdministratorCreated">Whether an administrator account was created.</param>
public sealed record SeedSummary(
    string TenantCode,
    string FirmCode,
    int PermissionsAdded,
    int RolesAdded,
    int LedgersAdded,
    bool AdministratorCreated);
