namespace ERP.Application.Abstractions.Security;

/// <summary>Hashes and verifies passwords.</summary>
/// <remarks>
/// An abstraction rather than a static helper because the algorithm and its cost
/// parameters must be replaceable. Password hashing has a shelf life: today's
/// sensible iteration count is tomorrow's trivially brute-forced one, and the
/// implementation is expected to be revised without the domain noticing.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Hashes a password for storage.</summary>
    /// <param name="password">The plain-text password.</param>
    /// <returns>
    /// An encoded string carrying the algorithm, its parameters, the salt, and the
    /// hash - everything needed to verify it later, and to recognise it as
    /// outdated when the parameters change.
    /// </returns>
    string Hash(string password);

    /// <summary>Verifies a password against a stored hash.</summary>
    /// <param name="password">The plain-text password supplied at sign-in.</param>
    /// <param name="encodedHash">The stored hash.</param>
    /// <returns>The verification outcome.</returns>
    PasswordVerificationResult Verify(string password, string encodedHash);
}

/// <summary>The outcome of verifying a password.</summary>
public enum PasswordVerificationResult
{
    /// <summary>The password does not match.</summary>
    Failed = 0,

    /// <summary>The password matches.</summary>
    Success = 1,

    /// <summary>
    /// The password matches, but the stored hash uses outdated parameters and
    /// should be rewritten.
    /// </summary>
    /// <remarks>
    /// Sign-in is the only moment the plain-text password is available, so it is
    /// the only opportunity to upgrade the stored hash. Handling this result is
    /// what lets a cost increase roll out across existing accounts instead of
    /// applying only to new ones.
    /// </remarks>
    SuccessRehashNeeded = 2,
}
