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
    private readonly IMfaProvider? _mfa;
    private readonly IMfaTicketProtector? _tickets;

    /// <summary>Initialises a new instance of the <see cref="AuthenticationService"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="passwordHasher">The password hasher.</param>
    /// <param name="tokenService">The token service.</param>
    /// <param name="permissionChecker">The permission checker.</param>
    /// <param name="tenantContext">The ambient tenant scope, established here during sign-in.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="mfa">The second-factor server, where one is configured.</param>
    /// <param name="tickets">Seals the ticket between the two sign-in steps.</param>
    public AuthenticationService(
        ErpDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IPermissionChecker permissionChecker,
        ITenantContext tenantContext,
        IClock clock,
        ILogger<AuthenticationService> logger,
        IMfaProvider? mfa = null,
        IMfaTicketProtector? tickets = null)
    {
        _mfa = mfa;
        _tickets = tickets;
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

        // The password is right. Where the user has enrolled a second factor, sign-in
        // stops here with a ticket rather than tokens, and finishes once the factor
        // passes. A successful login is recorded only then.
        if (await ChallengeAsync(user, now, cancellationToken) is { } challenge)
        {
            await _context.SaveChangesAsync(cancellationToken);

            return Result.Success(new AuthenticationResponse(
                string.Empty, challenge.ExpiresAtUtc, string.Empty,
                user.MustChangePassword, user.DisplayName, challenge));
        }

        user.RecordSuccessfulLogin(now);

        AuthenticationResponse response = await IssueTokensAsync(
            user, now, request.UserAgent, request.IpAddress, existingToken: null, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        LogSignInSucceeded(_logger, user.UserName);

        return Result.Success(response);
    }

    /// <inheritdoc />
    public async Task<Result<AuthenticationResponse>> VerifyMfaCodeAsync(
        MfaCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = _clock.UtcNow;

        if (ReadTicket(request.Ticket, "signin", now) is not [string tenant, string userText]
            || _mfa is not { IsEnabled: true })
        {
            return Result.Failure<AuthenticationResponse>(AuthenticationErrors.InvalidCredentials);
        }

        using IDisposable scope = _tenantContext.BeginScope(TenantId.From(Guid.Parse(tenant)));

        (User? user, MfaFactor? factor) = await LoadForMfaAsync(
            userText, request.FactorId, now, cancellationToken);

        if (user is null || factor is null || factor.Kind == MfaFactorKind.Push)
        {
            return Result.Failure<AuthenticationResponse>(AuthenticationErrors.InvalidCredentials);
        }

        Result<bool> checkedCode = await _mfa.CheckCodeAsync(factor.Serial, request.Code, cancellationToken);

        if (checkedCode.IsFailure)
        {
            return Result.Failure<AuthenticationResponse>(checkedCode.Error);
        }

        if (!checkedCode.Value)
        {
            // A wrong code counts towards the same lockout as a wrong password, so the
            // second factor cannot be guessed at leisure once the password is known.
            user.RecordFailedLogin(now);
            await _context.SaveChangesAsync(cancellationToken);
            LogSignInFailed(_logger, user.UserName, "wrong second-factor code");

            return Result.Failure<AuthenticationResponse>(Error.Unauthorized(
                "Authentication.WrongCode", "That code is not right. Try the next one."));
        }

        return Result.Success(await FinishMfaAsync(
            user, factor, now, request.UserAgent, request.IpAddress, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<MfaPushStarted>> SendMfaPushAsync(
        string ticket,
        Guid factorId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow;

        if (ReadTicket(ticket, "signin", now) is not [string tenant, string userText]
            || _mfa is not { IsEnabled: true }
            || _tickets is null)
        {
            return Result.Failure<MfaPushStarted>(AuthenticationErrors.InvalidCredentials);
        }

        using IDisposable scope = _tenantContext.BeginScope(TenantId.From(Guid.Parse(tenant)));

        (User? user, MfaFactor? factor) = await LoadForMfaAsync(userText, factorId, now, cancellationToken);

        if (user is null || factor is not { Kind: MfaFactorKind.Push })
        {
            return Result.Failure<MfaPushStarted>(AuthenticationErrors.InvalidCredentials);
        }

        Result<string> sent = await _mfa.SendPushAsync(factor.Serial, cancellationToken);

        if (sent.IsFailure)
        {
            return Result.Failure<MfaPushStarted>(sent.Error);
        }

        DateTimeOffset expires = now.AddMinutes(2);

        return Result.Success(new MfaPushStarted(
            _tickets.Protect($"push|{tenant}|{userText}|{factor.Id.Value}|{sent.Value}", expires),
            expires));
    }

    /// <inheritdoc />
    public async Task<Result<AuthenticationResponse?>> PollMfaPushAsync(
        MfaPushPollRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = _clock.UtcNow;

        // Both tickets, and for the same user: the push ticket alone would let anyone
        // who saw it in transit collect the tokens.
        if (ReadTicket(request.Ticket, "signin", now) is not [string tenant, string userText]
            || ReadTicket(request.PushTicket, "push", now) is not [string pushTenant, string pushUser, string factorText, string transaction]
            || pushTenant != tenant
            || pushUser != userText
            || _mfa is not { IsEnabled: true })
        {
            return Result.Failure<AuthenticationResponse?>(AuthenticationErrors.InvalidCredentials);
        }

        Result<bool> approved = await _mfa.IsPushApprovedAsync(transaction, cancellationToken);

        if (approved.IsFailure)
        {
            return Result.Failure<AuthenticationResponse?>(approved.Error);
        }

        if (!approved.Value)
        {
            return Result.Success<AuthenticationResponse?>(null);
        }

        using IDisposable scope = _tenantContext.BeginScope(TenantId.From(Guid.Parse(tenant)));

        (User? user, MfaFactor? factor) = await LoadForMfaAsync(
            userText, Guid.Parse(factorText), now, cancellationToken);

        if (user is null || factor is null)
        {
            return Result.Failure<AuthenticationResponse?>(AuthenticationErrors.InvalidCredentials);
        }

        return Result.Success<AuthenticationResponse?>(await FinishMfaAsync(
            user, factor, now, request.UserAgent, request.IpAddress, cancellationToken));
    }

    /// <summary>
    /// The second-factor step a sign-in must pass, or null when the user has enrolled
    /// none or no second-factor server is configured.
    /// </summary>
    private async Task<MfaChallenge?> ChallengeAsync(
        User user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_mfa is not { IsEnabled: true } || _tickets is null)
        {
            return null;
        }

        List<MfaFactor> factors = await _context.MfaFactors
            .Where(f => f.UserId == user.Id && f.ActivatedAtUtc != null)
            .ToListAsync(cancellationToken);

        if (factors.Count == 0)
        {
            return null;
        }

        DateTimeOffset expires = now.AddMinutes(5);

        return new MfaChallenge(
            _tickets.Protect($"signin|{user.TenantId.Value}|{user.Id.Value}", expires),
            expires,
            [.. factors
                .OrderBy(f => ChoiceOrder(f.Kind))
                .ThenBy(f => f.CreatedAtUtc)
                .Select(f => new MfaFactorChoice(f.Id.Value, f.Kind, f.Label))]);
    }

    /// <summary>Push first - one tap - then the authenticator, backup codes last.</summary>
    private static int ChoiceOrder(MfaFactorKind kind) => kind switch
    {
        MfaFactorKind.Push => 0,
        MfaFactorKind.Authenticator => 1,
        _ => 2,
    };

    /// <summary>Opens a ticket of the given kind and returns its fields after the kind.</summary>
    private string[]? ReadTicket(string? ticket, string kind, DateTimeOffset now)
    {
        string? payload = _tickets?.Unprotect(ticket, now);

        if (payload is null)
        {
            return null;
        }

        string[] parts = payload.Split('|');

        return parts.Length > 1 && parts[0] == kind && parts.Skip(1).All(p => p.Length > 0)
            ? parts[1..]
            : null;
    }

    /// <summary>The user and their active factor, if both still stand and the user may sign in.</summary>
    private async Task<(User? User, MfaFactor? Factor)> LoadForMfaAsync(
        string userText,
        Guid factorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userText, out Guid userGuid))
        {
            return (null, null);
        }

        UserId userId = UserId.From(userGuid);

        User? user = await _context.Users
            .Include(u => u.Roles)
            .Include(u => u.FirmAccess)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || user.EnsureCanAuthenticate(now).IsFailure)
        {
            return (null, null);
        }

        MfaFactorId id = MfaFactorId.From(factorId);

        MfaFactor? factor = await _context.MfaFactors
            .FirstOrDefaultAsync(
                f => f.Id == id && f.UserId == userId && f.ActivatedAtUtc != null,
                cancellationToken);

        return (user, factor);
    }

    /// <summary>Records the sign-in and issues the tokens, once the second factor passed.</summary>
    private async Task<AuthenticationResponse> FinishMfaAsync(
        User user,
        MfaFactor factor,
        DateTimeOffset now,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        factor.RecordUse(now);
        user.RecordSuccessfulLogin(now);

        AuthenticationResponse response = await IssueTokensAsync(
            user, now, userAgent, ipAddress, existingToken: null, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        LogSignInSucceeded(_logger, user.UserName);

        return response;
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

        // Refresh arrives unauthenticated, so nothing has established a tenant,
        // and refresh tokens are tenant-scoped. Without a scope the global query
        // filter compares against default(TenantId) and finds nothing - and
        // row-level security would block the read for the same reason. Sign-in
        // does exactly this before its own lookup.
        Result<Tenant> tenant = await ResolveTenantAsync(request.TenantCode, cancellationToken);

        if (tenant.IsFailure)
        {
            return Result.Failure<AuthenticationResponse>(
                AuthenticationErrors.InvalidRefreshToken);
        }

        using IDisposable scope = _tenantContext.BeginScope(tenant.Value.Id);

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
        string? tenantCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Success();
        }

        // Without a tenant scope the lookup below finds nothing, and sign-out
        // would report success while revoking nothing at all - the worst possible
        // outcome for an endpoint whose entire job is to end a session.
        Result<Tenant> tenant = await ResolveTenantAsync(tenantCode, cancellationToken);

        if (tenant.IsFailure)
        {
            return Result.Success();
        }

        using IDisposable scope = _tenantContext.BeginScope(tenant.Value.Id);

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

        (string rawRefresh, string refreshHash) = _tokenService.CreateRefreshToken();
        TimeSpan lifetime = _tokenService.RefreshTokenLifetime;

        // The refresh token is issued first so the access token can name the sign-in
        // it belongs to.
        RefreshToken refresh = existingToken is not null
            ? existingToken.Rotate(refreshHash, now, lifetime, userAgent, ipAddress).Value
            : RefreshToken.Issue(
                user.TenantId, user.Id, refreshHash, now, lifetime, userAgent, ipAddress).Value;

        _context.RefreshTokens.Add(refresh);

        AccessToken accessToken = _tokenService.CreateAccessToken(new AccessTokenRequest(
            user.Id,
            user.TenantId,
            user.UserName,
            user.DisplayName,
            user.Email,
            [.. user.FirmAccess.Select(a => a.FirmId).Distinct()],
            [.. user.FirmAccess.Where(a => a.BranchId.HasValue).Select(a => a.BranchId!.Value).Distinct()],
            roleNames,
            user.MustChangePassword,
            refresh.FamilyId));

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
