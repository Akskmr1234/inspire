namespace ERP.Application.Abstractions.Security;

/// <summary>Seals what sign-in carries between its password step and its second-factor step.</summary>
/// <remarks>
/// The browser holds the ticket between the two steps, so it must be impossible to forge
/// or alter, and useless once it has expired. Nothing in it is secret, only unforgeable.
/// </remarks>
public interface IMfaTicketProtector
{
    /// <summary>Seals some text until a moment.</summary>
    /// <param name="payload">What to seal.</param>
    /// <param name="expiresAtUtc">When it stops being accepted.</param>
    /// <returns>The sealed ticket.</returns>
    string Protect(string payload, DateTimeOffset expiresAtUtc);

    /// <summary>Opens a ticket, if it is genuine and not expired.</summary>
    /// <param name="ticket">The ticket.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>What was sealed, or null.</returns>
    string? Unprotect(string? ticket, DateTimeOffset nowUtc);
}
