using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Platform.Branches;
using ERP.Domain.Identity;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Security;

/// <summary>The branches of the firm in session.</summary>
/// <remarks>
/// Kept beside user administration, which it works hand in hand with: a branch is no
/// use until somebody may work in it, so the one who adds it may at once, and the
/// users screen grants it to the rest.
/// </remarks>
public sealed class BranchAdministration : IBranchAdministration
{
    private readonly ErpDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    /// <summary>Initialises a new instance of the <see cref="BranchAdministration"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The signed-in user.</param>
    public BranchAdministration(
        ErpDbContext context,
        ITenantContext tenantContext,
        ICurrentUser currentUser)
    {
        _context = context;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BranchSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        Firm? firm = await FirmAsync(cancellationToken);
        return firm is null ? [] : await DescribeAsync(firm.Branches, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BranchSummary>> ListMineAsync(
        CancellationToken cancellationToken = default)
    {
        Firm? firm = await FirmAsync(cancellationToken);

        if (firm is null || !_currentUser.IsAuthenticated)
        {
            return [];
        }

        UserId me = _currentUser.UserId;
        List<BranchId> mine = await _context.Users.AsNoTracking()
            .Where(user => user.Id == me)
            .SelectMany(user => user.FirmAccess)
            .Where(access => access.FirmId == firm.Id && access.BranchId != null)
            .Select(access => access.BranchId!.Value)
            .ToListAsync(cancellationToken);

        return await DescribeAsync(
            firm.Branches.Where(branch => branch.IsActive && mine.Contains(branch.Id)),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<BranchSummary>> CreateAsync(
        SaveBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Firm? firm = await FirmAsync(cancellationToken, tracked: true);

        if (firm is null)
        {
            return Result.Failure<BranchSummary>(Error.NotFound(
                "Firm.NotFound", "There is no firm in session to add a branch to."));
        }

        Result<Branch> added = firm.AddBranch(request.Code, request.Name);

        if (added.IsFailure)
        {
            return Result.Failure<BranchSummary>(added.Error);
        }

        Branch branch = added.Value;
        Apply(branch, request);

        // Whoever adds a branch may work in it at once; everybody else is given it on
        // the users screen. A branch nobody could reach would be added to no purpose.
        if (_currentUser.IsAuthenticated)
        {
            UserId me = _currentUser.UserId;
            User? maker = await _context.Users
                .Include(user => user.FirmAccess)
                .FirstOrDefaultAsync(user => user.Id == me, cancellationToken);

            maker?.GrantFirmAccess(firm.Id, branch.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success((await DescribeAsync([branch], cancellationToken))[0]);
    }

    /// <inheritdoc />
    public async Task<Result<BranchSummary>> UpdateAsync(
        Guid branchId,
        SaveBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Firm? firm = await FirmAsync(cancellationToken, tracked: true);
        Branch? branch = firm?.Branches.FirstOrDefault(
            candidate => candidate.Id == BranchId.From(branchId));

        if (branch is null)
        {
            return Result.Failure<BranchSummary>(Error.NotFound(
                "Branch.NotFound", "That branch does not exist."));
        }

        Result renamed = branch.Rename(request.Name);

        if (renamed.IsFailure)
        {
            return Result.Failure<BranchSummary>(renamed.Error);
        }

        // The head office is where the firm's books are kept: it cannot be put away.
        if (!request.IsActive && branch.IsHeadOffice)
        {
            return Result.Failure<BranchSummary>(Error.BusinessRule(
                "Branch.HeadOfficeActive", "The head office cannot be made inactive."));
        }

        Apply(branch, request);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success((await DescribeAsync([branch], cancellationToken))[0]);
    }

    private static void Apply(Branch branch, SaveBranchRequest request)
    {
        branch.SetArabicName(request.NameArabic);
        branch.SetContactDetails(request.Address, null, request.Phone, request.Email);

        if (request.IsActive)
        {
            branch.Activate();
        }
        else
        {
            branch.Deactivate();
        }
    }

    /// <summary>The firm in session, with its branches; the tenant's first where none is named.</summary>
    private async Task<Firm?> FirmAsync(CancellationToken cancellationToken, bool tracked = false)
    {
        IQueryable<Firm> firms = _context.Firms.Include(firm => firm.Branches);

        if (!tracked)
        {
            firms = firms.AsNoTracking();
        }

        FirmId? firmId = _tenantContext.FirmId;

        return firmId is { } id
            ? await firms.FirstOrDefaultAsync(firm => firm.Id == id, cancellationToken)
            : await firms.OrderBy(firm => firm.Code).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<BranchSummary>> DescribeAsync(
        IEnumerable<Branch> branches,
        CancellationToken cancellationToken)
    {
        List<Branch> listed = [.. branches
            .OrderByDescending(branch => branch.IsHeadOffice)
            .ThenBy(branch => branch.Code, StringComparer.Ordinal)];
        List<BranchId> ids = [.. listed.Select(branch => branch.Id)];

        var counts = await _context.Users.AsNoTracking()
            .SelectMany(user => user.FirmAccess)
            .Where(access => access.BranchId != null && ids.Contains(access.BranchId.Value))
            .GroupBy(access => access.BranchId)
            .Select(group => new { Branch = group.Key, Users = group.Count() })
            .ToListAsync(cancellationToken);

        return
        [
            .. listed.Select(branch => new BranchSummary(
                branch.Id.Value,
                branch.Code,
                branch.Name,
                branch.NameArabic,
                branch.IsHeadOffice,
                branch.IsActive,
                branch.AddressLine1,
                branch.Phone,
                branch.Email,
                counts.FirstOrDefault(count => count.Branch == branch.Id)?.Users ?? 0)),
        ];
    }
}
