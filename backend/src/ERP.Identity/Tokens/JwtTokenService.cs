using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ERP.Application.Abstractions.Security;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Tenancy;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ERP.Identity.Tokens;

/// <summary>Issues JWT access tokens and opaque refresh tokens.</summary>
public sealed class JwtTokenService : ITokenService
{
    /// <summary>The claim carrying the tenant, read by the tenant-resolution middleware.</summary>
    public const string TenantClaim = "tenant_id";

    /// <summary>The claim carrying a firm the user may work in. May appear several times.</summary>
    public const string FirmClaim = "firm_id";

    /// <summary>The claim carrying a branch the user may work in. May appear several times.</summary>
    public const string BranchClaim = "branch_id";

    /// <summary>The claim flagging that a password change is outstanding.</summary>
    public const string MustChangePasswordClaim = "must_change_password";

    private static readonly JsonWebTokenHandler Handler = new();

    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _credentials;

    /// <summary>Initialises a new instance of the <see cref="JwtTokenService"/> class.</summary>
    /// <param name="options">The JWT configuration.</param>
    /// <param name="clock">The clock.</param>
    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _clock = clock;

        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public AccessToken CreateAccessToken(AccessTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset issuedAt = _clock.UtcNow;
        DateTimeOffset expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, request.UserId.Value.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(JwtRegisteredClaimNames.UniqueName, request.UserName),
            new(JwtRegisteredClaimNames.Email, request.Email),
            new(ClaimTypes.NameIdentifier, request.UserId.Value.ToString()),
            new(ClaimTypes.Name, request.DisplayName),
            new(TenantClaim, request.TenantId.Value.ToString()),
            new(MustChangePasswordClaim, request.MustChangePassword ? "true" : "false"),
        ];

        // Firms and branches are carried as repeated claims. They are small,
        // bounded sets, and having them in the token lets the tenant middleware
        // validate a firm-switch header without a database round trip on every
        // request.
        foreach (FirmId firmId in request.FirmIds)
        {
            claims.Add(new Claim(FirmClaim, firmId.Value.ToString()));
        }

        foreach (BranchId branchId in request.BranchIds)
        {
            claims.Add(new Claim(BranchClaim, branchId.Value.ToString()));
        }

        // Roles travel in the token; individual permissions deliberately do not.
        // A role set is a handful of short strings, whereas a permission set runs
        // to hundreds and must be revocable before the token expires.
        foreach (string role in request.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials,
        };

        return new AccessToken(Handler.CreateToken(descriptor), expiresAt);
    }

    /// <inheritdoc />
    public (string Token, string Hash) CreateRefreshToken()
    {
        // 256 bits from a cryptographic RNG. The token is a bearer credential with
        // no structure to verify, so its only defence is being infeasible to guess.
        byte[] raw = RandomNumberGenerator.GetBytes(32);
        string token = Base64UrlEncoder.Encode(raw);

        return (token, HashRefreshToken(token));
    }

    /// <inheritdoc />
    public string HashRefreshToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        // A single SHA-256 pass, not a slow KDF. This is a 256-bit random value,
        // not a human-chosen password: there is no dictionary to run against it, so
        // the iteration count that protects a password buys nothing here and would
        // cost a hash on every refresh.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return Convert.ToBase64String(hash);
    }
}
