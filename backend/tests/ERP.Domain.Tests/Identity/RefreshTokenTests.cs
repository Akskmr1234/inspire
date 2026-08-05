using ERP.Domain.Identity;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tests.Identity;

/// <summary>Tests for <see cref="RefreshToken"/>.</summary>
public sealed class RefreshTokenTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly UserId User = UserId.NewId();
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    [Fact]
    public void An_issued_token_is_usable_and_starts_a_new_family()
    {
        RefreshToken token = Issue();

        token.EnsureUsable(Now).IsSuccess.ShouldBeTrue();
        token.IsRevoked.ShouldBeFalse();
        token.FamilyId.ShouldNotBe(Guid.Empty);
        token.ExpiresAtUtc.ShouldBe(Now.Add(Lifetime));
    }

    [Fact]
    public void Two_sign_ins_produce_separate_families()
    {
        // Signing in on a phone must not invalidate the desktop session, so each
        // authentication starts its own rotation chain.
        Issue().FamilyId.ShouldNotBe(Issue().FamilyId);
    }

    [Fact]
    public void Rotation_revokes_the_old_token_and_keeps_the_family()
    {
        RefreshToken original = Issue();

        RefreshToken successor = original.Rotate("new-hash", Now.AddHours(1), Lifetime).Value;

        original.IsRevoked.ShouldBeTrue();
        original.RevocationReason.ShouldBe(RefreshTokenRevocationReason.Rotated);
        successor.IsRevoked.ShouldBeFalse();
        successor.FamilyId.ShouldBe(original.FamilyId);
        successor.TokenHash.ShouldBe("new-hash");
    }

    [Fact]
    public void Rotation_never_extends_the_original_expiry()
    {
        // A session has a maximum life however often it is refreshed. Without this
        // a stolen token could be renewed indefinitely and the session would never
        // end.
        RefreshToken original = Issue();
        DateTimeOffset hardExpiry = original.ExpiresAtUtc;

        RefreshToken successor = original
            .Rotate("h2", Now.AddDays(13), Lifetime).Value;

        successor.ExpiresAtUtc.ShouldBe(hardExpiry);
        successor.ExpiresAtUtc.ShouldBeLessThan(Now.AddDays(13).Add(Lifetime));
    }

    [Fact]
    public void Rotation_shortens_the_expiry_when_the_new_lifetime_ends_sooner()
    {
        RefreshToken original = Issue();

        RefreshToken successor = original
            .Rotate("h2", Now.AddHours(1), TimeSpan.FromHours(1)).Value;

        successor.ExpiresAtUtc.ShouldBe(Now.AddHours(2));
    }

    [Fact]
    public void A_token_cannot_be_rotated_twice()
    {
        // The reuse signal. Because every exchange rotates, a second presentation
        // of the same token means two parties hold it.
        RefreshToken original = Issue();
        original.Rotate("h2", Now.AddHours(1), Lifetime);

        Result<RefreshToken> replay = original.Rotate("h3", Now.AddHours(2), Lifetime);

        replay.IsFailure.ShouldBeTrue();
        replay.Error.Code.ShouldBe("RefreshToken.Revoked");
        replay.Error.Kind.ShouldBe(ErrorKind.Unauthorized);
    }

    [Fact]
    public void An_expired_token_cannot_be_used_or_rotated()
    {
        RefreshToken token = Issue();
        DateTimeOffset afterExpiry = Now.Add(Lifetime).AddSeconds(1);

        token.EnsureUsable(afterExpiry).Error.Code.ShouldBe("RefreshToken.Expired");
        token.Rotate("h2", afterExpiry, Lifetime).Error.Code.ShouldBe("RefreshToken.Expired");
    }

    [Fact]
    public void A_token_is_usable_right_up_to_the_instant_it_expires()
    {
        RefreshToken token = Issue();

        token.EnsureUsable(token.ExpiresAtUtc.AddTicks(-1)).IsSuccess.ShouldBeTrue();
        token.EnsureUsable(token.ExpiresAtUtc).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Revoking_records_the_reason_and_is_idempotent()
    {
        RefreshToken token = Issue();

        token.Revoke(RefreshTokenRevocationReason.SignedOut, Now.AddHours(1));
        token.Revoke(RefreshTokenRevocationReason.SuspectedTheft, Now.AddHours(2));

        // The first reason stands - a token is revoked once, and overwriting the
        // reason would lose why.
        token.RevocationReason.ShouldBe(RefreshTokenRevocationReason.SignedOut);
        token.RevokedAtUtc.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Device_details_carry_forward_through_rotation()
    {
        // Feeds the "signed-in devices" screen, which must keep identifying a
        // session across refreshes rather than losing track at the first rotation.
        RefreshToken original = RefreshToken.Issue(
            Tenant, User, "hash", Now, Lifetime, "Chrome/Windows", "203.0.113.7").Value;

        RefreshToken successor = original.Rotate("h2", Now.AddHours(1), Lifetime).Value;

        successor.UserAgent.ShouldBe("Chrome/Windows");
        successor.IpAddress.ShouldBe("203.0.113.7");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_token_without_a_hash_is_rejected(string hash) =>
        RefreshToken.Issue(Tenant, User, hash, Now, Lifetime)
            .Error.Code.ShouldBe("RefreshToken.HashRequired");

    [Fact]
    public void A_non_positive_lifetime_is_rejected()
    {
        RefreshToken.Issue(Tenant, User, "hash", Now, TimeSpan.Zero)
            .Error.Code.ShouldBe("RefreshToken.InvalidLifetime");

        RefreshToken.Issue(Tenant, User, "hash", Now, TimeSpan.FromDays(-1))
            .Error.Code.ShouldBe("RefreshToken.InvalidLifetime");
    }

    private static RefreshToken Issue() =>
        RefreshToken.Issue(Tenant, User, "hash", Now, Lifetime).Value;
}
