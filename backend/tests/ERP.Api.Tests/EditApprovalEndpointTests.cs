using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Approvals;
using ERP.Application.Platform.Settings;
using ERP.Application.Platform.Users;
using ERP.Application.Sales;
using ERP.Domain.Inventory;
using ERP.Domain.Platform;
using ERP.Domain.Sales;

namespace ERP.Api.Tests;

/// <summary>
/// Where the firm requires it, a change to a posted document by somebody who may not approve
/// it waits for somebody who may, and is made only then.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EditApprovalEndpointTests
{
    private const string Policies = "/api/v1/settings/policies";
    private const string Invoices = "/api/v1/sales/invoices";
    private const string Requests = "/api/v1/platform/edit-requests";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly string[] ClerkPermissions =
    [
        "sales:invoice:view", "sales:invoice:create", "sales:invoice:edit", "sales:invoice:delete",
    ];

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="EditApprovalEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public EditApprovalEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_correction_by_a_clerk_waits_for_an_approver_and_is_made_when_approved()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(admin);
        await ReceiveAsync(admin, masters);
        HttpClient clerk = await ClerkAsync(admin);

        try
        {
            await SetAsync(admin, FirmPolicies.Default with { EditsRequireApproval = true });

            Guid sale = await SellAsync(clerk, masters, 2m);

            // The clerk's correction is held, and the invoice stands as it was.
            HttpResponseMessage held = await clerk.PutAsJsonAsync($"{Invoices}/{sale}", Edit(masters, 3m));
            held.StatusCode.ShouldBe(HttpStatusCode.Accepted, await held.Content.ReadAsStringAsync());
            EditRequestSummary request = (await held.Content.ReadFromJsonAsync<EditRequestSummary>())!;
            request.Status.ShouldBe(EditRequestStatus.Pending);
            request.Action.ShouldBe(EditAction.Edit);
            (await TotalAsync(admin, sale)).ShouldBe(30m);

            // One change at a time.
            (await clerk.PutAsJsonAsync($"{Invoices}/{sale}", Edit(masters, 4m)))
                .StatusCode.ShouldBe(HttpStatusCode.Conflict);

            // The clerk sees their own request but may not decide it.
            EditRequestSummary own = (await clerk.GetFromJsonAsync<List<EditRequestSummary>>(Requests))!
                .Single(row => row.Id == request.Id);
            own.CanDecide.ShouldBeFalse();
            own.IsOwn.ShouldBeTrue();
            (await clerk.PostAsJsonAsync($"{Requests}/{request.Id}/approve", new { }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // The approver sees it, approves it, and the correction is made as it was asked for.
            (await admin.GetFromJsonAsync<List<EditRequestSummary>>($"{Requests}?status=1"))!
                .Single(row => row.Id == request.Id).CanDecide.ShouldBeTrue();
            HttpResponseMessage approved = await admin.PostAsJsonAsync(
                $"{Requests}/{request.Id}/approve", new { Note = "Checked against the delivery" });
            approved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await approved.Content.ReadAsStringAsync());
            (await TotalAsync(admin, sale)).ShouldBe(45m);

            EditRequestDetail detail = (await admin.GetFromJsonAsync<EditRequestDetail>($"{Requests}/{request.Id}"))!;
            detail.Summary.Status.ShouldBe(EditRequestStatus.Approved);
            detail.Summary.DecisionNote.ShouldBe("Checked against the delivery");

            // A cancellation is held the same way, and refused it leaves the invoice posted.
            HttpResponseMessage cancel = await clerk.PostAsJsonAsync(
                $"{Invoices}/{sale}/cancel", new { Reason = "Customer changed their mind" });
            cancel.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancel.Content.ReadAsStringAsync());
            EditRequestSummary cancellation = (await cancel.Content.ReadFromJsonAsync<EditRequestSummary>())!;

            (await admin.PostAsJsonAsync($"{Requests}/{cancellation.Id}/reject", new { }))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await admin.PostAsJsonAsync($"{Requests}/{cancellation.Id}/reject", new { Note = "Goods have left" }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            SalesInvoiceDetail invoice = (await admin.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{sale}"))!;
            invoice.Header.Status.ShouldBe(SalesInvoiceStatus.Posted);

            // The approver's own correction needs nobody's approval.
            (await admin.PutAsJsonAsync($"{Invoices}/{sale}", Edit(masters, 1m)))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            (await TotalAsync(admin, sale)).ShouldBe(15m);
        }
        finally
        {
            await SetAsync(admin, FirmPolicies.Default);
        }
    }

    [Fact]
    public async Task Without_the_setting_or_before_posting_a_correction_is_made_at_once()
    {
        HttpClient admin = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(admin);
        await ReceiveAsync(admin, masters);
        HttpClient clerk = await ClerkAsync(admin);

        Guid sale = await SellAsync(clerk, masters, 2m);

        (await clerk.PutAsJsonAsync($"{Invoices}/{sale}", Edit(masters, 3m)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TotalAsync(admin, sale)).ShouldBe(45m);

        try
        {
            await SetAsync(admin, FirmPolicies.Default with { EditsRequireApproval = true });

            // A draft has moved nothing yet, so there is nothing to approve.
            HttpResponseMessage drafted = await clerk.PostAsJsonAsync(Invoices, Sale(masters, 1m, post: false));
            drafted.StatusCode.ShouldBe(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync());
            Guid draft = (await drafted.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;

            (await clerk.PutAsJsonAsync($"{Invoices}/{draft}", Edit(masters, 2m)))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await SetAsync(admin, FirmPolicies.Default);
        }
    }

    private static object Sale(TestMasters masters, decimal quantity, bool post = true) => new
    {
        Date = Today,
        CustomerLedgerId = masters.CustomerId,
        masters.WarehouseId,
        Kind = 1,
        Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = 15m, TaxPercentage = 0m } },
        Post = post,
    };

    private static object Edit(TestMasters masters, decimal quantity) => new
    {
        Date = Today,
        CustomerLedgerId = masters.CustomerId,
        masters.WarehouseId,
        Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = 15m, TaxPercentage = 0m } },
        Reason = "Quantity keyed wrongly",
    };

    private static async Task<Guid> SellAsync(HttpClient client, TestMasters masters, decimal quantity)
    {
        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, Sale(masters, quantity));
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());

        return (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;
    }

    private static async Task<decimal> TotalAsync(HttpClient client, Guid sale) =>
        (await client.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{sale}"))!.Header.Total;

    private static async Task SetAsync(HttpClient client, FirmPolicies policies)
    {
        HttpResponseMessage saved = await client.PutAsJsonAsync(Policies, policies);
        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
    }

    private static async Task ReceiveAsync(HttpClient client, TestMasters masters)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/inventory/stock/documents", new
        {
            Type = StockDocumentType.MaterialReceipt,
            Date = Today,
            masters.WarehouseId,
            Lines = new[] { new { masters.ProductId, Quantity = 20m, Rate = 10m } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A user who may enter, correct and cancel sales, but not approve changes to them.</summary>
    private async Task<HttpClient> ClerkAsync(HttpClient admin)
    {
        string tag = Guid.NewGuid().ToString("N")[..8];

        HttpResponseMessage role = await admin.PostAsJsonAsync("/api/v1/platform/roles", new
        {
            Name = $"Counter {tag}",
            Description = "Sales without approval",
            PermissionCodes = ClerkPermissions,
            MenuCodes = Array.Empty<string>(),
        });
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync());
        Guid roleId = (await role.Content.ReadFromJsonAsync<RoleSummary>())!.RoleId;

        string userName = $"clerk{tag}";
        string password = $"Clerk-{tag}-Password!";

        HttpResponseMessage user = await admin.PostAsJsonAsync("/api/v1/platform/users", new
        {
            UserName = userName,
            Email = $"{userName}@example.com",
            DisplayName = "Counter Clerk",
            Password = password,
            RoleIds = new[] { roleId },
        });
        user.StatusCode.ShouldBe(HttpStatusCode.Created, await user.Content.ReadAsStringAsync());

        return await _factory.CreateAuthenticatedClientAsync(userName, password);
    }
}
