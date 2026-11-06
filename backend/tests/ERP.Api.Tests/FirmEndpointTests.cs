using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.Application.Platform.Branches;
using ERP.Application.Platform.Firms;
using ERP.Application.Platform.Users;

namespace ERP.Api.Tests;

/// <summary>Tests for the tenant's firms, end to end through the real host.</summary>
/// <remarks>
/// A firm is only of use once it has books: these check that one added after
/// installation can be switched to and has a chart of accounts to post to.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class FirmEndpointTests
{
    private const string Firms = "/api/v1/platform/firms";

    private readonly ApiFactory _factory;

    public FirmEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_firm_added_has_a_head_office_and_books_its_maker_can_switch_to()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        string code = $"F{Guid.NewGuid():N}"[..8].ToUpperInvariant();

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            Firms, new SaveFirmRequest(code, "Second Trading", null, "QAR", null));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        FirmSummary firm = (await response.Content.ReadFromJsonAsync<FirmSummary>())!;

        firm.Code.ShouldBe(code);
        firm.Branches.ShouldHaveSingleItem().IsHeadOffice.ShouldBeTrue();

        // A new sign-in carries the new firm; the switch header then reaches it.
        HttpClient again = await _factory.CreateAuthenticatedClientAsync();
        (await again.GetFromJsonAsync<List<FirmSummary>>($"{Firms}/mine"))!
            .ShouldContain(mine => mine.FirmId == firm.FirmId);

        again.DefaultRequestHeaders.Add("X-Erp-Firm", firm.FirmId.ToString());
        again.DefaultRequestHeaders.Add("X-Erp-Branch", firm.Branches[0].BranchId.ToString());

        HttpResponseMessage ledgers = await again.GetAsync("/api/v1/accounting/ledgers");
        ledgers.StatusCode.ShouldBe(HttpStatusCode.OK, await ledgers.Content.ReadAsStringAsync());
        using JsonDocument body = JsonDocument.Parse(await ledgers.Content.ReadAsStringAsync());
        JsonElement list = body.RootElement.ValueKind == JsonValueKind.Array
            ? body.RootElement
            : body.RootElement.GetProperty("items");
        list.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Two_firms_cannot_share_a_code()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        string code = $"D{Guid.NewGuid():N}"[..8].ToUpperInvariant();

        (await admin.PostAsJsonAsync(Firms, new SaveFirmRequest(code, "One", null, "QAR", null)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync(Firms, new SaveFirmRequest(code, "Two", null, "QAR", null)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_user_given_only_another_firms_branch_works_only_in_that_firm()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];

        HttpResponseMessage added = await admin.PostAsJsonAsync(
            Firms, new SaveFirmRequest($"U{suffix}"[..8].ToUpperInvariant(), "Other Trading", null, "QAR", null));
        FirmSummary other = (await added.Content.ReadFromJsonAsync<FirmSummary>())!;

        Guid role = (await admin.GetFromJsonAsync<List<RoleSummary>>("/api/v1/platform/roles"))![0].RoleId;
        string userName = $"firm{suffix}";
        const string password = "Firm-Clerk-Password-1!";

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/v1/platform/users",
            new SaveUserRequest(
                userName, $"{userName}@example.test", "Other clerk", null, password, [role],
                BranchIds: [other.Branches[0].BranchId]));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        HttpClient clerk = await _factory.CreateAuthenticatedClientAsync(userName, password);
        (await clerk.GetFromJsonAsync<List<FirmSummary>>($"{Firms}/mine"))!
            .Select(firm => firm.FirmId).ShouldBe([other.FirmId]);
    }

    [Fact]
    public async Task A_branch_is_added_under_the_firm_chosen_not_the_one_in_session()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        FirmSummary other = (await (await admin.PostAsJsonAsync(
                Firms, new SaveFirmRequest($"B{suffix}", "Branch Holder", null, "QAR", null)))
            .Content.ReadFromJsonAsync<FirmSummary>())!;

        HttpResponseMessage added = await admin.PostAsJsonAsync(
            "/api/v1/platform/branches",
            new SaveBranchRequest($"S{suffix}", "Souq", null, null, null, null, FirmId: other.FirmId));
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        BranchSummary souq = (await added.Content.ReadFromJsonAsync<BranchSummary>())!;

        FirmSummary holder = (await admin.GetFromJsonAsync<List<FirmSummary>>(Firms))!
            .Single(firm => firm.FirmId == other.FirmId);
        holder.Branches.Select(branch => branch.BranchId).ShouldContain(souq.BranchId);

        // And it can be corrected from the firm in session, wherever it belongs.
        HttpResponseMessage renamed = await admin.PutAsJsonAsync(
            $"/api/v1/platform/branches/{souq.BranchId}",
            new SaveBranchRequest(souq.Code, "Souq Waqif", null, null, null, null));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
    }
}
