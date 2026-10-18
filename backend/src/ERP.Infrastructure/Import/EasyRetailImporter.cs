using System.Globalization;
using System.Text;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Inventory.Stock;
using ERP.Application.Platform.Import;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Parties;
using ERP.Domain.Platform;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Import;

/// <summary>Brings an EasyRetail export into the selected firm.</summary>
/// <remarks>
/// <para>
/// Works the masters directly through their aggregates and saves in one go per step:
/// eight thousand products through a command each would take minutes, and every rule
/// the commands enforce is the aggregates' own. Opening stock goes through the stock
/// document command instead, because posting it moves stock and makes a journal and
/// none of that should be done twice.
/// </para>
/// <para>
/// Every record brought in is linked to its EasyRetail identifier (<see cref="ImportLink"/>),
/// so any step can be run again and finds what it did last time rather than doing it
/// twice. Where Inspire already has the same thing - its own Cash, Sales and Purchase
/// ledgers, a group named the same - the import uses that rather than a copy.
/// </para>
/// </remarks>
public sealed class EasyRetailImporter : IEasyRetailImporter
{
    private const string Source = "easyretail";

    // EasyRetail's own built-in ledgers all sit below this identifier. An unused one -
    // the GST and excise heads of a Qatar shop - is left behind rather than copied.
    private const int LastBuiltInLedger = 55;

    // The name EasyRetail gives a "nothing chosen" category, brand or lookup value.
    private const string Placeholder = "primary";

    /// <summary>EasyRetail's group names, read as Inspire's seeded group codes.</summary>
    private static readonly Dictionary<string, string> KnownGroups = new(StringComparer.Ordinal)
    {
        ["asset"] = "1000",
        ["assets"] = "1000",
        ["liability"] = "2000",
        ["liabilities"] = "2000",
        ["equity"] = "3000",
        ["income"] = "4000",
        ["expense"] = "5000",
        ["expenses"] = "5000",
        ["currentassets"] = "1100",
        ["cashinhand"] = "1110",
        ["bankaccounts"] = "1110",
        ["cashandbank"] = "1110",
        ["sundrydebtors"] = "1200",
        ["stockinhand"] = "1300",
        ["fixedassets"] = "1400",
        ["capitalaccount"] = "3100",
        ["reservesandsurplus"] = "3200",
        ["currentliabilities"] = "2100",
        ["currentliabilitiesandprovisions"] = "2100",
        ["sundrycreditors"] = "2200",
        ["dutiesandtaxes"] = "2300",
        ["directincome"] = "4100",
        ["salesaccounts"] = "4100",
        ["serviceincome"] = "4200",
        ["indirectincome"] = "4900",
        ["purchaseaccounts"] = "5100",
        ["directexpense"] = "5200",
        ["directexpenses"] = "5200",
        ["indirectexpense"] = "5900",
        ["indirectexpenses"] = "5900",
    };

    /// <summary>EasyRetail's built-in ledgers, read as the Inspire ledgers that do the same job.</summary>
    private static readonly Dictionary<string, string> KnownLedgers = new(StringComparer.Ordinal)
    {
        ["cash"] = "CASH",
        ["purchase"] = "PURCHASE",
        ["sales"] = "SALES",
        ["salesreturn"] = "SALES-RETURN",
        ["round"] = "ROUND-OFF",
        ["roundoff"] = "ROUND-OFF",
        ["discountallowed"] = "DISC-ALLOWED",
        ["discountreceived"] = "DISC-RECEIVED",
        ["chequeinhand"] = "CHEQUES-IN-HAND",
        ["chequesinhand"] = "CHEQUES-IN-HAND",
        ["capital"] = "CAPITAL",
        ["inputtax"] = "VAT-INPUT",
        ["outputtax"] = "VAT-OUTPUT",
        ["outputtax1"] = "VAT-OUTPUT",
        ["servicesales"] = "SERVICE-INC",
        ["servicecharge"] = "SERVICE-INC",
    };

    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="EasyRetailImporter"/> class.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="tenantContext">The ambient tenant and firm.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="sender">The request dispatcher, for posting opening stock.</param>
    public EasyRetailImporter(
        ErpDbContext db,
        ITenantContext tenantContext,
        IClock clock,
        ISender sender)
    {
        _db = db;
        _tenantContext = tenantContext;
        _clock = clock;
        _sender = sender;
    }

