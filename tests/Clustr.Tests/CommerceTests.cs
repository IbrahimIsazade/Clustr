using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Clustr.Tests;

public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "clustr-tests", Guid.NewGuid().ToString());
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["DataDirectory"] = directory }));
    public HttpClient Client()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Clustr-Request", "1");
        return client;
    }
}

public class CommerceTests
{
    [Fact]
    public async Task SampleDataIsAtomicAndCannotBeLoadedTwice()
    {
        using var app = new TestApp(); var (c, w) = await Owner(app);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/w/{w}/sample-data", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/w/{w}/sample-data", null)).StatusCode);
        var products = await c.GetFromJsonAsync<JsonNode>($"/api/w/{w}/products");
        Assert.Equal(6, (int)products!["total"]!);
        var movements = await c.GetFromJsonAsync<JsonArray>($"/api/w/{w}/movements");
        Assert.Equal(6, movements!.Count);
        var csv = await c.GetAsync($"/api/w/{w}/export");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Total EUR", await csv.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvalidStockAndDuplicateSkuAreRejectedAndNonRestockingRefundKeepsStock()
    {
        using var app = new TestApp(); var (c, w) = await Owner(app); var p = await Product(c, w, 2);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/w/{w}/products/{p}/stock", new { delta = -3, reason = "Too many" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/w/{w}/products", new { name = "Duplicate", sku = "cof", category = "Drinks", priceCents = 100, lowStockThreshold = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/w/{w}/sales", Cart(p, 0))).StatusCode);
        var sale = await Post(c, $"/api/w/{w}/sales", Cart(p));
        (await c.PostAsJsonAsync($"/api/w/{w}/sales/{sale["id"]}/refund", new { reason = "Damaged", restock = false })).EnsureSuccessStatusCode();
        var products = await c.GetFromJsonAsync<JsonNode>($"/api/w/{w}/products");
        Assert.Equal(1, (int)products!["items"]![0]!["stock"]!);
    }

    private static async Task<JsonNode> Post(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body);
        Assert.True(r.IsSuccessStatusCode, $"{path}: {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<JsonNode>())!;
    }
    private static async Task<(HttpClient Client, int Workspace)> Owner(TestApp app, string email = "owner@example.com")
    {
        var c = app.Client();
        await Post(c, "/api/auth/register", new { email, password = "Some long password!" });
        var w = await Post(c, "/api/workspaces", new { name = "Test shop" });
        return (c, (int)w["id"]!);
    }
    private static async Task<int> Product(HttpClient c, int w, int stock = 10)
    {
        var p = await Post(c, $"/api/w/{w}/products", new { name = "Coffee", sku = "COF", category = "Drinks", priceCents = 350, lowStockThreshold = 2 });
        var id = (int)p["id"]!;
        var r = await c.PostAsJsonAsync($"/api/w/{w}/products/{id}/stock", new { delta = stock, reason = "Opening stock" });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        return id;
    }
    private static object Cart(int product, int quantity = 1, string? key = null, int? customerId = null) => new { requestKey = key ?? Guid.NewGuid().ToString(), customerId, paymentMethod = "Cash", lines = new[] { new { productId = product, quantity } } };

    [Fact]
    public async Task CheckoutIsIdempotentAndRefundRestoresStockOnlyOnce()
    {
        using var app = new TestApp(); var (c, w) = await Owner(app); var p = await Product(c, w);
        var cart = Cart(p, 3); var sale = await Post(c, $"/api/w/{w}/sales", cart);
        var retry = await Post(c, $"/api/w/{w}/sales", cart);
        Assert.Equal((int)sale["id"]!, (int)retry["id"]!); Assert.Equal(1050, (int)sale["totalCents"]!);
        var products = await c.GetFromJsonAsync<JsonNode>($"/api/w/{w}/products"); Assert.Equal(7, (int)products!["items"]![0]!["stock"]!);
        var r = await c.PostAsJsonAsync($"/api/w/{w}/sales/{sale["id"]}/refund", new { reason = "Returned", restock = true });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        r = await c.PostAsJsonAsync($"/api/w/{w}/sales/{sale["id"]}/refund", new { reason = "Again", restock = true });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        products = await c.GetFromJsonAsync<JsonNode>($"/api/w/{w}/products"); Assert.Equal(10, (int)products!["items"]![0]!["stock"]!);
        var overview = await c.GetFromJsonAsync<JsonNode>($"/api/w/{w}/overview"); Assert.Equal(0, (int)overview!["revenueCents"]!);
    }

    [Fact]
    public async Task CrossTenantAccessAndRelatedEntityInjectionAreDenied()
    {
        using var app = new TestApp(); var (a, wa) = await Owner(app); var (b, wb) = await Owner(app, "other@example.com");
        var pa = await Product(a, wa); var pb = await Product(b, wb);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.GetAsync($"/api/w/{wb}/products")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync($"/api/w/{wa}/sales", Cart(pb))).StatusCode);
        var customer = await Post(b, $"/api/w/{wb}/customers", new { name = "Other customer", email = "customer@example.com" });
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync($"/api/w/{wa}/sales", Cart(pa, customerId: (int)customer["id"]!))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.DeleteAsync($"/api/w/{wa}/products/{pb}")).StatusCode);
    }

    [Fact]
    public async Task CashierCanSellButCannotManageStockOrRefundAndRevocationTakesEffect()
    {
        using var app = new TestApp(); var (owner, w) = await Owner(app); var p = await Product(owner, w);
        var cashier = app.Client(); await Post(cashier, "/api/auth/register", new { email = "cashier@example.com", password = "Long cashier password" });
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/w/{w}/members", new { email = "cashier@example.com", role = "Cashier" })).StatusCode);
        var sale = await Post(cashier, $"/api/w/{w}/sales", Cart(p));
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.DeleteAsync($"/api/w/{w}/products/{p}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync($"/api/w/{w}/products/{p}/stock", new { delta = 1, reason = "Not allowed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync($"/api/w/{w}/sales/{sale["id"]}/refund", new { reason = "Not allowed", restock = true })).StatusCode);
        var members = await owner.GetFromJsonAsync<JsonArray>($"/api/w/{w}/members");
        var id = members!.Single(x => (string)x!["role"]! == "Cashier")!["id"];
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/w/{w}/members/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/w/{w}/products")).StatusCode);
    }

    [Fact]
    public async Task LastItemCannotBeSoldTwice()
    {
        using var app = new TestApp(); var (c, w) = await Owner(app); var p = await Product(c, w, 1);
        var responses = await Task.WhenAll(c.PostAsJsonAsync($"/api/w/{w}/sales", Cart(p)), c.PostAsJsonAsync($"/api/w/{w}/sales", Cart(p)));
        Assert.Single(responses, r => r.IsSuccessStatusCode); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var products = await c.GetFromJsonAsync<JsonNode>($"/api/w/{w}/products"); Assert.Equal(0, (int)products!["items"]![0]!["stock"]!);
    }

    [Fact]
    public async Task FailedCartRollsBackAndChangedRetryIsRejected()
    {
        using var app = new TestApp(); var (c, w) = await Owner(app); var p = await Product(c, w);
        var failed = await c.PostAsJsonAsync($"/api/w/{w}/sales", new { requestKey = Guid.NewGuid().ToString(), paymentMethod = "Cash", lines = new[] { new { productId = p, quantity = 2 }, new { productId = int.MaxValue, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);
        var products = await c.GetFromJsonAsync<JsonNode>($"/api/w/{w}/products"); Assert.Equal(10, (int)products!["items"]![0]!["stock"]!);
        var key = Guid.NewGuid().ToString(); await Post(c, $"/api/w/{w}/sales", Cart(p, key: key));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/w/{w}/sales", Cart(p, 2, key))).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<JsonArray>($"/api/w/{w}/sales?page=2"))!);
    }

    [Fact]
    public async Task SaleSnapshotSurvivesPriceChangesAndArchival()
    {
        using var app = new TestApp(); var (c, w) = await Owner(app); var p = await Product(c, w);
        var sale = await Post(c, $"/api/w/{w}/sales", Cart(p));
        (await c.PutAsJsonAsync($"/api/w/{w}/products/{p}", new { name = "Changed", sku = "COF", category = "Drinks", priceCents = 999, lowStockThreshold = 2 })).EnsureSuccessStatusCode();
        (await c.DeleteAsync($"/api/w/{w}/products/{p}")).EnsureSuccessStatusCode();
        sale = (await c.GetFromJsonAsync<JsonNode>($"/api/w/{w}/sales/{sale["id"]}"))!;
        Assert.Equal("Coffee", (string)sale["lines"]![0]!["name"]!); Assert.Equal(350, (int)sale["totalCents"]!);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync($"/api/w/{w}/sales", Cart(p))).StatusCode);
    }

    [Fact]
    public async Task AuthenticationAndWriteHeaderAreRequired()
    {
        using var app = new TestApp(); using var c = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/workspaces")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/auth/register", new { email = "x@y.com", password = "long password" })).StatusCode);
        c.DefaultRequestHeaders.Add("X-Clustr-Request", "1");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/register", new { email = "x@y.com", password = "short" })).StatusCode);
        await Post(c, "/api/auth/register", new { email = "x@y.com", password = "long password" });
        (await c.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/workspaces")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/login", new { email = "x@y.com", password = "incorrect password" })).StatusCode);
        await Post(c, "/api/auth/login", new { email = "x@y.com", password = "long password" });
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/workspaces")).StatusCode);
    }
}
