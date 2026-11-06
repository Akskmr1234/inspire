using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Platform.Branches;
using ERP.Application.Platform.Firms;
using ERP.Domain.Identity;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Seeding;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Security;

/// <summary>The tenant's firms.</summary>
/// <remarks>
/// A firm is a set of books of its own, so a new one is set up the way the first was at
/// installation, by the same seeding steps: without a chart of accounts and an open
/// year it could not post a single document. Its tax regime and time zone follow the
/// firm it is added from - a tenant trades under one set of rules.
/// </remarks>
public sealed class FirmAdministration : IFirmAdministration
{
    private readonly ErpDbContext _context;
    private readonly DatabaseSeeder _seeder;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    /// <summary>Initialises a new instance of the <see cref="FirmAdministration"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="seeder">The seeder whose firm steps set a new firm up.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The signed-in user.</param>
    public FirmAdministration(
        ErpDbContext context,
        DatabaseSeeder seeder,
        ITenantContext tenantContext,
        ICurrentUser currentUser)
    {
        _context = context;
        _seeder = seeder;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FirmSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        List<Firm> firms = await _context.Firms.AsNoTracking()
            .Include(firm => firm.Branches)
            .OrderBy(firm => firm.Code)
            .ToListAsync(cancellationToken);

        return await DescribeAsync(firms, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FirmSummary>> ListMineAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return [];
        }

        UserId me = _currentUser.UserId;
        List<UserFirmAccess> mine = await _context.Users.AsNoTracking()
            .Where(user => user.Id == me)
            .SelectMany(user => user.FirmAccess)
            .ToListAsync(cancellationToken);
        HashSet<FirmId> firmIds = [.. mine.Select(access => access.FirmId)];
        HashSet<BranchId> branchIds = [.. mine
            .Where(access => access.BranchId.HasValue)
            .Select(access => access.BranchId!.Value)];

        List<Firm> firms = await _context.Firms.AsNoTracking()
            .Include(firm => firm.Branches)
            .Where(firm => firm.IsActive && firmIds.Contains(firm.Id))
            .OrderBy(firm => firm.Code)
            .ToListAsync(cancellationToken);

        return await DescribeAsync(
            firms, branch => branch.IsActive && branchIds.Contains(branch.Id), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<FirmSummary>> CreateAsync(
        SaveFirmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        TenantId tenantId = _tenantContext.TenantId;

        Result<CurrencyCode> currency = CurrencyCode.Create(request.BaseCurrency);

        if (currency.IsFailure)
        {
            return Result.Failure<FirmSummary>(currency.Error);
        }

        string code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();

        if (await _context.Firms.AnyAsync(firm => firm.Code == code, cancellationToken))
        {
            return Result.Failure<FirmSummary>(Error.Conflict(
                "Firm.DuplicateCode", $"There is already a firm with code '{code}'."));
        }

        // Its rules follow the firm it is added from: one tenant, one tax regime.
        Firm? from = await CurrentFirmAsync(cancellationToken);

        if (from is null)
        {
            return Result.Failure<FirmSummary>(Error.NotFound(
                "Firm.NotFound", "There is no firm in session to add another from."));
        }

        Result<Firm> created = Firm.Create(
            tenantId, code, request.Name, currency.Value, from.TaxRegime, from.TimeZoneId);

        if (created.IsFailure)
        {
            return Result.Failure<FirmSummary>(created.Error);
        }

        Firm firm = created.Value;
        Result details = Apply(firm, request);

        if (details.IsFailure)
        {
            return Result.Failure<FirmSummary>(details.Error);
        }

        Branch head = firm.AddBranch("HO", "Head Office", isHeadOffice: true).Value;
        _context.Firms.Add(firm);

        // Whoever adds a firm may work in it at once, in its head office.
        if (_currentUser.IsAuthenticated)
        {
            UserId me = _currentUser.UserId;
            User? maker = await _context.Users
                .Include(user => user.FirmAccess)
                .FirstOrDefaultAsync(user => user.Id == me, cancellationToken);

            maker?.GrantFirmAccess(firm.Id, head.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _seeder.SetUpFirmAsync(firm, cancellationToken);

        return Result.Success((await DescribeAsync([firm], null, cancellationToken))[0]);
    }

    /// <inheritdoc />
    public async Task<Result<FirmSummary>> UpdateAsync(
        Guid firmId,
        SaveFirmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Firm? firm = await _context.Firms
            .Include(candidate => candidate.Branches)
            .FirstOrDefaultAsync(candidate => candidate.Id == FirmId.From(firmId), cancellationToken);

        if (firm is null)
        {
            return Result.Failure<FirmSummary>(Error.NotFound(
                "Firm.NotFound", "That firm does not exist."));
        }

        // The firm the session is working in cannot be put away from under it.
        if (!request.IsActive && firm.Id == _tenantContext.FirmId)
        {
            return Result.Failure<FirmSummary>(Error.BusinessRule(
                "Firm.InUse", "The firm you are working in cannot be made inactive. Switch to another first."));
        }

        Result renamed = firm.Rename(request.Name);

        if (renamed.IsFailure)
        {
            return Result.Failure<FirmSummary>(renamed.Error);
        }

        Result details = Apply(firm, request);

        if (details.IsFailure)
        {
            return Result.Failure<FirmSummary>(details.Error);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success((await DescribeAsync([firm], null, cancellationToken))[0]);
    }

    private static Result Apply(Firm firm, SaveFirmRequest request)
    {
        firm.SetArabicName(request.NameArabic);

        Result tax = firm.SetTaxRegistration(request.TaxRegistrationNumber, firm.StateCode);

        if (tax.IsFailure)
        {
            return tax;
        }

        if (request.IsActive)
        {
            firm.Activate();
        }
        else
        {
            firm.Deactivate();
        }

        return Result.Success();
    }

    private async Task<Firm?> CurrentFirmAsync(CancellationToken cancellationToken) =>
        _tenantContext.FirmId is { } id
            ? await _context.Firms.AsNoTracking().FirstOrDefaultAsync(firm => firm.Id == id, cancellationToken)
            : await _context.Firms.AsNoTracking().OrderBy(firm => firm.Code).FirstOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyList<FirmSummary>> DescribeAsync(
        IReadOnlyList<Firm> firms,
        Func<Branch, bool>? branchFilter,
        CancellationToken cancellationToken)
    {
        List<FirmId> ids = [.. firms.Select(firm => firm.Id)];
        var access = await _context.Users.AsNoTracking()
            .SelectMany(user => user.FirmAccess, (user, row) => new { user.Id, row.FirmId, row.BranchId })
            .Where(row => ids.Contains(row.FirmId))
            .ToListAsync(cancellationToken);

        return
        [
            .. firms.Select(firm => new FirmSummary(
                firm.Id.Value,
                firm.Code,
                firm.Name,
                firm.NameArabic,
                firm.BaseCurrency.Code,
                firm.TaxRegistrationNumber,
                firm.IsActive,
                access.Where(row => row.FirmId == firm.Id).Select(row => row.Id).Distinct().Count(),
                [
                    .. firm.Branches
                        .Where(branch => branchFilter?.Invoke(branch) ?? true)
                        .OrderByDescending(branch => branch.IsHeadOffice)
                        .ThenBy(branch => branch.Code, StringComparer.Ordinal)
                        .Select(branch => new BranchSummary(
                            branch.Id.Value,
                            branch.Code,
                            branch.Name,
                            branch.NameArabic,
                            branch.IsHeadOffice,
                            branch.IsActive,
                            branch.AddressLine1,
                            branch.Phone,
                            branch.Email,
                            access.Where(row => row.BranchId == branch.Id).Select(row => row.Id).Distinct().Count())),
                ])),
        ];
    }
}