    /// <inheritdoc />
    public async Task<Result<ImportStepResult>> ImportMastersAsync(
        ImportMastersRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Firm> firm = await FirmAsync(cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<ImportStepResult>(firm.Error);
        }

        Links links = await Links.LoadAsync(_db, firm.Value.Id, Source, cancellationToken);
        Tally tally = new();

        Result groups = await ImportGroupsAsync(firm.Value, request.Groups, links, tally, cancellationToken);

        if (groups.IsFailure)
        {
            return Result.Failure<ImportStepResult>(groups.Error);
        }

        await ImportLedgersAsync(firm.Value, request, links, tally, cancellationToken);
        await ImportInventoryMastersAsync(firm.Value, request, links, tally, cancellationToken);

        links.Flush(_db, firm.Value, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(tally.Result());
    }

    private async Task<Result> ImportGroupsAsync(
        Firm firm,
        IReadOnlyList<ErGroup> legacyGroups,
        Links links,
        Tally tally,
        CancellationToken cancellationToken)
    {
        List<AccountGroup> groups = await _db.AccountGroups
            .Where(group => group.FirmId == firm.Id)
            .ToListAsync(cancellationToken);

        if (!groups.Exists(group => group.ParentId is null))
        {
            return Result.Failure(Error.BusinessRule(
                "Import.NoChartOfAccounts",
                "This firm has no chart of accounts to import into."));
        }

        Dictionary<int, ErGroup> legacy = legacyGroups.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
        HashSet<string> codes = new(groups.Select(g => g.Code), StringComparer.OrdinalIgnoreCase);

        foreach (int legacyId in legacyGroups.Select(group => group.Id))
        {
            if (links.IsFromEarlierRun("group", Key(legacyId)))
            {
                tally.AlreadyImported++;
                continue;
            }

            Resolve(legacyId, depth: 0);
        }

        return Result.Success();

        AccountGroup Resolve(int legacyId, int depth)
        {
            string key = Key(legacyId);

            if (links.Find("group", key) is { } linkedId && groups.Find(g => g.Id.Value == linkedId) is { } linked)
            {
                return linked;
            }

            if (!legacy.TryGetValue(legacyId, out ErGroup? source) || depth > 20)
            {
                return Root(AccountNature.Asset);
            }

            string name = Clean(source.Name, 100) is { Length: > 0 } n ? n : $"Group {legacyId}";
            string normal = Normalise(name);

            AccountGroup? target = null;

            if (KnownGroups.TryGetValue(normal, out string? code))
            {
                target = groups.Find(g => string.Equals(g.Code, code, StringComparison.OrdinalIgnoreCase))
                    ?? (source.UnderId == 0 ? Root(NatureOfHead(normal)) : null);
            }

            target ??= groups.Find(g => Normalise(g.Name) == normal);

            if (target is not null)
            {
                links.Add("group", key, target.Id.Value);
                tally.Matched++;
                return target;
            }

            AccountGroup parent = source.UnderId == 0
                ? Root(NatureOfHead(normal))
                : Resolve(source.UnderId, depth + 1);

            string groupCode = FreeCode(Codes.FromName(name, 20, "GROUP"), 20, codes);
            Result<AccountGroup> created = AccountGroup.CreateChild(parent, groupCode, name);

            if (created.IsFailure)
            {
                tally.Warn($"Group '{name}' was put under '{parent.Name}': {created.Error.Description}");
                links.Add("group", key, parent.Id.Value);
                return parent;
            }

            _db.AccountGroups.Add(created.Value);
            groups.Add(created.Value);
            links.Add("group", key, created.Value.Id.Value);
            tally.Created++;
            return created.Value;
        }

        AccountGroup Root(AccountNature nature) =>
            groups.Find(g => g.ParentId is null && g.Nature == nature)
            ?? groups.First(g => g.ParentId is null);
    }

    /// <summary>What a ledger is here: its party record decides, then the group it sits in.</summary>
    private static LedgerKind KindOf(
        ErLedger source,
        AccountGroup group,
        ErParty? customer,
        ErParty? supplier,
        ErEmployee? employee)
    {
        if (customer is not null)
        {
            return LedgerKind.Customer;
        }

        if (supplier is not null)
        {
            return LedgerKind.Supplier;
        }

        if (employee is not null)
        {
            return LedgerKind.Employee;
        }

        return group.Code switch
        {
            "1200" => LedgerKind.Customer,
            "2200" => LedgerKind.Supplier,
            "1110" when Normalise(source.Name).Contains("cash", StringComparison.Ordinal) => LedgerKind.Cash,
            "1110" => LedgerKind.Bank,
            _ => LedgerKind.General,
        };
    }

    /// <summary>The code a new ledger is given: the party's own, or one made from its name.</summary>
    private static string CodeFor(
        LedgerKind kind,
        ErLedger source,
        string name,
        ErParty? customer,
        ErParty? supplier,
        ErEmployee? employee)
    {
        if (kind == LedgerKind.Customer)
        {
            return customer is not null ? $"CUS-{customer.Id:D4}" : $"CUS-L{source.Id}";
        }

        if (kind == LedgerKind.Supplier)
        {
            if (supplier is null)
            {
                return $"SUP-L{source.Id}";
            }

            return IsUsableCode(supplier.Code) ? supplier.Code.Trim().ToUpperInvariant() : $"SUP-{supplier.Id:D4}";
        }

        return kind == LedgerKind.Employee && employee is not null
            ? $"EMP-{employee.Id:D4}"
            : Codes.FromName(name, 30, "LEDGER");
    }

    private static AccountNature NatureOfHead(string normalName) => normalName switch
    {
        "liability" or "liabilities" => AccountNature.Liability,
        "equity" => AccountNature.Equity,
        "income" => AccountNature.Income,
        "expense" or "expenses" => AccountNature.Expense,
        _ => AccountNature.Asset,
    };

    private async Task ImportLedgersAsync(
        Firm firm,
        ImportMastersRequest request,
        Links links,
        Tally tally,
        CancellationToken cancellationToken)
    {
        List<AccountGroup> groups = await _db.AccountGroups
            .Where(group => group.FirmId == firm.Id)
            .ToListAsync(cancellationToken);

        groups.AddRange(_db.ChangeTracker.Entries<AccountGroup>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity));

        Dictionary<Guid, AccountGroup> groupsById = groups
            .GroupBy(g => g.Id.Value)
            .ToDictionary(g => g.Key, g => g.First());

        List<Ledger> ledgers = await _db.Ledgers
            .Where(ledger => ledger.FirmId == firm.Id)
            .ToListAsync(cancellationToken);

        HashSet<string> codes = new(ledgers.Select(l => l.Code), StringComparer.OrdinalIgnoreCase);
        List<Ledger> preexisting = [.. ledgers];

        PartyPlaces places = await PartyPlaces.LoadAsync(_db, firm, cancellationToken);

        Dictionary<int, ErParty> customers = ByLedger(request.Customers, party => party.LedgerId);
        Dictionary<int, ErParty> suppliers = ByLedger(request.Suppliers, party => party.LedgerId);
        Dictionary<int, ErEmployee> employees = ByLedger(request.Employees, person => person.LedgerId);

        foreach (ErLedger source in request.Ledgers)
        {
            string key = Key(source.Id);

            if (links.Find("ledger", key) is not null)
            {
                tally.AlreadyImported++;
                continue;
            }

            AccountGroup group = links.Find("group", Key(source.GroupId)) is { } groupId
                && groupsById.TryGetValue(groupId, out AccountGroup? linkedGroup)
                    ? linkedGroup
                    : groups.Find(g => g.ParentId is null)!;

            customers.TryGetValue(source.Id, out ErParty? customer);
            suppliers.TryGetValue(source.Id, out ErParty? supplier);
            employees.TryGetValue(source.Id, out ErEmployee? employee);

            LedgerKind kind = KindOf(source, group, customer, supplier, employee);

            bool unused = source.Postings == 0 && source.Opening == 0m && source.Balance == 0m;
            bool party = kind is LedgerKind.Customer or LedgerKind.Supplier;

            if (unused && (source.Id <= LastBuiltInLedger || (party && customer is null && supplier is null)))
            {
                tally.Skipped++;
                continue;
            }

            string name = Clean(source.Name, 200) is { Length: > 0 } n ? n : $"Ledger {source.Id}";
            string normal = Normalise(name);

            Ledger? existing = null;

            if (KnownLedgers.TryGetValue(normal, out string? knownCode) && !party)
            {
                existing = ledgers.Find(l => string.Equals(l.Code, knownCode, StringComparison.OrdinalIgnoreCase));
            }

            // EasyRetail calls its first bank account "Primary".
            if (existing is null && normal == Placeholder && kind == LedgerKind.Bank)
            {
                existing = ledgers.Find(l => string.Equals(l.Code, "BANK", StringComparison.OrdinalIgnoreCase));
            }

            // An account of the same name already here - a head somebody opened by hand
            // before importing - is that account. Not for customers and suppliers: two
            // parties may share a name, and each has a code of its own to be found by.
            if (!party)
            {
                existing ??= preexisting.Find(l => Normalise(l.Name) == normal && Ledger.IsPlain(l.Kind) && Ledger.IsPlain(kind));
            }

            // A party already here under the code it would be given is that party - but
            // only one that was here before this run: two EasyRetail suppliers can share a
            // code, and the second must not become the first.
            if (party)
            {
                string partyCode = CodeFor(kind, source, name, customer, supplier, employee);
                existing ??= preexisting.Find(l =>
                    l.Kind == kind && string.Equals(l.Code, partyCode, StringComparison.OrdinalIgnoreCase));
            }

            if (existing is not null)
            {
                links.Add("ledger", key, existing.Id.Value);
                tally.Matched++;
                continue;
            }

            string code = FreeCode(CodeFor(kind, source, name, customer, supplier, employee), 30, codes);

            Result<Ledger> created = Ledger.Create(group, code, name, kind, firm.BaseCurrency);

            if (created.IsFailure)
            {
                tally.Warn($"Ledger '{name}' was not imported: {created.Error.Description}");
                continue;
            }

            Ledger ledger = created.Value;
            ErParty? details = customer ?? supplier;

            if (details is not null)
            {
                ledger.SetContactDetails(
                    Clean(details.Phone, 30),
                    Clean(details.Mobile, 30),
                    Clean(details.Email, 200),
                    Clean(details.Address1, 200),
                    Clean(details.Address2, 200));
                ledger.SetTaxDetails(Clean(details.TaxNumber, 50), null);
                ledger.SetCreditTerms(
                    details.CreditLimit > 0m ? details.CreditLimit : null,
                    details.CreditDays > 0 ? details.CreditDays : null);
                ApplyPartyDetails(ledger, details, places);
            }
            else if (employee is not null)
            {
                ledger.SetContactDetails(null, Clean(employee.Mobile, 30), Clean(employee.Email, 200), null, null);
            }

            if (party)
            {
                ledger.SetBillWise(true);
            }

            _db.Ledgers.Add(ledger);
            ledgers.Add(ledger);
            links.Add("ledger", key, ledger.Id.Value);
            tally.Created++;
        }
    }

