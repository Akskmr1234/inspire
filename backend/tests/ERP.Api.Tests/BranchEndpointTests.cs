using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Branches;
using ERP.Application.Platform.Users;

namespace ERP.Api.Tests;

/// <summary>
/// Tests for the firm's branches and who may work in them, end to end through the real host.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BranchEndpointTests
{
    private const string Branches = "/api/v1/platform/branches";
    private const string Users = "/api/v1/platform/users";

    private readonly ApiFactory _factory;

    public BranchEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_branch_added_is_listed_and_its_maker_may_work_in_it()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        string code = $"BR{Guid.NewGuid():N}"[..8].ToUpperInvariant();

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            Branches,
            new SaveBranchRequest(code, "Downtown", null, "1 Main Street", "5550100", null));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        BranchSummary created = (await response.Content.ReadFromJsonAsync<BranchSummary>())!;

        created.Code.ShouldBe(code);
        created.IsHeadOffice.ShouldBeFalse();
        created.UserCount.ShouldBe(1);

        (await admin.GetFromJsonAsync<List<BranchSummary>>(Branches))!
            .ShouldContain(branch => branch.BranchId == created.BranchId);

        // Without that, a branch nobody could reach would be made to no purpose.
        (await admin.GetFromJsonAsync<List<BranchSummary>>($"{Branches}/mine"))!
            .ShouldContain(branch => branch.BranchId == created.BranchId);
    }

    [Fact]
    public async Task Two_branches_cannot_share_a_code()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        string code = $"DU{Guid.NewGuid():N}"[..8].ToUpperInvariant();

        (await admin.PostAsJsonAsync(Branches, new SaveBranchRequest(code, "First", null, null, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await admin.PostAsJsonAsync(Branches, new SaveBranchRequest(code, "Second", null, null, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_head_office_cannot_be_put_away_but_can_be_renamed()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        BranchSummary head = (await admin.GetFromJsonAsync<List<BranchSummary>>(Branches))!
            .Single(branch => branch.IsHeadOffice);

        HttpResponseMessage inactive = await admin.PutAsJsonAsync(
            $"{Branches}/{head.BranchId}",
            new SaveBranchRequest(head.Code, head.Name, null, null, null, null, IsActive: false));
        inactive.IsSuccessStatusCode.ShouldBeFalse();

        HttpResponseMessage renamed = await admin.PutAsJsonAsync(
            $"{Branches}/{head.BranchId}",
            new SaveBranchRequest(head.Code, head.Name, null, "Head office street", null, null));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        (await renamed.Content.ReadFromJsonAsync<BranchSummary>())!.Address.ShouldBe("Head office street");
    }

    [Fact]
    public async Task A_user_works_only_in_the_branches_chosen_for_them()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];

        BranchSummary head = (await admin.GetFromJsonAsync<List<BranchSummary>>(Branches))!
            .Single(branch => branch.IsHeadOffice);
        HttpResponseMessage added = await admin.PostAsJsonAsync(
            Branches,
            new SaveBranchRequest($"US{suffix}"[..8].ToUpperInvariant(), "Mall", null, null, null, null));
        BranchSummary mall = (await added.Content.ReadFromJsonAsync<BranchSummary>())!;

        Guid role = (await admin.GetFromJsonAsync<List<RoleSummary>>("/api/v1/platform/roles"))![0].RoleId;
        string userName = $"branch{suffix}";
        const string password = "Branch-Clerk-Password-1!";

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            Users,
            new SaveUserRequest(
                userName, $"{userName}@example.test", "Mall clerk", null, password, [role],
                BranchIds: [mall.BranchId]));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        UserSummary user = (await created.Content.ReadFromJsonAsync<UserSummary>())!;
        user.BranchIds.ShouldBe([mall.BranchId]);

        // Signed in, the clerk is offered the mall and nothing else.
        HttpClient clerk = await _factory.CreateAuthenticatedClientAsync(userName, password);
        (await clerk.GetFromJsonAsync<List<BranchSummary>>($"{Branches}/mine"))!
            .Select(branch => branch.BranchId).ShouldBe([mall.BranchId]);

        HttpResponseMessage moved = await admin.PutAsJsonAsync(
            $"{Users}/{user.UserId}",
            new SaveUserRequest(
                userName, $"{userName}@example.test", "Mall clerk", null, null, [role],
                BranchIds: [head.BranchId, mall.BranchId]));
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await moved.Content.ReadFromJsonAsync<UserSummary>())!.BranchIds
            .ShouldBe([head.BranchId, mall.BranchId], ignoreOrder: true);

        // A user in no branch could do nothing at all.
        HttpResponseMessage none = await admin.PutAsJsonAsync(
            $"{Users}/{user.UserId}",
            new SaveUserRequest(
                userName, $"{userName}@example.test", "Mall clerk", null, null, [role], BranchIds: []));
        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Branches_refuse_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        (await client.GetAsync($"{Branches}/mine")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
