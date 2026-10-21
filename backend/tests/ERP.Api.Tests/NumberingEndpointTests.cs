using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Numbering;
using ERP.Application.Purchase;

namespace ERP.Api.Tests;

/// <summary>Tests for document numbering, end to end through the real host.</summary>
/// <remarks>
/// The entry screens used to invent their own numbers - "INV-1001" above a series that
/// would issue "SL/2026/0007" - and the settings screen offered prefixes the server never
/// saw. What these pin is that the number a screen is shown is the one the document gets,
/// and that a format set through the API is the one the next document carries.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class NumberingEndpointTests
{
    private const string Numbering = "/api/v1/numbering";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public NumberingEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Every_document_a_user_enters_is_listed_with_its_next_number()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        List<DocumentNumberingView> rows =
            (await client.GetFromJsonAsync<List<DocumentNumberingView>>(Numbering))!;

        rows.Select(row => row.DocumentType).ShouldContain("sales.invoice");
        rows.Select(row => row.DocumentType).ShouldContain("purchase.return");
        rows.Count.ShouldBe(12);

        foreach (DocumentNumberingView row in rows)
        {
            row.Next.ShouldStartWith(row.Prefix ?? string.Empty);
        }
    }

    [Fact]
    public async Task A_format_set_here_is_the_one_the_next_document_carries()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        string prefix = $"POX{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";

        HttpResponseMessage set = await client.PutAsJsonAsync(
            $"{Numbering}/purchase.order",
            new { Prefix = prefix, Separator = "-", IncludeYear = false });

        set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());

        DocumentNumberingView peeked = (await client.GetFromJsonAsync<DocumentNumberingView>(
            $"{Numbering}/purchase.order/next?date={Today:yyyy-MM-dd}"))!;

        peeked.Next.ShouldStartWith($"{prefix}-");

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/v1/purchase/orders",
            new
            {
                Date = Today,
                SupplierLedgerId = masters.SupplierId,
                WarehouseId = masters.WarehouseId,
                Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 10m, TaxPercentage = 0m } },
            });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        // The number previewed is the number issued. Peeking reserved nothing, so
        // nothing else took it in between.
        (await created.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!.Number
            .ShouldBe(peeked.Next);

        // Back to the default, so the other order tests see the format they expect.
        await client.PutAsJsonAsync(
            $"{Numbering}/purchase.order", new { Prefix = "PO", Separator = "/", IncludeYear = true });
    }

    [Fact]
    public async Task The_counter_moves_forward_and_never_back()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        DocumentNumberingView before = (await client.GetFromJsonAsync<DocumentNumberingView>(
            $"{Numbering}/sales.order/next"))!;

        HttpResponseMessage forward = await client.PutAsJsonAsync(
            $"{Numbering}/sales.order",
            new { before.Prefix, Separator = before.Separator, before.IncludeYear, NextNumber = before.NextNumber + 50 });

        forward.StatusCode.ShouldBe(HttpStatusCode.OK, await forward.Content.ReadAsStringAsync());

        // Back would reissue numbers documents already carry.
        HttpResponseMessage back = await client.PutAsJsonAsync(
            $"{Numbering}/sales.order",
            new { before.Prefix, Separator = before.Separator, before.IncludeYear, NextNumber = before.NextNumber });

        back.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_document_type_that_is_not_numbered_here_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync($"{Numbering}/nonsense.type/next");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