    private async Task ImportInventoryMastersAsync(
        Firm firm,
        ImportMastersRequest request,
        Links links,
        Tally tally,
        CancellationToken cancellationToken)
    {
        // Categories. EasyRetail files every category under one called "Primary", which
        // is no category at all: its children become top-level categories here, and a
        // product left in "Primary" itself goes to "General".
        List<Category> categories = await _db.Categories
            .Where(category => category.FirmId == firm.Id)
            .ToListAsync(cancellationToken);
        HashSet<string> categoryCodes = new(categories.Select(c => c.Code), StringComparer.OrdinalIgnoreCase);

        foreach (ErCategory source in request.Categories.OrderBy(c => c.UnderId == 0 ? 0 : 1))
        {
            string key = Key(source.Id);

            if (links.Find("category", key) is not null)
            {
                tally.AlreadyImported++;
                continue;
            }

            bool isRoot = source.UnderId == 0 || Normalise(source.Name) == Placeholder;
            string name = Clean(source.Name, Category.MaximumNameLength) ?? $"Category {source.Id}";

            if (isRoot)
            {
                name = "General";
            }

            Category? parent = !isRoot
                && links.Find("category", Key(source.UnderId)) is { } parentId
                && categories.Find(c => c.Id.Value == parentId) is { } p
                && Normalise(request.Categories.FirstOrDefault(c => c.Id == source.UnderId)?.Name ?? string.Empty) != Placeholder
                    ? p
                    : null;

            Category? existing = categories.Find(c =>
                Normalise(c.Name) == Normalise(name) && c.ParentId == parent?.Id);

            if (existing is not null)
            {
                links.Add("category", key, existing.Id.Value);
                tally.Matched++;
                continue;
            }

            string code = FreeCode(Codes.FromName(name, Category.MaximumCodeLength, "CATEGORY"), Category.MaximumCodeLength, categoryCodes);
            Result<Category> created = parent is null
                ? Category.CreateRoot(firm.TenantId, firm.Id, code, name)
                : Category.CreateChild(parent, code, name);

            if (created.IsFailure)
            {
                tally.Warn($"Category '{name}' was not imported: {created.Error.Description}");
                continue;
            }

            _db.Categories.Add(created.Value);
            categories.Add(created.Value);
            links.Add("category", key, created.Value.Id.Value);
            tally.Created++;
        }

        // Brands. "Primary" means no brand, and is left out.
        List<Brand> brands = await _db.Brands
            .Where(brand => brand.FirmId == firm.Id)
            .ToListAsync(cancellationToken);
        HashSet<string> brandCodes = new(brands.Select(b => b.Code), StringComparer.OrdinalIgnoreCase);

        foreach (ErNamed source in request.Brands)
        {
            string key = Key(source.Id);
            string name = Clean(source.Name, Brand.MaximumNameLength) ?? string.Empty;

            if (links.Find("brand", key) is not null)
            {
                tally.AlreadyImported++;
                continue;
            }

            if (name.Length == 0 || Normalise(name) == Placeholder)
            {
                tally.Skipped++;
                continue;
            }

            if (brands.Find(b => Normalise(b.Name) == Normalise(name)) is { } existing)
            {
                links.Add("brand", key, existing.Id.Value);
                tally.Matched++;
                continue;
            }

            string code = FreeCode(Codes.FromName(name, Brand.MaximumCodeLength, "BRAND"), Brand.MaximumCodeLength, brandCodes);
            Result<Brand> created = Brand.Create(firm.TenantId, firm.Id, code, name);

            if (created.IsFailure)
            {
                tally.Warn($"Brand '{name}' was not imported: {created.Error.Description}");
                continue;
            }

            _db.Brands.Add(created.Value);
            brands.Add(created.Value);
            links.Add("brand", key, created.Value.Id.Value);
            tally.Created++;
        }

        // Units, bases first so a derived unit has something to convert to.
        List<UnitOfMeasure> units = await _db.UnitsOfMeasure
            .Where(unit => unit.FirmId == firm.Id)
            .ToListAsync(cancellationToken);
        HashSet<string> unitCodes = new(units.Select(u => u.Code), StringComparer.OrdinalIgnoreCase);

        foreach (ErUnit source in request.Units.OrderBy(u => u.BaseId == 0 ? 0 : 1))
        {
            string key = Key(source.Id);

            if (links.Find("unit", key) is not null)
            {
                tally.AlreadyImported++;
                continue;
            }

            string name = Clean(source.Name, UnitOfMeasure.MaximumNameLength) is { Length: > 0 } n ? n : $"Unit {source.Id}";
            string shortName = Clean(source.ShortName, UnitOfMeasure.MaximumSymbolLength) ?? name;

            UnitOfMeasure? existing = units.Find(u =>
                Normalise(u.Name) == Normalise(name)
                || string.Equals(u.Code, shortName, StringComparison.OrdinalIgnoreCase)
                || (Normalise(name) is "no" or "nos" or "number" && u.Code is "NOS" or "NO" or "PCS"));

            if (existing is not null)
            {
                links.Add("unit", key, existing.Id.Value);
                tally.Matched++;
                continue;
            }

            string code = FreeCode(Codes.FromName(shortName, UnitOfMeasure.MaximumCodeLength, "UNIT"), UnitOfMeasure.MaximumCodeLength, unitCodes);
            UnitOfMeasure? baseUnit = source.BaseId != 0 && links.Find("unit", Key(source.BaseId)) is { } baseId
                ? units.Find(u => u.Id.Value == baseId)
                : null;

            Result<UnitOfMeasure> created = baseUnit is { IsBaseUnit: true } && source.Factor > 0m
                ? UnitOfMeasure.CreateDerived(baseUnit, code, name, source.Factor, shortName)
                : UnitOfMeasure.CreateBase(firm.TenantId, firm.Id, code, name, shortName);

            if (created.IsFailure)
            {
                tally.Warn($"Unit '{name}' was not imported: {created.Error.Description}");
                continue;
            }

            _db.UnitsOfMeasure.Add(created.Value);
            units.Add(created.Value);
            links.Add("unit", key, created.Value.Id.Value);
            tally.Created++;
        }

        // Warehouses. EasyRetail's "Primary" godown is the shop's main store, which is
        // the warehouse Inspire already defaults to.
        List<Warehouse> warehouses = await _db.Warehouses
            .Where(warehouse => warehouse.FirmId == firm.Id)
            .ToListAsync(cancellationToken);
        HashSet<string> warehouseCodes = new(warehouses.Select(w => w.Code), StringComparer.OrdinalIgnoreCase);

        foreach (ErNamed source in request.Warehouses)
        {
            string key = Key(source.Id);

            if (links.Find("warehouse", key) is not null)
            {
                tally.AlreadyImported++;
                continue;
            }

            string name = Clean(source.Name, Warehouse.MaximumNameLength) is { Length: > 0 } n ? n : $"Warehouse {source.Id}";

            Warehouse? existing = warehouses.Find(w => Normalise(w.Name) == Normalise(name))
                ?? (Normalise(name) == Placeholder ? warehouses.Find(w => w.IsDefault) : null);

            if (existing is not null)
            {
                links.Add("warehouse", key, existing.Id.Value);
                tally.Matched++;
                continue;
            }

            string code = FreeCode(
                Normalise(name) == Placeholder ? "MAIN" : Codes.FromName(name, Warehouse.MaximumCodeLength, "STORE"),
                Warehouse.MaximumCodeLength,
                warehouseCodes);
            Result<Warehouse> created = Warehouse.Create(
                firm.TenantId, firm.Id, code, Normalise(name) == Placeholder ? "Main warehouse" : name);

            if (created.IsFailure)
            {
                tally.Warn($"Warehouse '{name}' was not imported: {created.Error.Description}");
                continue;
            }

            if (!warehouses.Exists(w => w.IsDefault))
            {
                created.Value.MakeDefault();
            }

            _db.Warehouses.Add(created.Value);
            warehouses.Add(created.Value);
            links.Add("warehouse", key, created.Value.Id.Value);
            tally.Created++;
        }
    }

