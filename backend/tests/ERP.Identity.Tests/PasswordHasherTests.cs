using ERP.Application.Abstractions.Security;
using ERP.Identity.Passwords;

namespace ERP.Identity.Tests;

/// <summary>Tests for <see cref="Pbkdf2PasswordHasher"/>.</summary>
/// <remarks>
/// A low iteration count is used throughout. These tests are about the
/// hasher's behaviour, not its cost, and running 600,000 iterations dozens of
/// times would make the suite unpleasant to run. The production count is asserted
/// separately as a constant.
/// </remarks>
public sealed class PasswordHasherTests
{
    private const int TestIterations = 1_000;

    private static readonly Pbkdf2PasswordHasher Hasher = new(TestIterations);

    [Fact]
    public void The_default_iteration_count_meets_current_guidance()
    {
        // OWASP's recommendation for PBKDF2-HMAC-SHA256. If this is ever lowered
        // it should be a deliberate, reviewed decision rather than a quiet edit.
        Pbkdf2PasswordHasher.DefaultIterations.ShouldBeGreaterThanOrEqualTo(600_000);
    }

    [Fact]
    public void A_correct_password_verifies()
    {
        string hash = Hasher.Hash("correct horse battery staple");

        Hasher.Verify("correct horse battery staple", hash)
            .ShouldBe(PasswordVerificationResult.Success);
    }

    [Fact]
    public void An_incorrect_password_does_not_verify()
    {
        string hash = Hasher.Hash("correct horse battery staple");

        Hasher.Verify("Correct horse battery staple", hash)
            .ShouldBe(PasswordVerificationResult.Failed);
        Hasher.Verify("wrong", hash).ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // Each hash carries its own random salt. Identical hashes for identical
        // passwords would reveal which users share one, and would make a single
        // precomputed table effective against the whole database.
        string first = Hasher.Hash("same password");
        string second = Hasher.Hash("same password");

        first.ShouldNotBe(second);
        Hasher.Verify("same password", first).ShouldBe(PasswordVerificationResult.Success);
        Hasher.Verify("same password", second).ShouldBe(PasswordVerificationResult.Success);
    }

    [Fact]
    public void The_encoded_hash_carries_its_own_parameters()
    {
        // Self-describing, so raising the cost later does not invalidate existing
        // passwords: an old hash still verifies against its own recorded count.
        string hash = Hasher.Hash("whatever");
        string[] parts = hash.Split('$');

        parts.Length.ShouldBe(4);
        parts[0].ShouldBe("pbkdf2-sha256");
        parts[1].ShouldBe(TestIterations.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_hash_made_with_fewer_iterations_verifies_but_asks_to_be_upgraded()
    {
        // The cost-increase path. Sign-in is the only moment the plain-text
        // password is available, so it is the only chance to rewrite the stored
        // hash at the new cost.
        Pbkdf2PasswordHasher weak = new(1_000);
        Pbkdf2PasswordHasher strong = new(50_000);

        string legacyHash = weak.Hash("unchanged password");

        strong.Verify("unchanged password", legacyHash)
            .ShouldBe(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void A_hash_at_the_current_cost_needs_no_upgrade()
    {
        string hash = Hasher.Hash("current");

        Hasher.Verify("current", hash).ShouldBe(PasswordVerificationResult.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("bcrypt$1000$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$!!!notbase64!!!$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA==")]
    public void A_malformed_stored_hash_fails_rather_than_throwing(string stored)
    {
        // Corrupt stored data must not become an unhandled exception on the
        // sign-in path, where it would be an easy denial-of-service trigger.
        Hasher.Verify("any password", stored).ShouldBe(PasswordVerificationResult.Failed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_password_never_verifies(string? password)
    {
        string hash = Hasher.Hash("a real password");

        Hasher.Verify(password!, hash).ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Hashing_rejects_an_empty_password_outright()
    {
        Should.Throw<ArgumentException>(() => Hasher.Hash(string.Empty));
        Should.Throw<ArgumentException>(() => Hasher.Hash("   "));
    }

    [Fact]
    public void An_absurdly_low_iteration_count_is_rejected_at_construction()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Pbkdf2PasswordHasher(10));
    }

    [Fact]
    public void Unicode_passwords_round_trip()
    {
        // Arabic is a first-class language in this product, and passphrases are
        // not restricted to ASCII.
        const string password = "كلمة المرور الطويلة جدا ١٢٣";

        Hasher.Verify(password, Hasher.Hash(password))
            .ShouldBe(PasswordVerificationResult.Success);
    }
}
