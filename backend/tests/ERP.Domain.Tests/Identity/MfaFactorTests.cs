using ERP.Domain.Identity;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tests.Identity;

/// <summary>Tests for <see cref="MfaFactor"/>.</summary>
public sealed class MfaFactorTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly UserId User = UserId.NewId();
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_factor_is_not_asked_for_until_its_set_up_is_finished()
    {
        MfaFactor factor = MfaFactor.Begin(Tenant, User, MfaFactorKind.Authenticator, " TOTP0001 ", " Work phone ", Now).Value;

        factor.IsActive.ShouldBeFalse();
        factor.Serial.ShouldBe("TOTP0001");
        factor.Label.ShouldBe("Work phone");

        factor.Activate(Now.AddMinutes(1));
        factor.Activate(Now.AddMinutes(5));

        factor.IsActive.ShouldBeTrue();
        factor.ActivatedAtUtc.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void A_factor_needs_a_serial_and_a_known_kind()
    {
        MfaFactor.Begin(Tenant, User, MfaFactorKind.Push, " ", "Phone", Now)
            .Error.Code.ShouldBe("Mfa.SerialRequired");
        MfaFactor.Begin(Tenant, User, (MfaFactorKind)9, "S1", "Phone", Now)
            .Error.Code.ShouldBe("Mfa.UnknownKind");
        MfaFactor.Begin(Tenant, User, MfaFactorKind.Push, "S1", new string('x', 101), Now)
            .Error.Code.ShouldBe("Mfa.LabelTooLong");
    }

    [Fact]
    public void Each_use_is_recorded()
    {
        Result<MfaFactor> factor = MfaFactor.Begin(Tenant, User, MfaFactorKind.BackupCodes, "PITN1", "Backup codes", Now);

        factor.Value.RecordUse(Now.AddDays(2));

        factor.Value.LastUsedAtUtc.ShouldBe(Now.AddDays(2));
    }
}