    /// <inheritdoc />
    public async Task<Result<ImportStepResult>> ImportProductsAsync(
        ImportProductsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Firm> found = await FirmAsync(cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ImportStepResult>(found.Error);
        }

        Firm firm = found.Value;
        Links links = await Links.LoadAsync(_db, firm.Id, Source, cancellationToken);
        Tally tally = new();

        Dictionary<Guid, Category> categories = await _db.Categories
            .Where(category => category.FirmId == firm.Id)
            .ToDictionaryAsync(category => category.Id.Value, cancellationToken);
        HashSet<string> categoryCodes = new(categories.Values.Select(c => c.Code), StringComparer.OrdinalIgnoreCase);
        Dictionary<Guid, Brand> brands = await _db.Brands
            .Where(brand => brand.FirmId == firm.Id)
            .ToDictionaryAsync(brand => brand.Id.Value, cancellationToken);
        Dictionary<Guid, UnitOfMeasure> units = await _db.UnitsOfMeasure
            .Where(unit => unit.FirmId == firm.Id)
            .ToDictionaryAsync(unit => unit.Id.Value, cancellationToken);

        UnitOfMeasure? fallbackUnit = units.Values.FirstOrDefault(u => u.IsBaseUnit && u.IsActive);

        if (fallbackUnit is null)
        {
            return Result.Failure<ImportStepResult>(Error.BusinessRule(
                "Import.NoUnits", "Import the masters first: there is no unit of measure to put products in."));
        }

        var existingProducts = await _db.Products
            .Where(p => p.FirmId == firm.Id)
            .Select(p => new { p.Id, p.Code, p.Description })
            .ToListAsync(cancellationToken);
        HashSet<string> productCodes = new(existingProducts.Select(p => p.Code), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Guid> sameProduct = existingProducts
            .GroupBy(p => $"{p.Code.ToUpperInvariant()}|{Normalise(p.Description)}")
            .ToDictionary(g => g.Key, g => g.First().Id.Value);

        List<ErProduct> wanted = [.. request.Products.Where(p => links.Find("product", Key(p.Id)) is null)];
        tally.AlreadyImported += request.Products.Count - wanted.Count;

        // Barcodes are unique across the whole tenant, so the ones already taken are read
        // once rather than a scanned code being found to collide on save.
        List<string> candidates = [.. wanted.SelectMany(p => new[] { p.Upc, p.Ean })
            .Concat(request.Barcodes.Select(b => b.Barcode))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)];
        HashSet<string> barcodesTaken = new(
            await _db.ProductBarcodes.Where(b => candidates.Contains(b.Barcode)).Select(b => b.Barcode).ToListAsync(cancellationToken),
            StringComparer.Ordinal);

        Dictionary<int, string> subClasses = request.SubClasses
            .GroupBy(s => s.Id)
            .ToDictionary(s => s.Key, s => s.First().Name);
        ILookup<int, ErBarcode> extraBarcodes = request.Barcodes.ToLookup(b => b.ProductId);
        Category? general = null;
        int duplicateBarcodes = 0;

        foreach (ErProduct source in wanted)
        {
            Category category = CategoryFor(source);
            UnitOfMeasure unit = UnitFor(source.UnitId) ?? fallbackUnit;
            string name = Clean(source.Name, Product.MaximumDescriptionLength) is { Length: > 0 } n ? n : source.Code;

            string wantedCode = (Clean(source.Code, 200) ?? string.Empty).ToUpperInvariant();

            if (wantedCode.Length == 0)
            {
                wantedCode = $"ER{source.Id}";
            }

            // The same code and description already here is the same product, brought in
            // before or entered by hand: linked rather than copied.
            if (sameProduct.TryGetValue($"{wantedCode}|{Normalise(name)}", out Guid sameId))
            {
                links.Add("product", Key(source.Id), sameId);
                tally.Matched++;
                continue;
            }

            string code = FreeCode(
                wantedCode.Length > Product.MaximumCodeLength ? wantedCode[..Product.MaximumCodeLength].TrimEnd() : wantedCode,
                Product.MaximumCodeLength,
                productCodes);

            if (!string.Equals(code, wantedCode, StringComparison.Ordinal))
            {
                tally.Warn($"Product '{name}' is coded {code}: EasyRetail's code '{source.Code}' was too long or already used.");
            }

            Result<Product> created = Product.Create(
                category,
                unit,
                code,
                name,
                // EasyRetail's ItemTypeID names an item group (iPhone, tablet, watch), not
                // whether the product is stocked: everything it sells is a stocked item.
                ItemType.Stock,
                firm.BaseCurrency);

            if (created.IsFailure)
            {
                productCodes.Remove(code);
                tally.Warn($"Product '{name}' was not imported: {created.Error.Description}");
                continue;
            }

            Product product = created.Value;

            product.SetArabicDescription(Clean(source.NameArabic, Product.MaximumDescriptionLength));
            product.Describe(name, Clean(source.ShortDescription, Product.MaximumShortDescriptionLength), null);
            product.SetAttributes(
                Value(source.Manufacturer, Product.MaximumAttributeLength),
                null,
                Value(source.Size, Product.MaximumAttributeLength),
                Value(source.Origin, Product.MaximumAttributeLength));
            product.SetLocation(Clean(source.Rack, Product.MaximumAttributeLength), Clean(source.Bin, Product.MaximumAttributeLength));
            product.SetDeviceAttributes(new DeviceAttributes(
                Value(source.Device, DeviceAttributes.MaximumLength),
                Value(source.Colour, DeviceAttributes.MaximumLength),
                Value(source.Battery, DeviceAttributes.MaximumLength),
                Value(source.Ram, DeviceAttributes.MaximumLength),
                Value(source.Storage, DeviceAttributes.MaximumLength)));

            if (links.Find("brand", Key(source.BrandId)) is { } brandId && brands.TryGetValue(brandId, out Brand? brand))
            {
                product.SetBrand(brand);
            }

            if (UnitFor(source.PurchaseUnitId) is { } purchaseUnit && UnitFor(source.SalesUnitId) is { } salesUnit)
            {
                product.SetUnits(purchaseUnit, salesUnit, unit);
            }

            ProductRates rates = Rates(source, tally, name);
            product.SetRates(rates);

            if (StockLevels.Create(Positive(source.MinLevel), Positive(source.ReorderLevel), Positive(source.MaxLevel)) is { IsSuccess: true } levels)
            {
                product.SetLevels(levels.Value);
            }

            foreach ((string barcode, ProductRates? barcodeRates) in BarcodesOf(source))
            {
                if (!barcodesTaken.Add(barcode))
                {
                    duplicateBarcodes++;
                    continue;
                }

                if (product.AddBarcode(barcode, barcodeRates ?? rates).IsFailure)
                {
                    duplicateBarcodes++;
                }
            }

            if (!source.IsActive)
            {
                product.Deactivate();
            }

            _db.Products.Add(product);
            links.Add("product", Key(source.Id), product.Id.Value);
            tally.Created++;
        }

        if (duplicateBarcodes > 0)
        {
            tally.Warn($"{duplicateBarcodes} barcodes were left out because another product already carries them.");
        }

        links.Flush(_db, firm, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(tally.Result());

        UnitOfMeasure? UnitFor(int legacyId) =>
            links.Find("unit", Key(legacyId)) is { } id && units.TryGetValue(id, out UnitOfMeasure? found) ? found : null;

        // A product's category here is its EasyRetail sub-class filed under its category:
        // Spare > LCD, Phone > Phones. The sub-class is the finer of the two, and in
        // EasyRetail it stands beside the category rather than under it, so each pair
        // becomes one sub-category.
        Category CategoryFor(ErProduct source)
        {
            Category parent = links.Find("category", Key(source.CategoryId)) is { } parentId
                && categories.TryGetValue(parentId, out Category? linked) && linked.IsActive
                    ? linked
                    : General();

            if (!subClasses.TryGetValue(source.SubClassId, out string? subName)
                || Clean(subName, Category.MaximumNameLength) is not { Length: > 0 } cleaned
                || Normalise(cleaned) == Placeholder
                || Normalise(cleaned) == Normalise(parent.Name))
            {
                return parent;
            }

            string key = $"{source.CategoryId}:{source.SubClassId}";

            if (links.Find("subcategory", key) is { } childId && categories.TryGetValue(childId, out Category? child))
            {
                return child;
            }

            Category? existing = categories.Values.FirstOrDefault(c =>
                c.ParentId == parent.Id && Normalise(c.Name) == Normalise(cleaned));

            if (existing is null)
            {
                string childCode = FreeCode(Codes.FromName(cleaned, Category.MaximumCodeLength, "CATEGORY"), Category.MaximumCodeLength, categoryCodes);

                if (Category.CreateChild(parent, childCode, cleaned) is not { IsSuccess: true } created)
                {
                    return parent;
                }

                existing = created.Value;
                _db.Categories.Add(existing);
                categories[existing.Id.Value] = existing;
            }

            links.Add("subcategory", key, existing.Id.Value);
            return existing;
        }

        Category General()
        {
            if (general is not null)
            {
                return general;
            }

            general = categories.Values.FirstOrDefault(c => c.ParentId is null && Normalise(c.Name) == "general");

            if (general is null)
            {
                general = Category.CreateRoot(
                    firm.TenantId,
                    firm.Id,
                    FreeCode("GENERAL", Category.MaximumCodeLength, categoryCodes),
                    "General").Value;
                _db.Categories.Add(general);
                categories[general.Id.Value] = general;
            }

            return general;
        }

        IEnumerable<(string Barcode, ProductRates? Rates)> BarcodesOf(ErProduct source)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (string code in new[] { source.Upc, source.Ean })
            {
                string? clean = Clean(code, ProductBarcode.MaximumLength);

                if (clean is { Length: > 0 } && seen.Add(clean))
                {
                    yield return (clean, null);
                }
            }

            foreach (ErBarcode extra in extraBarcodes[source.Id])
            {
                string? clean = Clean(extra.Barcode, ProductBarcode.MaximumLength);

                if (clean is { Length: > 0 } && seen.Add(clean))
                {
                    Result<ProductRates> rates = ProductRates.Create(
                        Positive(extra.Cost), 0m, 0m, Positive(extra.Retail), Positive(extra.Wholesale), 0m, 0m);
                    yield return (clean, rates.IsSuccess && (extra.Retail > 0m || extra.Cost > 0m) ? rates.Value : null);
                }
            }
        }
    }

