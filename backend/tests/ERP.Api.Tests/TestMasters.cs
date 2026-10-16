using System.Net;
using System.Net.Http.Json;
using ERP.Application.Purchase;
using ERP.Application.Sales;

namespace ERP.Api.Tests;

/// <summary>The masters a document needs, made fresh for one test.</summary>
/// <param name="CustomerId">A customer.</param>
/// <param name="SupplierId">A supplier.</param>
/// <param name="WarehouseId">A warehouse.</param>
/// <param name="ProductId">A product.</param>
/// <param name="ProductCode">The product's code.</param>
/// <param name="UnitId">The product's stock unit.</param>
internal sealed record TestMasters(
    Guid CustomerId,
    Guid SupplierId,
    Guid WarehouseId,
    Guid ProductId,
    string ProductCode,
    Guid UnitId)
{
    private const string Inventory = "/api/v1/inventory";

    /// <summary>Creates a customer, a supplier, a warehouse and a product, all uniquely named.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="serialised">Whether the product is tracked by serial number.</param>
    /// <param name="billWise">Whether receipts from the customer are set against bills.</param>
    /// <returns>The masters.</returns>
    internal static async Task<TestMasters> ArrangeAsync(
        HttpClient client, bool serialised = false, bool billWise = false)
    {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid categoryId = await CreateAsync(
            client, "categories", new { Code = $"TCAT{suffix}", Name = $"Test {suffix}" });
        Guid unitId = await CreateAsync(
            client, "units", new { Code = $"TEA{suffix}", Name = "Each" });
        Guid warehouseId = await CreateAsync(
            client, "warehouses", new { Code = $"TWH{suffix}", Name = $"Store {suffix}" });

        string code = $"TPR{suffix}";

        HttpResponseMessage product = await client.PostAsJsonAsync(
            $"{Inventory}/products",
            new { Code = code, Description = "A test product", CategoryId = categoryId, StockUnitId = unitId, ItemType = 1 });

        product.StatusCode.ShouldBe(HttpStatusCode.Created, await product.Content.ReadAsStringAsync());
        Guid productId = await product.Content.ReadFromJsonAsync<Guid>();

        if (serialised)
        {
            HttpResponseMessage tracked = await client.PutAsJsonAsync(
                $"{Inventory}/products/{productId}/stocking",
                new { PurchaseUnitId = unitId, SalesUnitId = unitId, TracksSerialNumbers = true });

            tracked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await tracked.Content.ReadAsStringAsync());
        }

        HttpResponseMessage supplier = await client.PostAsJsonAsync(
            "/api/v1/purchase/suppliers", new { Code = $"TSP{suffix}", Name = $"Supplier {suffix}" });
        supplier.StatusCode.ShouldBe(HttpStatusCode.Created, await supplier.Content.ReadAsStringAsync());
        Guid supplierId = (await supplier.Content.ReadFromJsonAsync<SupplierResponse>())!.SupplierId;

        HttpResponseMessage customer = await client.PostAsJsonAsync(
            "/api/v1/sales/customers",
            billWise
                ? new { Code = $"TCU{suffix}", Name = $"Customer {suffix}", Terms = (object?)new { IsBillWise = true } }
                : new { Code = $"TCU{suffix}", Name = $"Customer {suffix}", Terms = (object?)null });
        customer.StatusCode.ShouldBe(HttpStatusCode.Created, await customer.Content.ReadAsStringAsync());
        Guid customerId = (await customer.Content.ReadFromJsonAsync<CustomerResponse>())!.CustomerId;

        return new TestMasters(customerId, supplierId, warehouseId, productId, code, unitId);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Inventory}/{resource}", body);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<Guid>();
    }
}
