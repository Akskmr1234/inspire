using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using ERP.Application.Abstractions.Security;
using ERP.Domain.Identity;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Security;

/// <summary>Where the privacyIDEA server is, and how the ERP signs in to it.</summary>
/// <remarks>
/// Off unless <see cref="Enabled"/> is set and the server's address and service
/// account are given: an installation without privacyIDEA signs in with a password
/// alone, exactly as before.
/// </remarks>
public sealed class PrivacyIdeaOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Erp:Mfa:PrivacyIdea";

    /// <summary>Gets or sets a value indicating whether second factors are used.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the server's address as the API reaches it, e.g. http://privacyidea:8080.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the privacyIDEA administrator the ERP acts as.</summary>
    public string AdminUser { get; set; } = string.Empty;

    /// <summary>Gets or sets that administrator's password.</summary>
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the address phones use to reach privacyIDEA's push endpoint, ending in
    /// <c>/ttype/push</c>. It must be reachable from the phone, so normally the public
    /// HTTPS address. Empty turns push off; the other factors still work.
    /// </summary>
    public string PushRegistrationUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether phones check the push address's certificate.</summary>
    public bool PushVerifySsl { get; set; } = true;

    /// <summary>Gets or sets the name authenticator apps and push requests show.</summary>
    public string Issuer { get; set; } = "Inspire ERP";

    /// <summary>Gets or sets how many backup codes are issued at a time.</summary>
    public int BackupCodeCount { get; set; } = 10;

    /// <summary>Gets a value indicating whether everything needed is configured.</summary>
    public bool IsConfigured =>
        Enabled
        && Uri.TryCreate(BaseUrl, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(AdminUser)
        && !string.IsNullOrWhiteSpace(AdminPassword);

    /// <summary>Gets a value indicating whether push can be enrolled.</summary>
    public bool PushConfigured =>
        IsConfigured && Uri.TryCreate(PushRegistrationUrl, UriKind.Absolute, out _);
}

