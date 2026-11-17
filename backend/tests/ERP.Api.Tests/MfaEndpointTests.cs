using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ERP.Application.Abstractions.Security;
using ERP.Application.Platform.Users;
using ERP.Domain.Identity;
using ERP.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Tests;

/// <summary>
/// Two-factor sign-in end to end, against a stand-in for the privacyIDEA server: enrolling
/// each kind of factor, the password step stopping at a ticket, and the second step
/// finishing it.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MfaEndpointTests
{
    private const string Route = "/api/v1/auth/account/mfa";
    private const string GoodCode = "246810";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="MfaEndpointTests"/> class.</summary>
    /// <param name="factory">The shared API host.</param>
    public MfaEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Without_a_second_factor_server_sign_in_is_password_only()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();

        MfaOverviewDto overview = (await admin.GetFromJsonAsync<MfaOverviewDto>(Route))!;

        overview.Available.ShouldBeFalse();
        (await admin.PostAsJsonAsync(Route, new { Kind = (int)MfaFactorKind.Authenticator }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_authenticator_must_prove_itself_then_is_asked_for_at_every_sign_in()
    {
        FakeMfa fake = new();
        WebApplicationFactory<Program> host = WithFake(fake);
        (string userName, string password) = await CreateUserAsync();
        HttpClient user = await SignedInAsync(host, userName, password);

        EnrolmentDto started = await BeginAsync(user, MfaFactorKind.Authenticator, fake);
        started.QrImage.ShouldStartWith("data:image/png");
        started.IsActive.ShouldBeFalse();

        // Until a first code proves the app was set up, sign-in does not ask for it.
        (await SignInAsync(host, userName, password)).Mfa.ShouldBeNull();

        (await user.PostAsJsonAsync($"{Route}/{started.FactorId}/confirm", new { Code = "000000" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await (await user.PostAsJsonAsync($"{Route}/{started.FactorId}/confirm", new { Code = GoodCode }))
            .Content.ReadFromJsonAsync<ConfirmDto>())!.Active.ShouldBeTrue();

        SignInDto first = await SignInAsync(host, userName, password);
        first.AccessToken.ShouldBeEmpty();
        first.Mfa.ShouldNotBeNull();
        first.Mfa.Factors.ShouldHaveSingleItem().Kind.ShouldBe((int)MfaFactorKind.Authenticator);

        HttpClient anonymous = host.CreateClient();
        HttpResponseMessage wrong = await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/verify", new
        {
            first.Mfa.Ticket,
            FactorId = started.FactorId,
            Code = "135790",
        });
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        HttpResponseMessage right = await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/verify", new
        {
            first.Mfa.Ticket,
            FactorId = started.FactorId,
            Code = GoodCode,
        });
        right.StatusCode.ShouldBe(HttpStatusCode.OK, await right.Content.ReadAsStringAsync());
        (await right.Content.ReadFromJsonAsync<SignInDto>())!.AccessToken.ShouldNotBeNullOrEmpty();

        MfaOverviewDto overview = (await user.GetFromJsonAsync<MfaOverviewDto>(Route))!;
        overview.Factors.ShouldHaveSingleItem().LastUsedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_push_signs_in_once_the_phone_approves_it()
    {
        FakeMfa fake = new();
        WebApplicationFactory<Program> host = WithFake(fake);
        (string userName, string password) = await CreateUserAsync();
        HttpClient user = await SignedInAsync(host, userName, password);

        EnrolmentDto push = await BeginAsync(user, MfaFactorKind.Push, fake);
        (await (await user.PostAsJsonAsync($"{Route}/{push.FactorId}/confirm", new { }))
            .Content.ReadFromJsonAsync<ConfirmDto>())!.Active.ShouldBeTrue();

        SignInDto first = await SignInAsync(host, userName, password);
        HttpClient anonymous = host.CreateClient();

        PushDto sent = (await (await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/push", new
        {
            first.Mfa!.Ticket,
            FactorId = push.FactorId,
        })).Content.ReadFromJsonAsync<PushDto>())!;

        HttpResponseMessage waiting = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/mfa/push/poll", new { first.Mfa.Ticket, sent.PushTicket });
        waiting.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        fake.Approve();

        HttpResponseMessage approved = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/mfa/push/poll", new { first.Mfa.Ticket, sent.PushTicket });
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await approved.Content.ReadFromJsonAsync<SignInDto>())!.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_forged_or_altered_ticket_is_refused()
    {
        FakeMfa fake = new();
        WebApplicationFactory<Program> host = WithFake(fake);
        (string userName, string password) = await CreateUserAsync();
        HttpClient user = await SignedInAsync(host, userName, password);

        EnrolmentDto backup = await BeginAsync(user, MfaFactorKind.BackupCodes, fake);
        backup.IsActive.ShouldBeTrue();
        backup.BackupCodes.ShouldNotBeNull().Count.ShouldBe(10);

        SignInDto first = await SignInAsync(host, userName, password);
        string ticket = first.Mfa!.Ticket;
        string altered = string.Concat(ticket.AsSpan(0, ticket.Length - 2), ticket[^2] == 'A' ? "BB" : "AA");

        HttpClient anonymous = host.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/verify", new
        {
            Ticket = altered,
            FactorId = backup.FactorId,
            Code = GoodCode,
        })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/verify", new
        {
            Ticket = "forged.ticket",
            FactorId = backup.FactorId,
            Code = GoodCode,
        })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_administrator_reset_removes_every_factor_from_both_sides()
    {
        FakeMfa fake = new();
        WebApplicationFactory<Program> host = WithFake(fake);
        (string userName, string password) = await CreateUserAsync();
        HttpClient user = await SignedInAsync(host, userName, password);

        EnrolmentDto backup = await BeginAsync(user, MfaFactorKind.BackupCodes, fake);
        (await SignInAsync(host, userName, password)).Mfa.ShouldNotBeNull();

        HttpClient admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await SignInAsync(host, ApiFactory.AdminUserName, ApiFactory.AdminPassword)).AccessToken);

        UserSummary target = (await admin.GetFromJsonAsync<List<UserSummary>>("/api/v1/platform/users"))!
            .Single(u => u.UserName == userName);

        HttpResponseMessage reset = await admin.PostAsync($"/api/v1/platform/users/{target.UserId}/reset-mfa", null);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());

        fake.Removed.ShouldContain(backup.Serial);
        (await SignInAsync(host, userName, password)).AccessToken.ShouldNotBeNullOrEmpty();
    }

    private static async Task<EnrolmentDto> BeginAsync(HttpClient client, MfaFactorKind kind, FakeMfa fake)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(Route, new { Kind = (int)kind });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        EnrolmentDto started = (await response.Content.ReadFromJsonAsync<EnrolmentDto>())!;

        return started with { Serial = fake.LastSerial };
    }

    private static async Task<SignInDto> SignInAsync(
        WebApplicationFactory<Program> host,
        string userName,
        string password)
    {
        HttpResponseMessage response = await host.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login",
            new { userName, password, tenantCode = ApiFactory.TenantCode });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<SignInDto>())!;
    }

    private static async Task<HttpClient> SignedInAsync(
        WebApplicationFactory<Program> host,
        string userName,
        string password)
    {
        HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await SignInAsync(host, userName, password)).AccessToken);

        return client;
    }

    private WebApplicationFactory<Program> WithFake(FakeMfa fake) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IMfaProvider>(fake)));

    private async Task<(string UserName, string Password)> CreateUserAsync()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        List<RoleSummary> roles = (await admin.GetFromJsonAsync<List<RoleSummary>>("/api/v1/platform/roles"))!;

        string tag = Guid.NewGuid().ToString("N")[..8];
        string userName = $"mfa{tag}";
        string password = $"M-{tag}!";

        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/platform/users", new
        {
            UserName = userName,
            Email = $"{userName}@example.com",
            DisplayName = "Two Factor",
            Password = password,
            RoleIds = new[] { roles[0].RoleId },
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return (userName, password);
    }

    private sealed record MfaOverviewDto(bool Available, bool PushAvailable, List<FactorDto> Factors);

    private sealed record FactorDto(Guid FactorId, int Kind, string Label, bool IsActive, DateTimeOffset? LastUsedAtUtc);

    private sealed record EnrolmentDto(
        Guid FactorId,
        int Kind,
        string? QrImage,
        string? SetupUrl,
        List<string>? BackupCodes,
        bool IsActive)
    {
        public string Serial { get; init; } = string.Empty;
    }

    private sealed record ConfirmDto(bool Active);

    private sealed record ChoiceDto(Guid FactorId, int Kind, string Label);

    private sealed record ChallengeDto(string Ticket, DateTimeOffset ExpiresAtUtc, List<ChoiceDto> Factors);

    private sealed record SignInDto(string AccessToken, string RefreshToken, ChallengeDto? Mfa);

    private sealed record PushDto(string PushTicket, DateTimeOffset ExpiresAtUtc);

    /// <summary>Stands in for privacyIDEA: one good code, and a push the test approves.</summary>
    private sealed class FakeMfa : IMfaProvider
    {
        private bool _approved;

        public bool IsEnabled => true;

        public List<string> Removed { get; } = [];

        public string LastSerial { get; private set; } = string.Empty;

        public void Approve() => _approved = true;

        public Task<Result<MfaEnrolment>> EnrolAsync(
            MfaFactorKind kind,
            string label,
            CancellationToken cancellationToken = default)
        {
            string serial = $"FAKE{Guid.NewGuid():N}"[..20];
            LastSerial = serial;

            return Task.FromResult(Result.Success(kind == MfaFactorKind.BackupCodes
                ? new MfaEnrolment(serial, null, null, [.. Enumerable.Range(1, 10).Select(i => $"{i:D6}")])
                : new MfaEnrolment(serial, "data:image/png;base64,AAAA", "otpauth://totp/x", null)));
        }

        public Task<Result<bool>> IsRegisteredAsync(string serial, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(true));

        public Task<Result<bool>> CheckCodeAsync(string serial, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(code == GoodCode));

        public Task<Result<string>> SendPushAsync(string serial, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success("transaction-1"));

        public Task<Result<bool>> IsPushApprovedAsync(string transactionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(_approved));

        public Task<Result> RemoveAsync(string serial, CancellationToken cancellationToken = default)
        {
            Removed.Add(serial);
            return Task.FromResult(Result.Success());
        }
    }
}
