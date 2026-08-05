using System.Globalization;
using System.Security.Cryptography;
using ERP.Application.Abstractions.Security;

namespace ERP.Identity.Passwords;

/// <summary>
/// Hashes passwords with PBKDF2-HMAC-SHA256.
/// </summary>
/// <remarks>
/// <para>
/// Argon2id would be the first choice on cryptographic merit, but every .NET
/// implementation of it is a third-party package, and this codebase has already
/// been bitten once by a dependency that relicensed and then went unpatched with
/// a known advisory. PBKDF2 is in the framework, is FIPS-approved, and remains an
/// OWASP-sanctioned choice when the iteration count is high enough. That trade -
/// a somewhat weaker KDF with no supply-chain exposure - is the right one here.
/// </para>
/// <para>
/// The encoded form carries its own parameters:
/// </para>
/// <code>pbkdf2-sha256$600000$&lt;base64 salt&gt;$&lt;base64 hash&gt;</code>
/// <para>
/// Self-describing so the cost can be raised later without invalidating existing
/// passwords: an old hash still verifies against its own recorded iteration
/// count, and <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> tells
/// the caller to rewrite it at the one moment the plain-text password is
/// available.
/// </para>
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>The OWASP-recommended iteration count for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    private const string AlgorithmLabel = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private readonly int _iterations;

    /// <summary>Initialises a new instance of the <see cref="Pbkdf2PasswordHasher"/> class.</summary>
    /// <param name="iterations">
    /// The iteration count. Lower values are permitted so tests do not pay the
    /// full cost hundreds of times; production uses the default.
    /// </param>
    public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iterations), iterations, "Iteration count must be at least 1,000.");
        }

        _iterations = iterations;
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);

        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, _iterations, HashAlgorithmName.SHA256, HashBytes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{AlgorithmLabel}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <inheritdoc />
    public PasswordVerificationResult Verify(string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return PasswordVerificationResult.Failed;
        }

        string[] parts = encodedHash.Split('$');

        // A malformed or unrecognised hash is a verification failure, not an
        // exception. Corrupt stored data must not become an unhandled error on the
        // sign-in path, where it would be an easy denial-of-service trigger.
        if (parts.Length != 4
            || !string.Equals(parts[0], AlgorithmLabel, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations)
            || iterations < 1)
        {
            return PasswordVerificationResult.Failed;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Fixed-time comparison. A byte-by-byte equality check leaks, through its
        // own duration, how much of the hash matched - enough to reconstruct it
        // one byte at a time given sufficient attempts.
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerificationResult.Failed;
        }

        return iterations < _iterations
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }
}