    private static ProductRates Rates(ErProduct source, Tally tally, string name)
    {
        Result<ProductRates> rates = ProductRates.Create(
            Positive(source.Cost),
            Positive(source.ProfitPercent),
            0m,
            Positive(source.Retail),
            Positive(source.Wholesale),
            Positive(source.Other),
            Positive(source.Mrp));

        if (rates.IsSuccess)
        {
            return rates.Value;
        }

        // The one rule EasyRetail does not keep: a retail rate above the MRP. The MRP is
        // dropped rather than the retail rate, since the retail rate is what the shop sells at.
        tally.Warn($"Product '{name}': {rates.Error.Description} The MRP was left blank.");

        return ProductRates.Create(
            Positive(source.Cost),
            Positive(source.ProfitPercent),
            0m,
            Positive(source.Retail),
            Positive(source.Wholesale),
            Positive(source.Other),
            0m).Value;
    }

    /// <inheritdoc />
    public async Task<Result<ImportBalancesResult>> ImportBalancesAsync(
        ImportBalancesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Firm> found = await FirmAsync(cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ImportBalancesResult>(found.Error);
        }

        Firm firm = found.Value;
        Links links = await Links.LoadAsync(_db, firm.Id, Source, cancellationToken);
        Tally tally = new();

        Dictionary<Guid, Ledger> ledgers = await _db.Ledgers
            .Where(ledger => ledger.FirmId == firm.Id)
            .ToDictionaryAsync(ledger => ledger.Id.Value, cancellationToken);
        Dictionary<Guid, AccountGroup> groups = await _db.AccountGroups
            .Where(group => group.FirmId == firm.Id)
            .ToDictionaryAsync(group => group.Id.Value, cancellationToken);

        // Several EasyRetail ledgers can become one here - its three service-income heads
        // all land on Service Income - so balances are added up per Inspire ledger first.
        Dictionary<Guid, decimal> balances = [];
        decimal unlinked = 0m;

        foreach (ErLedger source in request.Ledgers)
        {
            decimal balance = Math.Round(source.Balance, 2, MidpointRounding.AwayFromZero);

            if (links.Find("ledger", Key(source.Id)) is { } ledgerId && ledgers.ContainsKey(ledgerId))
            {
                balances[ledgerId] = balances.GetValueOrDefault(ledgerId) + balance;
            }
            else if (balance != 0m)
            {
                unlinked += balance;
                tally.Warn($"Ledger '{source.Name}' has a balance of {balance.ToString("N2", CultureInfo.InvariantCulture)} but was not imported; it is included in the difference.");
            }
        }

        decimal profit = 0m;
        decimal debit = 0m;
        decimal credit = 0m;

        foreach ((Guid ledgerId, decimal balance) in balances)
        {
            Ledger ledger = ledgers[ledgerId];
            AccountNature nature = groups.TryGetValue(ledger.AccountGroupId.Value, out AccountGroup? group)
                ? group.Nature
                : AccountNature.Asset;

            decimal opening = balance;

            if (request.CarryProfitToEquity && nature is AccountNature.Income or AccountNature.Expense)
            {
                profit += balance;
                opening = 0m;
            }

            SetOpening(ledger, opening);
            tally.Created++;
            debit += Math.Max(opening, 0m);
            credit += Math.Max(-opening, 0m);
        }

        Ledger? profitLedger = SpecialLedger(
            firm, links, groups, ledgers, "profit", "PL-BROUGHT-FORWARD", "Profit and loss brought forward");

        SetOpening(profitLedger, profit);
        debit += Math.Max(profit, 0m);
        credit += Math.Max(-profit, 0m);

        // What EasyRetail's own opening balances were out by, plus anything that could
        // not be brought in. Put on a ledger of its own, named for what it is, so the
        // trial balance agrees and the amount is in plain sight rather than buried.
        decimal difference = -(debit - credit);

        Ledger? differenceLedger = SpecialLedger(
            firm, links, groups, ledgers, "difference", "OPENING-DIFFERENCE", "Opening balance difference");

        SetOpening(differenceLedger, difference);

        if (difference != 0m)
        {
            tally.Warn(
                $"EasyRetail's opening balances were out by {Math.Abs(difference).ToString("N2", CultureInfo.InvariantCulture)} "
                + $"{(difference > 0m ? "on the credit side" : "on the debit side")}{(unlinked != 0m ? ", with the ledgers not imported" : string.Empty)}. "
                + "The difference is on the ledger 'Opening balance difference' for an accountant to clear.");
        }

        debit += Math.Max(difference, 0m);
        credit += Math.Max(-difference, 0m);

        links.Flush(_db, firm, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new ImportBalancesResult(tally.Result(), debit, credit, profit, -difference));
    }

