using System.Net;
using System.Net.Http.Json;
using ERP.Application.Platform.Settings;
using ERP.Domain.Tenancy;

namespace ERP.Api.Tests;

/// <summary>The firm's financial years, set up from the settings screen.</summary>
[Collection(ApiCollection.Name)]
public sealed class FinancialYearEndpointTests
{
    private const string Years = "/api/v1/platform/financial-years";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="FinancialYearEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public FinancialYearEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_year_is_added_moved_and_closed_but_never_overlaps_another()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        int start = 2100 + Random.Shared.Next(0, 800);

        HttpResponseMessage created = await client.PostAsJsonAsync(Years, new
        {
            Code = $"{start}",
            StartDate = new DateOnly(start, 1, 1),
            EndDate = new DateOnly(start, 12, 31),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        FinancialYearSummary year = (await created.Content.ReadFromJsonAsync<FinancialYearSummary>())!;
        year.Status.ShouldBe(FinancialYearStatus.Open);

        // Overlapping it by a day is refused.
        (await client.PostAsJsonAsync(Years, new
        {
            Code = $"{start}-B",
            StartDate = new DateOnly(start, 12, 31),
            EndDate = new DateOnly(start + 1, 12, 30),
        })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Moved to an April-March year: nothing has been posted into it, so it may.
        HttpResponseMessage moved = await client.PutAsJsonAsync($"{Years}/{year.FinancialYearId}", new
        {
            Code = $"{start}-{(start + 1) % 100:00}",
            StartDate = new DateOnly(start, 4, 1),
            EndDate = new DateOnly(start + 1, 3, 31),
        });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await moved.Content.ReadFromJsonAsync<FinancialYearSummary>())!.StartDate
            .ShouldBe(new DateOnly(start, 4, 1));

        HttpResponseMessage closed = await client.PostAsJsonAsync(
            $"{Years}/{year.FinancialYearId}/open", new { Open = false });
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        (await closed.Content.ReadFromJsonAsync<FinancialYearSummary>())!.Status
            .ShouldBe(FinancialYearStatus.Closed);

        List<FinancialYearSummary> all = (await client.GetFromJsonAsync<List<FinancialYearSummary>>(Years))!;
        all.ShouldContain(row => row.FinancialYearId == year.FinancialYearId);
    }

    [Fact]
    public async Task The_current_year_cannot_be_moved_off_the_documents_already_posted_into_it()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Something posted today, so today's year has at least one document in it.
        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = today,
            SupplierLedgerId = masters.SupplierId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"FY-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 10m, TaxPercentage = 0m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());

        FinancialYearSummary current = (await client.GetFromJsonAsync<List<FinancialYearSummary>>(Years))!
            .Single(year => year.StartDate <= today && year.EndDate >= today);

        HttpResponseMessage moved = await client.PutAsJsonAsync($"{Years}/{current.FinancialYearId}", new
        {
            current.Code,
            StartDate = today.AddDays(1),
            current.EndDate,
        });
        moved.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await moved.Content.ReadAsStringAsync());
    }
}
