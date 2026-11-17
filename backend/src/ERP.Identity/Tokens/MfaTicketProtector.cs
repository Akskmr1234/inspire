using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ERP.Application.Abstractions.Security;
using Microsoft.Extensions.Options;

namespace ERP.Identity.Tokens;

/// <summary>Seals sign-in tickets with an HMAC keyed off the JWT signing key.</summary>
/// <remarks>
/// The key is derived for this one purpose (HKDF), so a ticket can never be passed off
/// as an access token or the other way round, and no second secret has to be managed.
/// </remarks>
public sealed class MfaTicketProtector : IMfaTicketProtector
{
    private readonly byte[] _key;

    /// <summary>Initialises a new instance of the <see cref="MfaTicketProtector"/> class.</summary>
    /// <param name="options">The JWT settings, whose signing key this derives from.</param>
    public MfaTicketProtector(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(options.Value.SigningKey),
            32,
            info: Encoding.UTF8.GetBytes("inspire-erp/mfa-ticket/v1"));
    }

    /// <inheritdoc />
    public string Protect(string payload, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string body = Base64Url(Encoding.UTF8.GetBytes(
            $"{expiresAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}|{payload}"));

        return $"{body}.{Base64Url(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body)))}";
    }

    /// <inheritdoc />
    public string? Unprotect(string? ticket, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Split('.') is not [string body, string mac])
        {
            return null;
        }

        byte[] expected = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body));
        byte[]? given = FromBase64Url(mac);

        if (given is null || !CryptographicOperations.FixedTimeEquals(expected, given))
        {
            return null;
        }

        byte[]? raw = FromBase64Url(body);
        string text = raw is null ? string.Empty : Encoding.UTF8.GetString(raw);
        int bar = text.IndexOf('|', StringComparison.Ordinal);

        if (bar <= 0
            || !long.TryParse(text.AsSpan(0, bar), NumberStyles.Integer, CultureInfo.InvariantCulture, out long expires)
            || nowUtc.ToUnixTimeSeconds() >= expires)
        {
            return null;
        }

        return text[(bar + 1)..];
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };

        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