    private static void SetOpening(Ledger? ledger, decimal amount)
    {
        ledger?.SetOpeningBalance(Math.Abs(amount), amount >= 0m ? EntrySide.Debit : EntrySide.Credit);
    }

    private Ledger? SpecialLedger(
        Firm firm,
        Links links,
        Dictionary<Guid, AccountGroup> groups,
        Dictionary<Guid, Ledger> ledgers,
        string key,
        string code,
        string name)
    {
        if (links.Find("special", key) is { } linkedId && ledgers.TryGetValue(linkedId, out Ledger? linked))
        {
            return linked;
        }

        Ledger? existing = ledgers.Values.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            AccountGroup? group = groups.Values.FirstOrDefault(g => g.Code == "3200")
                ?? groups.Values.FirstOrDefault(g => g.Nature == AccountNature.Equity && g.ParentId is null)
                ?? groups.Values.FirstOrDefault(g => g.Nature == AccountNature.Liability && g.ParentId is null);

            if (group is null)
            {
                return null;
            }

            Result<Ledger> created = Ledger.Create(group, code, name, LedgerKind.General, firm.BaseCurrency);

            if (created.IsFailure)
            {
                return null;
            }

            existing = created.Value;
            _db.Ledgers.Add(existing);
            ledgers[existing.Id.Value] = existing;
        }

