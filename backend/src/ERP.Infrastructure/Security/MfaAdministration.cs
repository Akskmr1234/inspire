using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Identity;
using ERP.Infrastructure.Persistence;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Security;

/// <summary>A user's second factors, kept in step with the second-factor server.</summary>
/// <remarks>
/// Every factor lives in two places: its secret on the server, its owner here. A factor
/// is created on the server first and recorded here after, and removed from the server
/// first and forgotten here after, so neither side is ever left with a factor the other
/// does not know about that could still pass a sign-in.
/// </remarks>
public sealed class MfaAdministration : IMfaAdministration
{
    private readonly ErpDbContext _context;
    private readonly IMfaProvider _mfa;
    private readonly PrivacyIdeaOptions _options;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="MfaAdministration"/> class.</summary>
    /// <param name="context">The database.</param>
    /// <param name="mfa">The second-factor server.</param>
    /// <param name="options">Its settings.</param>
    /// <param name="tenantContext">The ambient tenant.</param>
    /// <param name="clock">The clock.</param>
    public MfaAdministration(
        ErpDbContext context,
        IMfaProvider mfa,
        IOptions<PrivacyIdeaOptions> options,
        ITenantContext tenantContext,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _context = context;
        _mfa = mfa;
        _options = options.Value;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<MfaOverview> ListAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        List<MfaFactor> factors = await _context.MfaFactors
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new MfaOverview(
            _mfa.IsEnabled,
            _mfa.IsEnabled && _options.PushConfigured,
            [.. factors.Select(f => new MfaFactorView(
                f.Id.Value, f.Kind, f.Label, f.IsActive, f.CreatedAtUtc, f.LastUsedAtUtc))]);
    }

    /// <inheritdoc />
    public async Task<Result<MfaEnrolmentStarted>> BeginAsync(
        UserId userId,
        MfaFactorKind kind,
        string? label,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
        {
            return Result.Failure<MfaEnrolmentStarted>(Error.Forbidden(
                "Mfa.NoTenant", "Sign in again to set up two-factor sign-in."));
        }

        User? user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<MfaEnrolmentStarted>(Error.NotFound("User.NotFound", "No such user."));
        }

        // Unfinished set-ups of the same kind, and an earlier set of backup codes, give
        // way to the new one: a half-scanned QR code should not linger as a factor.
        List<MfaFactor> replaced = await _context.MfaFactors
            .Where(f => f.UserId == userId && f.Kind == kind
                && (f.ActivatedAtUtc == null || kind == MfaFactorKind.BackupCodes))
            .ToListAsync(cancellationToken);

        string name = string.IsNullOrWhiteSpace(label)
            ? kind switch
            {
                MfaFactorKind.Authenticator => "Authenticator app",
                MfaFactorKind.Push => "Phone",
                _ => "Backup codes",
            }
            : label.Trim();

        Result<MfaEnrolment> enrolled = await _mfa.EnrolAsync(kind, user.UserName, cancellationToken);

        if (enrolled.IsFailure)
        {
            return Result.Failure<MfaEnrolmentStarted>(enrolled.Error);
        }

        DateTimeOffset now = _clock.UtcNow;
        Result<MfaFactor> factor = MfaFactor.Begin(_tenantContext.TenantId, userId, kind, enrolled.Value.Serial, name, now);

        if (factor.IsFailure)
        {
            await _mfa.RemoveAsync(enrolled.Value.Serial, cancellationToken);
            return Result.Failure<MfaEnrolmentStarted>(factor.Error);
        }

        // Backup codes need no first use to prove they work: they are the codes.
        if (kind == MfaFactorKind.BackupCodes)
        {
            factor.Value.Activate(now);
        }

        foreach (MfaFactor old in replaced)
        {
            await _mfa.RemoveAsync(old.Serial, cancellationToken);
            _context.MfaFactors.Remove(old);
        }

        _context.MfaFactors.Add(factor.Value);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new MfaEnrolmentStarted(
            factor.Value.Id.Value,
            kind,
            enrolled.Value.QrImage,
            enrolled.Value.SetupUrl,
            enrolled.Value.BackupCodes,
            factor.Value.IsActive));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ConfirmAsync(
        UserId userId,
        Guid factorId,
        string? code,
        CancellationToken cancellationToken = default)
    {
        MfaFactor? factor = await FindAsync(userId, factorId, cancellationToken);

        if (factor is null)
        {
            return Result.Failure<bool>(NotFound());
        }

        if (factor.IsActive)
        {
            return Result.Success(true);
        }

        Result<bool> passed = factor.Kind == MfaFactorKind.Push
            ? await _mfa.IsRegisteredAsync(factor.Serial, cancellationToken)
            : await _mfa.CheckCodeAsync(factor.Serial, code ?? string.Empty, cancellationToken);

        if (passed.IsFailure)
        {
            return Result.Failure<bool>(passed.Error);
        }

        if (!passed.Value)
        {
            return factor.Kind == MfaFactorKind.Push
                ? Result.Success(false)
                : Result.Failure<bool>(Error.Validation(
                    "Mfa.WrongCode", "That code is not right. Check the phone's clock and try the next code."));
        }

        factor.Activate(_clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(UserId userId, Guid factorId, CancellationToken cancellationToken = default)
    {
        MfaFactor? factor = await FindAsync(userId, factorId, cancellationToken);

        if (factor is null)
        {
            return Result.Failure(NotFound());
        }

        Result removed = await _mfa.RemoveAsync(factor.Serial, cancellationToken);

        if (removed.IsFailure)
        {
            return removed;
        }

        _context.MfaFactors.Remove(factor);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> ResetAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        List<MfaFactor> factors = await _context.MfaFactors
            .Where(f => f.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (MfaFactor factor in factors)
        {
            Result removed = await _mfa.RemoveAsync(factor.Serial, cancellationToken);

            if (removed.IsFailure)
            {
                return Result.Failure<int>(removed.Error);
            }

            _context.MfaFactors.Remove(factor);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(factors.Count);
    }

    private static Error NotFound() => Error.NotFound("Mfa.FactorNotFound", "No such second factor on your account.");

    private Task<MfaFactor?> FindAsync(UserId userId, Guid factorId, CancellationToken cancellationToken)
    {
        MfaFactorId id = MfaFactorId.From(factorId);

        return _context.MfaFactors.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, cancellationToken);
    }
}
