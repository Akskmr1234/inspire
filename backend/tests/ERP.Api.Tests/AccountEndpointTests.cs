using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Users;

namespace ERP.Api.Tests;

/// <summary>A user's own profile: their details, and where they are signed in.</summary>
/// <remarks>
/// Run as a user made for the test rather than the administrator, so changing a name or
/// signing devices out cannot disturb the other tests sharing the host.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AccountEndpointTests
{
    private const string Account = "/api/v1/auth/account";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="AccountEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public AccountEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_user_reads_and_corrects_their_own_details()
    {
        (string userName, string password, string roleName) = await CreateUserAsync();
        HttpClient me = await _factory.CreateAuthenticatedClientAsync(userName, password);

        MyAccountResponse account = (await me.GetFromJsonAsync<MyAccountResponse>(Account))!;
        account.UserName.ShouldBe(userName);
        account.TenantCode.ShouldBe(ApiFactory.TenantCode);
        account.Roles.Select(role => role.Name).ShouldContain(roleName);
        account.Sessions.Count(session => session.IsCurrent).ShouldBe(1);
        account.LastLoginAtUtc.ShouldNotBeNull();

        HttpResponseMessage saved = await me.PutAsJsonAsync(Account, new
        {
            DisplayName = "  Counter Two  ",
            Email = $"Renamed.{userName}@Example.com",
            MobileNumber = "+974 5555 0102",
        });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        MyAccountResponse updated = (await saved.Content.ReadFromJsonAsync<MyAccountResponse>())!;
        updated.DisplayName.ShouldBe("Counter Two");
        updated.Email.ShouldBe($"renamed.{userName}@example.com");
        updated.MobileNumber.ShouldBe("+974 5555 0102");

        // Somebody else's address is refused by name, and nothing is changed.
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        MyAccountResponse administrator = (await admin.GetFromJsonAsync<MyAccountResponse>(Account))!;

        HttpResponseMessage taken = await me.PutAsJsonAsync(Account, new
        {
            DisplayName = "Counter Two",
            administrator.Email,
            MobileNumber = (string?)null,
        });

        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await me.GetFromJsonAsync<MyAccountResponse>(Account))!.Email
            .ShouldBe($"renamed.{userName}@example.com");

        // A name is required.
        (await me.PutAsJsonAsync(Account, new { DisplayName = " ", updated.Email }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_user_signs_their_other_devices_out()
    {
        (string userName, string password, _) = await CreateUserAsync();
        HttpClient phone = await _factory.CreateAuthenticatedClientAsync(userName, password);
        HttpClient laptop = await _factory.CreateAuthenticatedClientAsync(userName, password);
        HttpClient tablet = await _factory.CreateAuthenticatedClientAsync(userName, password);

        MyAccountResponse seen = (await laptop.GetFromJsonAsync<MyAccountResponse>(Account))!;
        seen.Sessions.Count.ShouldBe(3);
        MySession here = seen.Sessions.Single(session => session.IsCurrent);
        seen.Sessions[0].ShouldBe(here);

        // One device, by its sign-in.
        MySession phoneSession = (await phone.GetFromJsonAsync<MyAccountResponse>(Account))!
            .Sessions.Single(session => session.IsCurrent);

        (await laptop.DeleteAsync($"{Account}/sessions/{phoneSession.SessionId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await laptop.DeleteAsync($"{Account}/sessions/{phoneSession.SessionId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Everything but here.
        HttpResponseMessage others = await laptop.PostAsync($"{Account}/sessions/sign-out-others", null);
        others.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await others.Content.ReadFromJsonAsync<SignedOut>())!.Ended.ShouldBe(1);

        MyAccountResponse after = (await laptop.GetFromJsonAsync<MyAccountResponse>(Account))!;
        after.Sessions.ShouldHaveSingleItem().SessionId.ShouldBe(here.SessionId);

        // Nobody reaches another user's sign-in by guessing its identifier.
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        (await admin.DeleteAsync($"{Account}/sessions/{here.SessionId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _ = tablet;
    }

    private async Task<(string UserName, string Password, string RoleName)> CreateUserAsync()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        List<RoleSummary> roles = (await admin.GetFromJsonAsync<List<RoleSummary>>(
            "/api/v1/platform/roles"))!;
        RoleSummary role = roles[0];

        string tag = Guid.NewGuid().ToString("N")[..8];
        string userName = $"self{tag}";
        string password = $"Self-{tag}-Password!";

        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/platform/users", new
        {
            UserName = userName,
            Email = $"{userName}@example.com",
            DisplayName = "Counter One",
            Password = password,
            RoleIds = new[] { role.RoleId },
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return (userName, password, role.Name);
    }

    private sealed record SignedOut(int Ended);
}