        links.Add("special", key, existing.Id.Value);
        return existing;
    }

    /// <inheritdoc />
    public async Task<Result<ImportStepResult>> ImportStockAsync(
        ImportStockRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Firm> found = await FirmAsync(cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ImportStepResult>(found.Error);
        }

        Firm firm = found.Value;
        Links links = await Links.LoadAsync(_db, firm.Id, Source, cancellationToken);
        Tally tally = new();

        Dictionary<Guid, Product> products = await _db.Products
            .AsNoTracking()
            .Where(product => product.FirmId == firm.Id)
            .ToDictionaryAsync(product => product.Id.Value, cancellationToken);
        Guid? defaultWarehouse = await _db.Warehouses
            .Where(warehouse => warehouse.FirmId == firm.Id && warehouse.IsDefault)
            .Select(warehouse => (Guid?)warehouse.Id.Value)
            .FirstOrDefaultAsync(cancellationToken);

        int negative = 0;
        Dictionary<Guid, List<(string Key, StockDocumentLineInput Line)>> byWarehouse = [];

        foreach (ErStock row in request.Rows)
        {
            string key = $"{row.ProductId}:{row.WarehouseId}:{row.Batch.Trim()}";

            if (links.Find("stock", key) is not null)
            {
                tally.AlreadyImported++;
                continue;
            }

            if (row.Quantity <= 0m)
            {
                negative++;
                tally.Skipped++;
                continue;
            }

            if (links.Find("product", Key(row.ProductId)) is not { } productId
                || !products.TryGetValue(productId, out Product? product)
                || !product.IsStocked)
            {
                tally.Skipped++;
                continue;
            }

            Guid? warehouseId = links.Find("warehouse", Key(row.WarehouseId)) ?? defaultWarehouse;

            if (warehouseId is null)
            {
                tally.Skipped++;
                continue;
            }

            if (!byWarehouse.TryGetValue(warehouseId.Value, out List<(string Key, StockDocumentLineInput Line)>? lines))
            {
                lines = [];
                byWarehouse[warehouseId.Value] = lines;
            }

            // EasyRetail can hold one product twice in a warehouse - under its "Primary"
            // batch and under none - and a document takes a product once, so the two
            // quantities become one line.
            int same = lines.FindIndex(line => line.Line.ProductId == productId);

            if (same >= 0)
            {
                StockDocumentLineInput merged = lines[same].Line with { Quantity = lines[same].Line.Quantity + row.Quantity };
                lines[same] = (lines[same].Key, merged);
                lines.Add((key, merged with { Quantity = 0m }));
                continue;
            }

            lines.Add((key, new StockDocumentLineInput(
                productId,
                row.Quantity,
                Rate: Positive(row.Cost) > 0m ? Positive(row.Cost) : product.Rates.Cost)));
        }

        if (negative > 0)
        {
            tally.Warn($"{negative} stock quantities were zero or below in EasyRetail - more was sold than was recorded in - and were left out.");
        }

        foreach ((Guid warehouseId, List<(string Key, StockDocumentLineInput Line)> lines) in byWarehouse)
        {
            Result<CreateStockDocumentResponse> posted = await _sender.Send(
                new CreateStockDocumentCommand(
                    StockDocumentType.OpeningStock,
                    request.AsAt,
                    warehouseId,
                    [.. lines.Where(line => line.Line.Quantity > 0m).Select(line => line.Line)],
                    ReferenceNumber: "EASYRETAIL",
                    Narration: "Opening stock imported from EasyRetail",
                    PostImmediately: true),
                cancellationToken);

            if (posted.IsFailure)
            {
                return Result.Failure<ImportStepResult>(posted.Error);
            }

            foreach ((string key, _) in lines)
            {
                links.Add("stock", key, posted.Value.StockDocumentId);
            }

            tally.Created += lines.Count(line => line.Line.Quantity > 0m);
        }

        links.Flush(_db, firm, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(tally.Result());
    }

    private async Task<Result<Firm>> FirmAsync(CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Firm>(Error.Forbidden(
                "Import.NoFirmSelected", "Select the firm to import into first."));
        }

        Firm? firm = await _db.Firms.FirstOrDefaultAsync(f => f.Id == firmId, cancellationToken);

        return firm is null
            ? Result.Failure<Firm>(Error.NotFound("Firm.NotFound", "The selected firm no longer exists."))
            : Result.Success(firm);
    }

    private static Dictionary<int, T> ByLedger<T>(IReadOnlyList<T> items, Func<T, int> ledgerId) =>
        items.GroupBy(ledgerId).ToDictionary(group => group.Key, group => group.First());

    private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static decimal Positive(decimal value) => value > 0m ? value : 0m;

    private static bool IsUsableCode(string code) =>
        !string.IsNullOrWhiteSpace(code) && code.Trim().Length <= 30;

    /// <summary>Trims, collapses inner whitespace and caps the length; empty becomes null.</summary>
    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length > maxLength ? collapsed[..maxLength].TrimEnd() : collapsed;
    }

    /// <summary>Like <see cref="Clean"/>, but EasyRetail's "Primary" placeholder is no value.</summary>
    private static string? Value(string? value, int maxLength) =>
        Clean(value, maxLength) is { } cleaned && Normalise(cleaned) != Placeholder ? cleaned : null;

    /// <summary>"Current Liabilities &amp; Provisions" becomes "currentliabilitiesandprovisions".</summary>
    private static string Normalise(string value)
    {
        StringBuilder normal = new(value.Length);

        foreach (char c in value.Replace("&", "and", StringComparison.Ordinal).Where(char.IsLetterOrDigit))
        {
            normal.Append(char.ToLowerInvariant(c));
        }

        return normal.ToString();
    }

    /// <summary>The code itself, or it with -2, -3 and so on, whichever is not taken; then takes it.</summary>
    private static string FreeCode(string wanted, int maxLength, HashSet<string> taken)
    {
        string candidate = wanted;
        int attempt = 1;

        while (taken.Contains(candidate))
        {
            attempt++;
            string suffix = $"-{attempt.ToString(CultureInfo.InvariantCulture)}";
            candidate = wanted[..Math.Min(wanted.Length, maxLength - suffix.Length)] + suffix;
        }

        taken.Add(candidate);
        return candidate;
    }

    /// <summary>
    /// Where a customer or supplier is and their other numbers: the country and state
    /// matched by name to the firm's own lists, the area and route found or added.
    /// </summary>
    private static void ApplyPartyDetails(Ledger ledger, ErParty details, PartyPlaces places)
    {
        Country? country = places.FindCountry(details.Country);

        if (places.FindRegion(country, details.State) is { } region)
        {
            ledger.SetTaxDetails(Clean(details.TaxNumber, 50), region.Code);
        }

        ledger.SetPartyDetails(new PartyDetails(
            Clean(details.City, PartyDetails.MaximumNameLength),
            Clean(details.PinCode, PartyDetails.MaximumCodeLength),
            Clean(details.Website, PartyDetails.MaximumWebsiteLength),
            Clean(details.Fax, PartyDetails.MaximumCodeLength),
            Clean(details.ContactPerson, PartyDetails.MaximumNameLength),
            Clean(details.GstId, PartyDetails.MaximumRegistrationLength),
            Clean(details.Tin, PartyDetails.MaximumRegistrationLength),
            Clean(details.CstNumber, PartyDetails.MaximumRegistrationLength),
            country?.Code,
            places.TerritoryFor(TerritoryKind.Area, details.Area)?.Id,
            places.TerritoryFor(TerritoryKind.Route, details.Route)?.Id,
            details.IsSms == 1));
    }

    /// <summary>The firm's countries, states, areas and routes, as an import matches parties to them.</summary>
    private sealed class PartyPlaces
    {
        private readonly ErpDbContext _db;
        private readonly Firm _firm;
        private readonly Dictionary<string, Country> _countries;
        private readonly List<Region> _regions;
        private readonly List<Territory> _territories;

        private PartyPlaces(
            ErpDbContext db,
            Firm firm,
            Dictionary<string, Country> countries,
            List<Region> regions,
            List<Territory> territories)
        {
            _db = db;
            _firm = firm;
            _countries = countries;
            _regions = regions;
            _territories = territories;
        }

        /// <summary>Reads the firm's lists once for the whole import.</summary>
        public static async Task<PartyPlaces> LoadAsync(
            ErpDbContext db,
            Firm firm,
            CancellationToken cancellationToken)
        {
            List<Country> countries = await db.Countries
                .Where(country => country.FirmId == firm.Id)
                .ToListAsync(cancellationToken);

            // By name and by code: EasyRetail spells a country out, in capitals.
            Dictionary<string, Country> byKey = new(StringComparer.Ordinal);

            foreach (Country country in countries)
            {
                byKey.TryAdd(Normalise(country.Name), country);
                byKey.TryAdd(Normalise(country.Code), country);
            }

            return new PartyPlaces(
                db,
                firm,
                byKey,
                await db.Regions.Where(region => region.FirmId == firm.Id).ToListAsync(cancellationToken),
                await db.Territories.Where(territory => territory.FirmId == firm.Id).ToListAsync(cancellationToken));
        }

        /// <summary>The country of that name, if the firm has one.</summary>
        public Country? FindCountry(string? name) =>
            Value(name, Country.MaximumNameLength) is { } cleaned
                && _countries.TryGetValue(Normalise(cleaned), out Country? country)
                ? country
                : null;

        /// <summary>The state of that name in the country, if the firm has one.</summary>
        public Region? FindRegion(Country? country, string? name)
        {
            if (country is null || Value(name, Region.MaximumNameLength) is not { } cleaned)
            {
                return null;
            }

            string normal = Normalise(cleaned);

            return _regions.Find(region => region.CountryId == country.Id
                && (Normalise(region.Name) == normal || Normalise(region.Code) == normal));
        }

        /// <summary>The area or route of that name, added to the firm's list when it is new.</summary>
        public Territory? TerritoryFor(TerritoryKind kind, string? name)
        {
            if (Value(name, Territory.MaximumNameLength) is not { } cleaned)
            {
                return null;
            }

            string normal = Normalise(cleaned);
            Territory? found = _territories.Find(territory =>
                territory.Kind == kind && Normalise(territory.Name) == normal);

            if (found is not null)
            {
                return found;
            }

            Result<Territory> created = Territory.Create(_firm.TenantId, _firm.Id, kind, cleaned);

            if (created.IsFailure)
            {
                return null;
            }

            _db.Territories.Add(created.Value);
            _territories.Add(created.Value);

            return created.Value;
        }
    }

    /// <summary>Counts what a step did and collects what it has to say.</summary>
    private sealed class Tally
    {
        private readonly List<string> _warnings = [];

        public int Created { get; set; }

        public int Matched { get; set; }

        public int AlreadyImported { get; set; }

        public int Skipped { get; set; }

        public void Warn(string warning)
        {
            // Enough to act on; a thousand of the same would bury the rest.
            if (_warnings.Count < 200)
            {
                _warnings.Add(warning);
            }
        }

        public ImportStepResult Result() => new(Created, Matched, AlreadyImported, Skipped, _warnings);
    }

    /// <summary>The links of one import, read once and written once.</summary>
    private sealed class Links
    {
        private readonly Dictionary<(string Kind, string Key), Guid> _known;
        private readonly Dictionary<(string Kind, string Key), Guid> _added = [];

        private Links(Dictionary<(string Kind, string Key), Guid> known) => _known = known;

        public static async Task<Links> LoadAsync(
            ErpDbContext db,
            FirmId firmId,
            string source,
            CancellationToken cancellationToken)
        {
            List<ImportLink> links = await db.ImportLinks
                .AsNoTracking()
                .Where(link => link.FirmId == firmId && link.Source == source)
                .ToListAsync(cancellationToken);

            return new Links(links.ToDictionary(link => (link.Kind, link.LegacyKey), link => link.TargetId));
        }

        public Guid? Find(string kind, string key)
        {
            if (_added.TryGetValue((kind, key), out Guid added))
            {
                return added;
            }

            return _known.TryGetValue((kind, key), out Guid known) ? known : null;
        }

        /// <summary>Whether an earlier run linked this record, as against one earlier in this run.</summary>
        public bool IsFromEarlierRun(string kind, string key) => _known.ContainsKey((kind, key));

        public void Add(string kind, string key, Guid targetId) => _added[(kind, key)] = targetId;

        public void Flush(ErpDbContext db, Firm firm, DateTimeOffset nowUtc)
        {
            foreach (((string kind, string key), Guid targetId) in _added)
            {
                if (_known.ContainsKey((kind, key)))
                {
                    ImportLink existing = db.ImportLinks.Local.FirstOrDefault(link =>
                        link.FirmId == firm.Id && link.Source == Source && link.Kind == kind && link.LegacyKey == key)
                        ?? db.ImportLinks.Attach(ImportLink.Record(firm.TenantId, firm.Id, Source, kind, key, targetId, nowUtc)).Entity;
                    existing.Relink(targetId, nowUtc);
                    db.Entry(existing).State = EntityState.Modified;
                    continue;
                }

                db.ImportLinks.Add(ImportLink.Record(firm.TenantId, firm.Id, Source, kind, key, targetId, nowUtc));
            }

            _added.Clear();
        }
    }
}