/// <summary>The administrator session the ERP holds with privacyIDEA, shared by every request.</summary>
public sealed class PrivacyIdeaSession : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>Returns a live token, signing in again when the last one is old.</summary>
    /// <param name="signIn">Signs in and returns a fresh token.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The token.</returns>
    internal async Task<Result<string>> TokenAsync(
        Func<Task<Result<string>>> signIn,
        DateTimeOffset now)
    {
        await _gate.WaitAsync();
        try
        {
            if (_token is not null && now < _expiresAt)
            {
                return Result.Success(_token);
            }

            Result<string> fresh = await signIn();

            if (fresh.IsSuccess)
            {
                // privacyIDEA's own tokens last an hour; renewing well before that
                // means a request never carries one that expires on the way.
                _token = fresh.Value;
                _expiresAt = now.AddMinutes(30);
            }

            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the token, after the server refused it.</summary>
    internal void Forget() => _token = null;

    /// <summary>Gets or sets a value indicating whether the push policies are known to be in place.</summary>
    internal bool PoliciesApplied { get; set; }
}

/// <summary>Second factors through a privacyIDEA server.</summary>
/// <remarks>
/// <para>
/// Factors are created without a privacyIDEA user: the ERP keeps who owns each serial,
/// so no user directory has to be kept in step between the two systems. Codes are
/// checked by serial; a push is sent with an administrator's trigger and its answer
/// polled, which is privacyIDEA's "poll only" mode - no Firebase account, no cost.
/// </para>
/// <para>
/// The policies push needs (poll only, where phones register, what the request says)
/// are written to the server the first time push is used, so a fresh privacyIDEA needs
/// no setup beyond its administrator account.
/// </para>
/// </remarks>
public sealed partial class PrivacyIdeaMfaProvider : IMfaProvider
{
    private const string PushPolicy = "inspire-erp-push-enrolment";
    private const string PushTextPolicy = "inspire-erp-push-authentication";

    private readonly HttpClient _http;
    private readonly PrivacyIdeaOptions _options;
    private readonly PrivacyIdeaSession _session;
    private readonly IClock _clock;
    private readonly ILogger<PrivacyIdeaMfaProvider> _logger;

    /// <summary>Initialises a new instance of the <see cref="PrivacyIdeaMfaProvider"/> class.</summary>
    /// <param name="http">The client, with the server's address as its base.</param>
    /// <param name="options">The settings.</param>
    /// <param name="session">The shared administrator session.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">The logger.</param>
    public PrivacyIdeaMfaProvider(
        HttpClient http,
        IOptions<PrivacyIdeaOptions> options,
        PrivacyIdeaSession session,
        IClock clock,
        ILogger<PrivacyIdeaMfaProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options.Value;
        _session = session;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => _options.IsConfigured;

    /// <inheritdoc />
    public async Task<Result<MfaEnrolment>> EnrolAsync(
        MfaFactorKind kind,
        string label,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Result.Failure<MfaEnrolment>(Unavailable());
        }

        Dictionary<string, string> form = new() { ["genkey"] = "1" };

        switch (kind)
        {
            case MfaFactorKind.Authenticator:
                form["type"] = "totp";
                form["tokenlabel"] = string.IsNullOrWhiteSpace(label) ? _options.Issuer : label.Trim();
                form["tokenissuer"] = _options.Issuer;
                break;

            case MfaFactorKind.Push:
                if (!_options.PushConfigured)
                {
                    return Result.Failure<MfaEnrolment>(Error.BusinessRule(
                        "Mfa.PushNotConfigured",
                        "Push approval is not set up on this server. Use an authenticator app instead."));
                }

                Result policies = await EnsurePushPoliciesAsync(cancellationToken);

                if (policies.IsFailure)
                {
                    return Result.Failure<MfaEnrolment>(policies.Error);
                }

                form["type"] = "push";
                break;

            case MfaFactorKind.BackupCodes:
                form["type"] = "tan";
                form["tantoken_count"] = Math.Clamp(_options.BackupCodeCount, 5, 20)
                    .ToString(CultureInfo.InvariantCulture);
                break;

            default:
                return Result.Failure<MfaEnrolment>(Error.Validation(
                    "Mfa.UnknownKind", "That kind of second factor is not supported."));
        }

        form["description"] = $"{_options.Issuer}: {label}".Trim();

        Result<JsonElement> sent = await SendAsync(HttpMethod.Post, "/token/init", form, admin: true, cancellationToken);

        if (sent.IsFailure)
        {
            return Result.Failure<MfaEnrolment>(sent.Error);
        }

        if (!sent.Value.TryGetProperty("detail", out JsonElement detail)
            || detail.GetProperty("serial").GetString() is not { Length: > 0 } serial)
        {
            return Result.Failure<MfaEnrolment>(Failed("returned no serial"));
        }

        return kind switch
        {
            MfaFactorKind.Authenticator => Result.Success(new MfaEnrolment(
                serial, Text(detail, "googleurl", "img"), Text(detail, "googleurl", "value"), null)),
            MfaFactorKind.Push => Result.Success(new MfaEnrolment(
                serial, Text(detail, "pushurl", "img"), Text(detail, "pushurl", "value"), null)),
            _ => Result.Success(new MfaEnrolment(serial, null, null, Codes(detail))),
        };
    }

    /// <inheritdoc />
    public async Task<Result<bool>> IsRegisteredAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        Result<JsonElement> sent = await SendAsync(
            HttpMethod.Get, $"/token/?serial={Uri.EscapeDataString(serial)}", null, admin: true, cancellationToken);

        if (sent.IsFailure)
        {
            return Result.Failure<bool>(sent.Error);
        }

        JsonElement tokens = sent.Value.GetProperty("result").GetProperty("value").GetProperty("tokens");

        if (tokens.GetArrayLength() == 0)
        {
            return Result.Success(false);
        }

        string? state = tokens[0].TryGetProperty("rollout_state", out JsonElement s) ? s.GetString() : null;
        return Result.Success(string.IsNullOrEmpty(state) || state == "enrolled");
    }

    /// <inheritdoc />
    public async Task<Result<bool>> CheckCodeAsync(
        string serial,
        string code,
        CancellationToken cancellationToken = default)
    {
        string digits = new((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

        if (digits.Length is < 6 or > 12)
        {
            return Result.Success(false);
        }

        Result<JsonElement> sent = await SendAsync(
            HttpMethod.Post,
            "/validate/check",
            new Dictionary<string, string> { ["serial"] = serial, ["pass"] = digits },
            admin: false,
            cancellationToken);

        return sent.IsFailure
            ? Result.Failure<bool>(sent.Error)
            : Result.Success(Accepted(sent.Value));
    }

    /// <inheritdoc />
    public async Task<Result<string>> SendPushAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        Result policies = await EnsurePushPoliciesAsync(cancellationToken);

        if (policies.IsFailure)
        {
            return Result.Failure<string>(policies.Error);
        }

        Result<JsonElement> sent = await SendAsync(
            HttpMethod.Post,
            "/validate/triggerchallenge",
            new Dictionary<string, string> { ["serial"] = serial },
            admin: true,
            cancellationToken);

        if (sent.IsFailure)
        {
            return Result.Failure<string>(sent.Error);
        }

        JsonElement detail = sent.Value.GetProperty("detail");
        string? transaction = detail.TryGetProperty("transaction_id", out JsonElement single)
            ? single.GetString()
            : null;

        if (transaction is null
            && detail.TryGetProperty("transaction_ids", out JsonElement many)
            && many.GetArrayLength() > 0)
        {
            transaction = many[0].GetString();
        }

        return transaction is { Length: > 0 }
            ? Result.Success(transaction)
            : Result.Failure<string>(Error.BusinessRule(
                "Mfa.PushNotSent",
                "The phone for this push factor has not finished registering. Remove it and add it again."));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> IsPushApprovedAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        Result<JsonElement> sent = await SendAsync(
            HttpMethod.Get,
            $"/validate/polltransaction?transaction_id={Uri.EscapeDataString(transactionId)}",
            null,
            admin: false,
            cancellationToken);

        return sent.IsFailure
            ? Result.Failure<bool>(sent.Error)
            : Result.Success(Accepted(sent.Value));
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(string serial, CancellationToken cancellationToken = default)
    {
        Result<JsonElement> sent = await SendAsync(
            HttpMethod.Delete, $"/token/{Uri.EscapeDataString(serial)}", null, admin: true, cancellationToken);

        // Gone already is as good as removed.
        return sent.IsSuccess || sent.Error.Code == "Mfa.NotFound" ? Result.Success() : Result.Failure(sent.Error);
    }

    private static bool Accepted(JsonElement body) =>
        body.TryGetProperty("result", out JsonElement result)
        && result.TryGetProperty("value", out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement detail, string name, string field) =>
        detail.TryGetProperty(name, out JsonElement part)
        && part.TryGetProperty(field, out JsonElement value)
            ? value.GetString()
            : null;

    private static List<string> Codes(JsonElement detail)
    {
        List<(int Index, string Code)> codes = [];

        if (detail.TryGetProperty("otps", out JsonElement otps) && otps.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty otp in otps.EnumerateObject())
            {
                if (int.TryParse(otp.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    && otp.Value.GetString() is { } code)
                {
                    codes.Add((index, code));
                }
            }
        }

        return [.. codes.OrderBy(c => c.Index).Select(c => c.Code)];
    }

    private static Error Unavailable() => Error.BusinessRule(
        "Mfa.NotConfigured", "Two-factor sign-in is not set up on this server.");

    private static Error Failed(string what) => Error.Unexpected(
        "Mfa.ServerError", $"The two-factor server {what}. Try again, or ask an administrator.");

    private async Task<Result> EnsurePushPoliciesAsync(CancellationToken cancellationToken)
    {
        if (!_options.PushConfigured)
        {
            return Result.Failure(Error.BusinessRule(
                "Mfa.PushNotConfigured", "Push approval is not set up on this server."));
        }

        if (_session.PoliciesApplied)
        {
            return Result.Success();
        }

        string ssl = _options.PushVerifySsl ? "1" : "0";

        Result<JsonElement> enrolment = await SendAsync(
            HttpMethod.Post,
            $"/policy/{PushPolicy}",
            new Dictionary<string, string>
            {
                ["scope"] = "enrollment",
                ["action"] = $"push_firebase_configuration=poll only, push_registration_url={_options.PushRegistrationUrl}, push_ssl_verify={ssl}",
                ["active"] = "true",
            },
            admin: true,
            cancellationToken);

        if (enrolment.IsFailure)
        {
            return Result.Failure(enrolment.Error);
        }

        Result<JsonElement> authentication = await SendAsync(
            HttpMethod.Post,
            $"/policy/{PushTextPolicy}",
            new Dictionary<string, string>
            {
                ["scope"] = "authentication",
                ["action"] = $"push_title_on_mobile={_options.Issuer}, push_text_on_mobile=Approve sign-in to {_options.Issuer}?, push_allow_polling=allow, push_ssl_verify={ssl}",
                ["active"] = "true",
            },
            admin: true,
            cancellationToken);

        if (authentication.IsFailure)
        {
            return Result.Failure(authentication.Error);
        }

        _session.PoliciesApplied = true;
        return Result.Success();
    }

    private async Task<Result<JsonElement>> SendAsync(
        HttpMethod method,
        string path,
        Dictionary<string, string>? form,
        bool admin,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Result.Failure<JsonElement>(Unavailable());
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpRequestMessage request = new(method, Endpoint(path));

            if (form is not null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }

            if (admin)
            {
                Result<string> token = await _session.TokenAsync(() => SignInAsync(cancellationToken), _clock.UtcNow);

                if (token.IsFailure)
                {
                    return Result.Failure<JsonElement>(token.Error);
                }

                request.Headers.TryAddWithoutValidation("Authorization", token.Value);
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response;

            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                LogUnreachable(_logger, path, exception.Message);
                return Result.Failure<JsonElement>(Failed("could not be reached"));
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogUnreachable(_logger, path, "timed out");
                return Result.Failure<JsonElement>(Failed("did not answer in time"));
            }

            using (response)
            {
                // An administrator session the server no longer accepts: sign in once more.
                if (admin && response.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _session.Forget();
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return Result.Failure<JsonElement>(Error.NotFound("Mfa.NotFound", "No such factor on the two-factor server."));
                }

                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                JsonElement json;

                try
                {
                    json = JsonDocument.Parse(body).RootElement.Clone();
                }
                catch (JsonException)
                {
                    LogBadAnswer(_logger, path, (int)response.StatusCode);
                    return Result.Failure<JsonElement>(Failed("gave an answer that could not be read"));
                }

                bool ok = json.TryGetProperty("result", out JsonElement result)
                    && result.TryGetProperty("status", out JsonElement status)
                    && status.ValueKind == JsonValueKind.True;

                if (!ok)
                {
                    string message = json.TryGetProperty("result", out JsonElement r)
                        && r.TryGetProperty("error", out JsonElement e)
                        && e.TryGetProperty("message", out JsonElement m)
                            ? m.GetString() ?? string.Empty
                            : string.Empty;

                    LogRefused(_logger, path, (int)response.StatusCode, message);
                    return Result.Failure<JsonElement>(Failed("refused the request"));
                }

                return Result.Success(json);
            }
        }

        return Result.Failure<JsonElement>(Failed("refused the ERP's sign-in"));
    }

    private async Task<Result<string>> SignInAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Endpoint("/auth"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = _options.AdminUser,
                ["password"] = _options.AdminPassword,
            }),
        };

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonElement json = JsonDocument.Parse(body).RootElement;

            if (response.IsSuccessStatusCode
                && json.GetProperty("result").GetProperty("value").TryGetProperty("token", out JsonElement token)
                && token.GetString() is { Length: > 0 } value)
            {
                return Result.Success(value);
            }

            LogRefused(_logger, "/auth", (int)response.StatusCode, "administrator sign-in refused");
            return Result.Failure<string>(Failed("refused the ERP's sign-in"));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            LogUnreachable(_logger, "/auth", exception.Message);
            return Result.Failure<string>(Failed("could not be reached"));
        }
    }

    private Uri Endpoint(string path)
    {
        UriBuilder root = new(_options.BaseUrl);

        if (!root.Path.EndsWith('/'))
        {
            root.Path += '/';
        }

        return new Uri(root.Uri, path.TrimStart('/'));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "privacyIDEA {Path} could not be reached: {Reason}")]
    private static partial void LogUnreachable(ILogger logger, string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "privacyIDEA {Path} answered {Status} with an unreadable body")]
    private static partial void LogBadAnswer(ILogger logger, string path, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "privacyIDEA {Path} refused ({Status}): {Message}")]
    private static partial void LogRefused(ILogger logger, string path, int status, string message);
}
