using ERP.Identity.Tokens;
using Microsoft.Extensions.Options;

namespace ERP.Identity.Tests;

/// <summary>Tests for <see cref="MfaTicketProtector"/>.</summary>
public sealed class MfaTicketProtectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_ticket_opens_to_what_was_sealed_until_it_expires()
    {
        MfaTicketProtector protector = Protector("a-signing-key-of-at-least-thirty-two-chars");
        string ticket = protector.Protect("signin|tenant|user", Now.AddMinutes(5));

        protector.Unprotect(ticket, Now).ShouldBe("signin|tenant|user");
        protector.Unprotect(ticket, Now.AddMinutes(5)).ShouldBeNull();
    }

    [Fact]
    public void An_altered_or_foreign_ticket_does_not_open()
    {
        MfaTicketProtector protector = Protector("a-signing-key-of-at-least-thirty-two-chars");
        string ticket = protector.Protect("signin|tenant|user", Now.AddMinutes(5));
        string[] parts = ticket.Split('.');

        string swapped = protector.Protect("signin|tenant|someone-else", Now.AddMinutes(5)).Split('.')[0];

        protector.Unprotect($"{swapped}.{parts[1]}", Now).ShouldBeNull();
        protector.Unprotect(ticket + "x", Now).ShouldBeNull();
        protector.Unprotect("not-a-ticket", Now).ShouldBeNull();
        protector.Unprotect(null, Now).ShouldBeNull();

        // Sealed under another installation's key.
        Protector("another-signing-key-of-at-least-thirty-two")
            .Unprotect(ticket, Now).ShouldBeNull();
    }

    private static MfaTicketProtector Protector(string key) =>
        new(Options.Create(new JwtOptions { SigningKey = key }));
}
