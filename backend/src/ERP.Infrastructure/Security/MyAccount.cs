using ERP.Application.Abstractions.Security;
using ERP.Application.Platform.Users;
using ERP.Domain.Identity;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Security;

/// <summary>The EF Core implementation of <see cref="IMyAccount"/>.</summary>
/// <remarks>
/// A sign-in is a family of refresh tokens: signing in starts one, and every renewal
/// retires the token it used and issues the next in the same family. So the family's
/// first token says when the user signed in, its live token says when the device last
/// renewed, and revoking the family is signing that device out.
/// </remarks>
public sealed class MyAccount : IMyAccount
{
    private readonly ErpDbContext _context;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="MyAccount"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="permissionChecker">The permission cache, cleared when sessions end.</param>
    /// <param name="clock">The clock.</param>
    public MyAccount(ErpDbContext context, IPermissionChecker permissionChecker, IClock clock)
    {
        _context = context;
        _permissionChecker = permissionChecker;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<MyAccountResponse?> GetAsync(
        UserId userId,
        Guid? currentSession,
        CancellationToken cancellationToken = default)
    {
        User? user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null ? null : await DescribeAsync(user, currentSession, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<MyAccountResponse>> UpdateAsync(
        UserId userId,
        Guid? currentSession,
        UpdateMyAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Result.Failure<MyAccountResponse>(Error.Validation(
                "User.DisplayNameRequired", "Your name is required."));
        }

        if (request.DisplayName.Trim().Length > 150)
        {
            return Result.Failure<MyAccountResponse>(Error.Validation(
                "User.DisplayNameTooLong", "Your name cannot exceed 150 characters."));
        }

        User? user = await _context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<MyAccountResponse>(Error.NotFound(
                "User.NotFound", "Your account no longer exists."));
        }

        Result email = user.ChangeEmail(request.Email ?? string.Empty);

        if (email.IsFailure)
        {
            return Result.Failure<MyAccountResponse>(email.Error);
        }

        bool emailTaken = await _context.Users.AnyAsync(
            other => other.Email == user.Email && other.Id != user.Id,
            cancellationToken);

        if (emailTaken)
        {
            return Result.Failure<MyAccountResponse>(Error.Conflict(
                "User.EmailTaken",
                $"The email address '{user.Email}' is already used by another user."));
        }

        user.SetContactDetails(request.DisplayName, request.MobileNumber);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await DescribeAsync(user, currentSession, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result> EndSessionAsync(
        UserId userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        List<RefreshToken> live = await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.FamilyId == sessionId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        if (live.Count == 0)
        {
            return Result.Failure(Error.NotFound(
                "Session.NotFound", "That sign-in has already ended."));
        }

        DateTimeOffset now = _clock.UtcNow;

        foreach (RefreshToken token in live)
        {
            token.Revoke(RefreshTokenRevocationReason.SignedOut, now);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _permissionChecker.InvalidateAsync(userId, cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<int> EndOtherSessionsAsync(
        UserId userId,
        Guid? currentSession,
        CancellationToken cancellationToken = default)
    {
        List<RefreshToken> live = await _context.RefreshTokens
            .Where(t => t.UserId == userId
                && t.RevokedAtUtc == null
                && (currentSession == null || t.FamilyId != currentSession))
            .ToListAsync(cancellationToken);

        DateTimeOffset now = _clock.UtcNow;

        foreach (RefreshToken token in live)
        {
            token.Revoke(RefreshTokenRevocationReason.SignedOut, now);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _permissionChecker.InvalidateAsync(userId, cancellationToken);

        return live.Select(t => t.FamilyId).Distinct().Count();
    }

    private async Task<MyAccountResponse> DescribeAsync(
        User user,
        Guid? currentSession,
        CancellationToken cancellationToken)
    {
        List<RoleId> roleIds = [.. user.Roles.Select(r => r.RoleId)];

        List<MyRole> roles = await _context.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .OrderBy(r => r.Name)
            .Select(r => new MyRole(r.Name, r.Description, r.GrantsAllPermissions))
            .ToListAsync(cancellationToken);

        Tenant? tenant = await _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == user.TenantId, cancellationToken);

        DateTimeOffset now = _clock.UtcNow;

        // Every token of the user's still-live families, so the first one of each says
        // when that sign-in began. Retired tokens are kept with a reason, which is what
        // makes this possible.
        var tokens = await _context.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == user.Id
                && _context.RefreshTokens.Any(live => live.FamilyId == t.FamilyId
                    && live.RevokedAtUtc == null
                    && live.ExpiresAtUtc > now))
            .Select(t => new
            {
                t.FamilyId,
                t.IssuedAtUtc,
                t.ExpiresAtUtc,
                t.RevokedAtUtc,
                t.UserAgent,
                t.IpAddress,
            })
            .ToListAsync(cancellationToken);

        List<MySession> sessions = [.. tokens
            .GroupBy(t => t.FamilyId)
            .Select(family =>
            {
                var latest = family
                    .Where(t => t.RevokedAtUtc == null)
                    .OrderByDescending(t => t.IssuedAtUtc)
                    .First();

                return new MySession(
                    family.Key,
                    family.Min(t => t.IssuedAtUtc),
                    latest.IssuedAtUtc,
                    latest.ExpiresAtUtc,
                    latest.UserAgent,
                    latest.IpAddress,
                    family.Key == currentSession);
            })
            .OrderByDescending(s => s.IsCurrent)
            .ThenByDescending(s => s.LastActiveAtUtc)];

        return new MyAccountResponse(
            user.Id.Value,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.MobileNumber,
            tenant?.Code ?? string.Empty,
            tenant?.Name ?? string.Empty,
            roles,
            user.CreatedAtUtc,
            user.LastLoginAtUtc,
            user.PasswordChangedAtUtc,
            user.MustChangePassword,
            sessions);
    }
}
