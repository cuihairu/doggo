using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>Write path for product data: create/update product, replace plans, tiers and contents.</summary>
public class ProductAdminApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string Password = "pass-word-1";

    private async Task<string> RegisterAsync(string account)
    {
        var client = Factory.CreateClient();
        var register = await client.PostAsync("/api/auth/register", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        register.EnsureSuccessStatusCode();
        return account;
    }

    private async Task<HttpClient> LoginClientAsync(string account)
    {
        var client = Factory.CreateClient();
        var login = await client.PostAsync("/api/auth/login", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        login.EnsureSuccessStatusCode();
        var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<HttpClient> AdminClientAsync(string account)
    {
        await RegisterAsync(account);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var user = await db.UserAccounts.SingleAsync(u => u.Phone == account);
            user.IsAdmin = true;
            await db.SaveChangesAsync();
        }

        return await LoginClientAsync(account);
    }

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private async Task<Guid> SeedCrateAsync(string name = "Snakebite Case")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();

        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Type = ProductType.Crate,
            Name = name,
            ClassId = "493",
            IconUrl = "https://img/case.png",
            PurchasePriceCents = 1000,
            ReferencePriceCents = 1200,
            AdjustPriceCents = 100,
            FinalPriceCents = 1300,
            RegularPriceCents = 1600,
            Status = ProductStatus.OnShelf,
            SortOrder = 1
        };
        db.Products.Add(product);

        var plan = new CasePlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Title = "3连开",
            MinQuantity = 3,
            MaxQuantity = 3,
            CanUseCoupon = true,
            CanUseCoin = false,
            CashbackBps = 500
        };
        db.CasePlans.Add(plan);
        db.CasePlanOptions.Add(new CasePlanOption
        {
            PlanId = plan.Id,
            UpliftBps = 800,
            Quantity = 3,
            MinQuantity = 3,
            PriceCents = 3250
        });
        db.CaseContents.Add(new CaseContent
        {
            ProductId = product.Id,
            Title = "AK-47 | Redline",
            Quality = CaseQuality.Classified,
            Color = "#eb4b4b",
            ImageUrl = "https://img/ak.png",
            ReferencePriceCents = 9800,
            SortOrder = 1
        });
        db.QuantityOptions.Add(new QuantityOption { ProductId = product.Id, Quantity = 5, PriceCents = 1200 });

        await db.SaveChangesAsync();
        return product.Id;
    }

    private static object ProductForm(string name) => new
    {
        name,
        type = 1,
        classId = "493",
        iconUrl = "https://img/case.png",
        purchasePriceCents = 1000,
        referencePriceCents = 1200,
        adjustPriceCents = 100,
        finalPriceCents = 1300,
        regularPriceCents = 1600,
        status = 3,
        planFlags = "hot",
        buyNotice = "先充值再开",
        sortOrder = 7
    };

    [Fact]
    public async Task Admin_creates_product()
    {
        var client = await AdminClientAsync("13900000101");

        var created = await client.PostAsync("/api/admin/products", JsonBody(ProductForm("Dream Case")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var listed = JsonDocument.Parse(await client.GetStringAsync("/api/admin/products")).RootElement;
        var row = listed.EnumerateArray()
            .Single(p => p.GetProperty("id").GetString() == id);
        Assert.Equal("Dream Case", row.GetProperty("name").GetString());
        Assert.Equal(1300L, row.GetProperty("finalPriceCents").GetInt64());

        // On-shelf products reach the public surface too.
        var detail = await client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task Admin_creates_product_requires_a_name()
    {
        var client = await AdminClientAsync("13900000102");

        var created = await client.PostAsync("/api/admin/products", JsonBody(ProductForm("")));
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    [Fact]
    public async Task Admin_updates_product()
    {
        var productId = await SeedCrateAsync();
        var client = await AdminClientAsync("13900000103");

        var body = new Dictionary<string, object?>
        {
            ["name"] = "Snakebite Case V2",
            ["purchasePriceCents"] = 1100,
            ["referencePriceCents"] = 1300,
            ["adjustPriceCents"] = 150,
            ["finalPriceCents"] = 1450,
            ["regularPriceCents"] = 1700,
            ["planFlags"] = "hot,new",
            ["buyNotice"] = "新公告",
            ["sortOrder"] = 9
        };

        var response = await client.PutAsync($"/api/admin/products/{productId}", JsonBody(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var product = await db.Products.SingleAsync(p => p.Id == productId);
            Assert.Equal("Snakebite Case V2", product.Name);
            Assert.Equal(1450L, product.FinalPriceCents);
            Assert.Equal(1700L, product.RegularPriceCents);
            Assert.Equal("hot,new", product.PlanFlags);
            Assert.Equal("新公告", product.BuyNotice);
            Assert.Equal(9, product.SortOrder);
            Assert.Equal(1100L, product.PurchasePriceCents);
        }

        // Type/classId/iconUrl are updatable through the same endpoint.
        var touch = new Dictionary<string, object?>
        {
            ["type"] = 0,
            ["classId"] = "777",
            ["iconUrl"] = "https://img/key.png"
        };
        var touchResponse = await client.PutAsync($"/api/admin/products/{productId}", JsonBody(touch));
        Assert.Equal(HttpStatusCode.OK, touchResponse.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var product = await db.Products.SingleAsync(p => p.Id == productId);
            Assert.Equal(ProductType.Key, product.Type);
            Assert.Equal("777", product.ClassId);
            Assert.Equal("https://img/key.png", product.IconUrl);
        }
    }

    [Fact]
    public async Task Admin_update_and_plans_write_404_for_unknown_product()
    {
        var client = await AdminClientAsync("13900000104");
        var unknown = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync(
            $"/api/admin/products/{unknown}", JsonBody(ProductForm("Ghost")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync(
            $"/api/admin/products/{unknown}/plans", JsonBody(new { plans = Array.Empty<object>() }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync(
            $"/api/admin/products/{unknown}/quantity-options", JsonBody(new { options = Array.Empty<object>() }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync(
            $"/api/admin/products/{unknown}/contents", JsonBody(new { contents = Array.Empty<object>() }))).StatusCode);
    }

    [Fact]
    public async Task Admin_replaces_plans_and_options()
    {
        var productId = await SeedCrateAsync();
        var client = await AdminClientAsync("13900000105");

        var body = new
        {
            plans = new object[]
            {
                new
                {
                    title = "2连开",
                    minQuantity = 2,
                    maxQuantity = 2,
                    canUseCoupon = true,
                    canUseCoin = false,
                    cashbackBps = 300,
                    enabled = true,
                    options = new object[]
                    {
                        new { upliftBps = 600, quantity = 2, minQuantity = 2, priceCents = 2400, enabled = true }
                    }
                },
                new
                {
                    title = "5连开",
                    minQuantity = 5,
                    maxQuantity = 5,
                    canUseCoupon = false,
                    canUseCoin = true,
                    cashbackBps = 0,
                    enabled = true,
                    options = Array.Empty<object>()
                }
            }
        };

        var response = await client.PutAsync($"/api/admin/products/{productId}/plans", JsonBody(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var plans = await db.CasePlans.AsNoTracking().Where(p => p.ProductId == productId).ToListAsync();
            Assert.Equal(2, plans.Count);

            var two = plans.Single(p => p.Title == "2连开");
            Assert.Equal(300, two.CashbackBps);
            Assert.True(two.CanUseCoupon);
            Assert.False(two.CanUseCoin);

            var twoOptions = await db.CasePlanOptions.AsNoTracking().Where(o => o.PlanId == two.Id).ToListAsync();
            Assert.Single(twoOptions);
            Assert.Equal(2400L, twoOptions[0].PriceCents);
            Assert.Equal(600, twoOptions[0].UpliftBps);

            var five = plans.Single(p => p.Title == "5连开");
            Assert.Equal(0, five.CashbackBps);
            Assert.False(five.CanUseCoupon);
            Assert.True(five.CanUseCoin);
            Assert.Empty(await db.CasePlanOptions.AsNoTracking().Where(o => o.PlanId == five.Id).ToListAsync());
        }
    }

    [Fact]
    public async Task Admin_replaces_quantity_options()
    {
        var productId = await SeedCrateAsync();
        var client = await AdminClientAsync("13900000106");

        var body = new
        {
            options = new object[]
            {
                new { quantity = 10, priceCents = 11000, alarmThreshold = 3 },
                new { quantity = 20, priceCents = 21000, alarmThreshold = 5 }
            }
        };

        var response = await client.PutAsync($"/api/admin/products/{productId}/quantity-options", JsonBody(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/admin/products/{productId}/plans")).RootElement;
        var tiers = detail.GetProperty("quantityOptions").EnumerateArray().ToList();
        Assert.Equal(2, tiers.Count);
        Assert.Equal(10, tiers[0].GetProperty("quantity").GetInt32());
        Assert.Equal(11000L, tiers[0].GetProperty("priceCents").GetInt64());
        Assert.Equal(3, tiers[0].GetProperty("alarmThreshold").GetInt32());
    }

    [Fact]
    public async Task Admin_replaces_contents()
    {
        var productId = await SeedCrateAsync();
        var client = await AdminClientAsync("13900000107");

        var body = new
        {
            contents = new object[]
            {
                new { title = "M4A4 | Howl", quality = 6, color = "#eb4b4b", imageUrl = "https://img/m4.png", referencePriceCents = 300000, sortOrder = 1 },
                new { title = "AWP | Asiimov", quality = 5, color = "#eb4b4b", imageUrl = "https://img/awp.png", referencePriceCents = 9800, sortOrder = 2 }
            }
        };

        var response = await client.PutAsync($"/api/admin/products/{productId}/contents", JsonBody(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/products/{productId}")).RootElement;
        var contents = detail.GetProperty("contents").EnumerateArray().ToList();
        Assert.Equal(2, contents.Count);
        Assert.Equal("M4A4 | Howl", contents[0].GetProperty("title").GetString());
        Assert.Equal((int)CaseQuality.Covert, contents[0].GetProperty("quality").GetInt32());
        Assert.Equal(300000L, contents[0].GetProperty("referencePriceCents").GetInt64());
        Assert.Equal("AWP | Asiimov", contents[1].GetProperty("title").GetString());
        Assert.Equal((int)CaseQuality.Classified, contents[1].GetProperty("quality").GetInt32());
    }

    [Fact]
    public async Task Product_write_surface_still_requires_admin_role()
    {
        var client = await LoginClientAsync(await RegisterAsync("13900000108"));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(
            "/api/admin/products", JsonBody(ProductForm("Nope")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync(
            $"/api/admin/products/{Guid.CreateVersion7()}/plans", JsonBody(new { plans = Array.Empty<object>() }))).StatusCode);
    }
}
