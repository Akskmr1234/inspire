using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Identity;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Security;

/// <summary>The default <see cref="IAuthenticationService"/>.</summary>
/// <remarks>
/// Lives in Infrastructure rather than in ERP.Identity because it needs the
/// database. ERP.Identity holds the contracts and the pure mechanisms - hashing,
/// token construction, policy evaluation - which stay independently testable.
/// </remarks>
public sealed partial class AuthenticationService : IAuthenticationService
{
    /// <summary>
    /// A syntactically valid hash that no password matches, verified against when
    /// no user is found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without it, a sign-in for an unknown user returns the moment the lookup
    /// misses, while one for a real user pays for a full PBKDF2 verification. The
    /// difference is measurable from outside and turns the endpoint into a
    /// user-name oracle: an attacker learns which accounts exist by timing alone,
    /// which is exactly what the uniform error message is meant to hide.
    /// </para>
    /// <para>
    /// The salt and hash are arbitrary bytes of the right length. Nothing needs to
    /// verify against this; it exists only so the work is done.
    /// </para>
    /// </remarks>
    private const string DecoyHash =
        "pbkdf2-sha256$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private readonly ErpDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IPermissionChecker _permissionChecker;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly ILogger<AuthenticationService> _logger;

    /// <summary>Initialises a new instance of the <see cref="AuthenticationService"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="passwordHasher">The password hasher.</param>
    /// <param name="tokenService">The token service.</param>
    /// <param name="permissionChecker">The permission checker.</param>
    /// <param name="tenantContext">The ambient tenant scope, established here during sign-in.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">The logger.</param>
    public AuthenticationService(
        ErpDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IPermissionChecker permissionChecker,
        ITenantContext tenantContext,
        IClock clock,
        ILogger<AuthenticationService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _permissionChecker = permissionChecker;
        _tenantContext = tenantContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the tenant a sign-in attempt belongs to, before any tenant scope
    /// exists.
    /// </summary>
    /// <param name="tenantCode">The supplied company code, if any.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tenant, or a failure.</returns>
    /// <remarks>
    /// Queries the tenant registry, which is deliberately the one table with no
    /// tenant discriminator and no row-level-security policy. Nothing here is
    /// confidential, and every query after this point runs inside a properly
    /// established scope.
    /// <para>
    /// When no code is supplied and the installation holds exactly one tenant, it
    /// is used. That is the on-premises single-firm case the specification calls
    /// for, and nobody running one should have to type a company code. With more
    /// than one tenant present the code becomes mandatory, because guessing would
    /// mean signing somebody in to the wrong company's books.
    /// </para>
    /// </remarks>
    private async Task<Result<Tenant>> ResolveTenantAsync(
        string? tenantCode,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(tenantCode))
        {
            string code = tenantCode.Trim().ToLowerInvariant();

            Tenant? byCode = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

            return byCode is null
                ? Result.Failure<Tenant>(AuthenticationErrors.TenantNotResolved)
                : Result.Success(byCode);
        }

        // Take two so "exactly one" can be distinguished from "several" without a
        // second round trip.
        List<Tenant> candidates = await _context.Tenants
            .Where(t => t.IsActive)
            .Take(2)
            .ToListAsync(cancellationToken);

        return candidates.Count == 1
            ? Result.Success(candidates[0])
            : Result.Failure<Tenant>(AuthenticationErrors.TenantNotResolved);
    }

    /// <inheritdoc />
    public async Task<Result<AuthenticationResponse>> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = _clock.UtcNow;
        string identifier = request.UserName?.Trim().ToLowerInvariant() ?? string.Empty;

        // Sign-in arrives unauthenticated, so nothing has established a tenant.
        // Until one is, the global query filter compares against default(TenantId)
        // and the row-level-security policy against NULL - so the users table
        // reads as empty and every attempt fails with "invalid credentials",
        // however correct the password is. Resolving the tenant first is what
        // makes authentication possible at all.
        Result<Tenant> tenant = await ResolveTenantAsync(request.TenantCode, cancellationToken);

