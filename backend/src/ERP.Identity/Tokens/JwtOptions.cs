using System.ComponentModel.DataAnnotations;

namespace ERP.Identity.Tokens;

/// <summary>Configuration for issuing and validating JWTs.</summary>
/// <remarks>
/// Validated at startup with <c>ValidateOnStart</c>, so a missing or too-short
/// signing key stops the application immediately rather than surfacing as a
/// confusing 401 on the first sign-in attempt.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Gets or sets the token issuer.</summary>
    [Required]
    public string Issuer { get; set; } = "inspire-erp";

    /// <summary>Gets or sets the intended audience.</summary>
    [Required]
    public string Audience { get; set; } = "inspire-erp-api";

    /// <summary>Gets or sets the HMAC signing key.</summary>
    /// <remarks>
    /// Must be at least 32 bytes: HMAC-SHA256 keys shorter than the hash output
    /// weaken the signature, and the underlying library rejects them outright.
    /// Supply it from a secret store or an environment variable, never from a
    /// checked-in appsettings file.
    /// </remarks>
    [Required]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters.")]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how long an access token remains valid. Defaults to 15 minutes.
    /// </summary>
    /// <remarks>
    /// Short by design. An access token cannot be revoked before it expires, so
    /// its lifetime is the window in which a stolen one stays useful. Refresh
    /// tokens cover the gap and <em>can</em> be revoked.
    /// </remarks>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets how long a refresh token remains valid. Defaults to 14 days.
    /// </summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// Gets or sets the permitted clock skew in seconds when validating expiry.
    /// Defaults to 30.
    /// </summary>
    /// <remarks>
    /// The library's default is five minutes, which quietly extends every token's
    /// life by that much. Thirty seconds still absorbs ordinary clock drift
    /// between servers without materially widening the window.
    /// </remarks>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; } = 30;
}
