using ERP.Domain.Identity;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tests.Identity;

/// <summary>Tests for <see cref="User"/>.</summary>
public sealed class UserTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- creation

    [Fact]
    public void A_new_user_is_active_and_must_change_its_password()
    {
        // The initial password was chosen by whoever created the account, so it is
        // a handover secret rather than a credential the user owns.
        User user = CreateUser();

        user.IsActive.ShouldBeTrue();
        user.MustChangePassword.ShouldBeTrue();
        user.FailedLoginAttempts.ShouldBe(0);
        user.MfaMethod.ShouldBe(MfaMethod.None);
    }

    [Fact]
    public void The_user_name_and_email_are_normalised_to_lower_case()
    {
        // Sign-in must not depend on how the user capitalised their name today.
        User user = User.Create(
            Tenant, "  AccountsClerk  ", "  Clerk@Example.COM ", "Accounts Clerk", "hash").Value;

        user.UserName.ShouldBe("accountsclerk");
        user.Email.ShouldBe("clerk@example.com");
    }

    [Fact]
    public void The_display_name_falls_back_to_the_user_name()
    {
        User.Create(Tenant, "clerk", "c@example.com", "  ", "hash").Value
            .DisplayName.ShouldBe("clerk");
    }

    [Theory]
    [InlineData("ab", "User.UserNameLength")]
    [InlineData("", "User.UserNameRequired")]
    [InlineData("   ", "User.UserNameRequired")]
    public void An_invalid_user_name_is_rejected(string userName, string expected) =>
        User.Create(Tenant, userName, "a@example.com", "A", "hash").Error.Code.ShouldBe(expected);

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@@example.com")]
    [InlineData("user name@example.com")]
    [InlineData("user@examplecom")]
    [InlineData("")]
    public void An_invalid_email_is_rejected(string email) =>
        User.Create(Tenant, "clerk", email, "Clerk", "hash").Error.Code.ShouldBe("User.EmailInvalid");

    [Fact]
    public void A_user_without_a_password_hash_is_rejected() =>
        User.Create(Tenant, "clerk", "c@example.com", "Clerk", "  ")
            .Error.Code.ShouldBe("User.PasswordRequired");

    // ---------------------------------------------------------------- lockout

    [Fact]
    public void An_account_locks_after_the_configured_number_of_failures()
    {
        User user = CreateUser();

        for (int attempt = 1; attempt < User.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
            user.IsLockedOut(Now).ShouldBeFalse($"attempt {attempt} should not lock the account");
        }

        user.RecordFailedLogin(Now);

        user.IsLockedOut(Now).ShouldBeTrue();
        user.EnsureCanAuthenticate(Now).Error.Code.ShouldBe("User.LockedOut");
    }

    [Fact]
    public void A_lockout_expires_on_its_own()
    {
        User user = CreateUser();

        for (int i = 0; i < User.MaxFailedAttempts; i++)
        {
            user.RecordFailedLogin(Now);
        }

        user.IsLockedOut(Now.Add(User.LockoutDuration).AddSeconds(-1)).ShouldBeTrue();
        user.IsLockedOut(Now.Add(User.LockoutDuration).AddSeconds(1)).ShouldBeFalse();
    }

    [Fact]
    public void A_successful_sign_in_clears_the_failure_count()
    {
        User user = CreateUser();
        user.RecordFailedLogin(Now);
        user.RecordFailedLogin(Now);

        user.RecordSuccessfulLogin(Now);

        user.FailedLoginAttempts.ShouldBe(0);
        user.LockedOutUntilUtc.ShouldBeNull();
        user.LastLoginAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Locking_raises_an_event_for_the_security_log()
    {
        User user = CreateUser();

        for (int i = 0; i < User.MaxFailedAttempts; i++)
        {
            user.RecordFailedLogin(Now);
        }

        user.DomainEvents.OfType<UserLockedOut>().ShouldHaveSingleItem()
            .LockedUntilUtc.ShouldBe(Now.Add(User.LockoutDuration));
    }

    [Fact]
    public void A_disabled_account_cannot_authenticate()
    {
        User user = CreateUser();
        user.Deactivate();

        user.EnsureCanAuthenticate(Now).Error.Code.ShouldBe("User.Inactive");
        user.DomainEvents.OfType<UserDeactivated>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Reactivating_clears_any_lockout()
    {
        User user = CreateUser();

        for (int i = 0; i < User.MaxFailedAttempts; i++)
        {
            user.RecordFailedLogin(Now);
        }

        user.Deactivate();
        user.Activate();

        user.IsLockedOut(Now).ShouldBeFalse();
        user.EnsureCanAuthenticate(Now).IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- passwords

    [Fact]
    public void Setting_a_password_clears_the_lockout_and_the_change_requirement()
    {
        // Someone who has just proved they know the current password should not
        // remain locked out by earlier failed guesses.
        User user = CreateUser();

        for (int i = 0; i < User.MaxFailedAttempts; i++)
        {
            user.RecordFailedLogin(Now);
        }

        user.SetPassword("new-hash", Now).IsSuccess.ShouldBeTrue();

        user.PasswordHash.ShouldBe("new-hash");
        user.MustChangePassword.ShouldBeFalse();
        user.IsLockedOut(Now).ShouldBeFalse();
        user.PasswordChangedAtUtc.ShouldBe(Now);
        user.DomainEvents.OfType<UserPasswordChanged>().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_administrative_reset_forces_a_change_at_next_sign_in()
    {
        User user = CreateUser();
        user.SetPassword("chosen-by-user", Now);

        user.ResetPassword("temporary-hash", Now.AddDays(1));

        user.MustChangePassword.ShouldBeTrue();
        user.PasswordHash.ShouldBe("temporary-hash");
    }

    // ---------------------------------------------------------------- MFA

    [Fact]
    public void An_authenticator_app_requires_a_secret()
    {
        User user = CreateUser();

        user.EnableTotp("  ").Error.Code.ShouldBe("User.TotpSecretRequired");

        user.EnableTotp("JBSWY3DPEHPK3PXP").IsSuccess.ShouldBeTrue();
        user.MfaMethod.ShouldBe(MfaMethod.Totp);
        user.TotpSecret.ShouldBe("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void Sms_codes_require_a_mobile_number()
    {
        User user = CreateUser();

        user.EnableMfa(MfaMethod.Sms).Error.Code.ShouldBe("User.MobileRequiredForSms");

        user.SetContactDetails("Clerk", "+97455512345");
        user.EnableMfa(MfaMethod.Sms).IsSuccess.ShouldBeTrue();
        user.MfaMethod.ShouldBe(MfaMethod.Sms);
    }

    [Fact]
    public void Switching_away_from_an_authenticator_app_discards_the_secret()
    {
        User user = CreateUser();
        user.EnableTotp("JBSWY3DPEHPK3PXP");

        user.EnableMfa(MfaMethod.Email).IsSuccess.ShouldBeTrue();

        user.TotpSecret.ShouldBeNull();
        user.MfaMethod.ShouldBe(MfaMethod.Email);
    }

    [Fact]
    public void Totp_cannot_be_enabled_through_the_generic_method()
    {
        // It needs a secret, which the generic overload has no way to supply.
        CreateUser().EnableMfa(MfaMethod.Totp).Error.Code.ShouldBe("User.UseEnableTotp");
    }

    [Fact]
    public void Disabling_mfa_clears_both_the_method_and_the_secret()
    {
        User user = CreateUser();
        user.EnableTotp("JBSWY3DPEHPK3PXP");

        user.DisableMfa();

        user.MfaMethod.ShouldBe(MfaMethod.None);
        user.TotpSecret.ShouldBeNull();
    }

    // ---------------------------------------------------------------- roles and access

    [Fact]
    public void Roles_are_granted_once_and_removable()
    {
        User user = CreateUser();
        RoleId role = RoleId.NewId();

        user.AssignRole(role);
        user.AssignRole(role);

        user.Roles.Count.ShouldBe(1);

        user.RemoveRole(role);
        user.Roles.ShouldBeEmpty();
    }

    [Fact]
    public void Role_changes_raise_an_event_so_cached_permissions_are_dropped()
    {
        // A revoked permission must stop working promptly, not whenever a cache
        // happens to expire.
        User user = CreateUser();
        RoleId role = RoleId.NewId();

        user.AssignRole(role);
        user.AssignRole(role);
        user.RemoveRole(role);
        user.RemoveRole(role);

        user.DomainEvents.OfType<UserRolesChanged>().Count().ShouldBe(2);
    }

    [Fact]
    public void Firm_access_can_be_granted_firm_wide_or_per_branch()
    {
        User user = CreateUser();
        FirmId firm = FirmId.NewId();
        BranchId branch = BranchId.NewId();

        user.GrantFirmAccess(firm);
        user.GrantFirmAccess(firm, branch);
        user.GrantFirmAccess(firm, branch);

        // Firm-wide and branch-specific are distinct grants; the duplicate is not.
        user.FirmAccess.Count.ShouldBe(2);

        user.RevokeFirmAccess(firm);
        user.FirmAccess.ShouldBeEmpty();
    }

    private static User CreateUser() =>
        User.Create(Tenant, "clerk", "clerk@example.com", "Accounts Clerk", "initial-hash").Value;
}