        if (tenant.IsFailure)
        {
            LogSignInFailed(_logger, identifier, tenant.Error.Code);
            return Result.Failure<AuthenticationResponse>(tenant.Error);
        }

        Result canSignIn = tenant.Value.EnsureCanSignIn(DateOnly.FromDateTime(now.UtcDateTime));

        if (canSignIn.IsFailure)
        {
            LogSignInFailed(_logger, identifier, canSignIn.Error.Code);
            return Result.Failure<AuthenticationResponse>(canSignIn.Error);
        }

        using IDisposable scope = _tenantContext.BeginScope(tenant.Value.Id);

        User? user = await _context.Users
            .Include(u => u.Roles)
            .Include(u => u.FirmAccess)
            .FirstOrDefaultAsync(
                u => u.UserName == identifier || u.Email == identifier,
                cancellationToken);

        if (user is null)
        {
            // Burn the same time a real verification would, then fail identically.
            _passwordHasher.Verify(request.Password ?? string.Empty, DecoyHash);
            LogSignInFailed(_logger, identifier, "no such user");

            return Result.Failure<AuthenticationResponse>(
                AuthenticationErrors.InvalidCredentials);
        }

        Result canAuthenticate = user.EnsureCanAuthenticate(now);

        if (canAuthenticate.IsFailure)
        {
            LogSignInFailed(_logger, identifier, canAuthenticate.Error.Code);

            // The specific reason is logged but never returned: telling the caller
            // that an account exists and is locked confirms the account exists.
            return Result.Failure<AuthenticationResponse>(
                AuthenticationErrors.InvalidCredentials);
        }

        PasswordVerificationResult verification =
            _passwordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.RecordFailedLogin(now);
            await _context.SaveChangesAsync(cancellationToken);

            LogSignInFailed(_logger, identifier, "bad password");

            return Result.Failure<AuthenticationResponse>(
                AuthenticationErrors.InvalidCredentials);
        }

        // Sign-in is the only moment the plain-text password exists, so it is the
        // only opportunity to re-hash at a raised cost.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPassword(_passwordHasher.Hash(request.Password!), now);
        }

        user.RecordSuccessfulLogin(now);

        AuthenticationResponse response = await IssueTokensAsync(
            user, now, request.UserAgent, request.IpAddress, existingToken: null, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        LogSignInSucceeded(_logger, user.UserName);

        return Result.Success(response);
    }

    /// <inheritdoc />
    public async Task<Result<AuthenticationResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Failure<AuthenticationResponse>(
                AuthenticationErrors.InvalidRefreshToken);
        }

        DateTimeOffset now = _clock.UtcNow;
        string hash = _tokenService.HashRefreshToken(request.RefreshToken);

        RefreshToken? token = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            return Result.Failure<AuthenticationResponse>(
                AuthenticationErrors.InvalidRefreshToken);
        }

        // A revoked token being presented means two parties hold tokens from this
        // family - the legitimate client, which already rotated, and whoever
        // captured the old one. There is no way to tell which is which, so the
        // whole family goes. Forcing one user to sign in again is a far better
        // outcome than leaving an attacker with a live session.
        if (token.IsRevoked)
        {
            await RevokeFamilyAsync(token, now, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            LogTokenReuseDetected(_logger, token.UserId.Value, token.FamilyId);

            return Result.Failure<AuthenticationResponse>(
                AuthenticationErrors.InvalidRefreshToken);
        }

        if (token.EnsureUsable(now).IsFailure)
        {
            return Result.Failure<AuthenticationResponse>(
                AuthenticationErrors.InvalidRefreshToken);
        }

        User? user = await _context.Users
            .Include(u => u.Roles)
            .Include(u => u.FirmAccess)
            .FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);

        // An account disabled since the token was issued must not be able to renew
        // its way past the change.
        if (user is null || user.EnsureCanAuthenticate(now).IsFailure)
        {
            token.Revoke(RefreshTokenRevocationReason.Administrative, now);
            await _context.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthenticationResponse>(
                AuthenticationErrors.InvalidRefreshToken);
        }

        AuthenticationResponse response = await IssueTokensAsync(
            user, now, request.UserAgent, request.IpAddress, token, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(response);
    }

    /// <inheritdoc />
    public async Task<Result> SignOutAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Success();
        }

        string hash = _tokenService.HashRefreshToken(refreshToken);

        RefreshToken? token = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        // Signing out with an unknown token is not an error: the desired state
        // already holds, and reporting a failure would reveal whether a given
        // token ever existed.
        if (token is null)
        {
            return Result.Success();
        }

        // Revoke the whole family, not just this token. "Sign out" means ending the
        // session, and the session is the chain.
        await RevokeFamilyAsync(token, _clock.UtcNow, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ChangePasswordAsync(
        UserId userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow;

        User? user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(AuthenticationErrors.InvalidCredentials);
        }

        if (_passwordHasher.Verify(currentPassword ?? string.Empty, user.PasswordHash)
            == PasswordVerificationResult.Failed)
        {
            return Result.Failure(AuthenticationErrors.CurrentPasswordIncorrect);
        }

        Result policy = PasswordPolicy.Validate(newPassword);

        if (policy.IsFailure)
        {
            return policy;
        }

        Result applied = user.SetPassword(_passwordHasher.Hash(newPassword), now);

        if (applied.IsFailure)
        {
            return applied;
        }

        // Changing a password ends every other session. If the change was prompted
        // by a suspected compromise, leaving other sessions alive would defeat the
        // point of changing it.
        List<RefreshToken> sessions = await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (RefreshToken session in sessions)
        {
            session.Revoke(RefreshTokenRevocationReason.Administrative, now);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _permissionChecker.InvalidateAsync(userId, cancellationToken);

        return Result.Success();
    }

    private async Task<AuthenticationResponse> IssueTokensAsync(
        User user,
        DateTimeOffset now,
        string? userAgent,
        string? ipAddress,
        RefreshToken? existingToken,
        CancellationToken cancellationToken)
    {
        List<string> roleNames = await _context.Roles
            .Where(r => _context.Set<UserRole>()
                .Any(ur => ur.UserId == user.Id && ur.RoleId == r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        AccessToken accessToken = _tokenService.CreateAccessToken(new AccessTokenRequest(
            user.Id,
            user.TenantId,
            user.UserName,
            user.DisplayName,
            user.Email,
            [.. user.FirmAccess.Select(a => a.FirmId).Distinct()],
            [.. user.FirmAccess.Where(a => a.BranchId.HasValue).Select(a => a.BranchId!.Value).Distinct()],
            roleNames,
            user.MustChangePassword));

        (string rawRefresh, string refreshHash) = _tokenService.CreateRefreshToken();
        TimeSpan lifetime = _tokenService.RefreshTokenLifetime;

        if (existingToken is not null)
        {
            RefreshToken successor = existingToken
                .Rotate(refreshHash, now, lifetime, userAgent, ipAddress).Value;

            _context.RefreshTokens.Add(successor);
        }
        else
        {
            RefreshToken issued = RefreshToken.Issue(
                user.TenantId, user.Id, refreshHash, now, lifetime, userAgent, ipAddress).Value;

            _context.RefreshTokens.Add(issued);
        }

        return new AuthenticationResponse(
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            rawRefresh,
            user.MustChangePassword,
            user.DisplayName);
    }

    private async Task RevokeFamilyAsync(
        RefreshToken token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<RefreshToken> family = await _context.RefreshTokens
            .Where(t => t.FamilyId == token.FamilyId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (RefreshToken member in family)
        {
            member.Revoke(RefreshTokenRevocationReason.SuspectedTheft, now);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Sign-in failed for {Identifier}: {Reason}")]
    private static partial void LogSignInFailed(ILogger logger, string identifier, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in succeeded for {UserName}")]
    private static partial void LogSignInSucceeded(ILogger logger, string userName);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Refresh-token reuse detected for user {UserId}; revoking family {FamilyId}")]
    private static partial void LogTokenReuseDetected(
        ILogger logger, Guid userId, Guid familyId);
}
