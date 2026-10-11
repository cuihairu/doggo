using System.Text.Json;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class ProductApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<Product> SeedCrateAsync(
        string name = "Snakebite Case",
        ProductStatus status = ProductStatus.OnShelf,
        ProductType type = ProductType.Crate)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();

        var product = new Product
        {
            // Pre-assign so child rows reference the real id (ValueGeneratedNever).
            Id = Guid.CreateVersion7(),
            Type = type,
            Name = name,
            ClassId = "493",
            IconUrl = "https://img/case.png",
            PurchasePriceCents = 1000,
            ReferencePriceCents = 1200,
            AdjustPriceCents = 100,
            FinalPriceCents = 1300,
            RegularPriceCents = 1600,
            Status = status,
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
        db.PromotionPlans.Add(new PromotionPlan
        {
            ProductId = product.Id,
            DiscountPriceCents = 999,
            Stock = 10,
            PerUserLimit = 1,
            StartAtUtc = DateTime.UtcNow.AddHours(-1),
            EndAtUtc = DateTime.UtcNow.AddHours(1)
        });
        db.ProductRecommends.Add(new ProductRecommend
        {
            ProductId = product.Id,
            Title = "本周热门",
            Description = "大家都在开",
            Type = "hot",
            SortOrder = 1
        });

        await db.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task List_returns_onshelf_products_only()
    {
        await SeedCrateAsync();
        await SeedCrateAsync(name: "Hidden Crate", status: ProductStatus.OffShelf);

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/products"));

        Assert.Equal(1, document.RootElement.GetProperty("total").GetInt32());
        var item = document.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Snakebite Case", item.GetProperty("name").GetString());
        Assert.Equal(1300, item.GetProperty("finalPriceCents").GetInt64());
    }

    [Fact]
    public async Task List_filters_by_type_and_search()
    {
        await SeedCrateAsync();
        await SeedCrateAsync(name: "Case Key", status: ProductStatus.SelfOpenOnly, type: ProductType.Key);

        var client = Factory.CreateClient();

        using var keys = JsonDocument.Parse(await client.GetStringAsync("/api/products?type=Key"));
        Assert.Equal(1, keys.RootElement.GetProperty("total").GetInt32());

        using var hit = JsonDocument.Parse(await client.GetStringAsync("/api/products?q=snakebite"));
        Assert.Equal(1, hit.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Detail_returns_plans_contents_tiers_and_promotion()
    {
        var product = await SeedCrateAsync();

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync($"/api/products/{product.Id}"));
        var root = document.RootElement;

        Assert.Equal(product.Id, root.GetProperty("id").GetGuid());
        Assert.Equal(1300, root.GetProperty("finalPriceCents").GetInt64());
        Assert.Equal(1600, root.GetProperty("regularPriceCents").GetInt64());

        var plan = root.GetProperty("plans").EnumerateArray().Single();
        Assert.Equal("3连开", plan.GetProperty("title").GetString());
        Assert.Equal(500, plan.GetProperty("cashbackBps").GetInt32());
        var option = plan.GetProperty("options").EnumerateArray().Single();
        Assert.Equal(3250, option.GetProperty("priceCents").GetInt64());

        var content = root.GetProperty("contents").EnumerateArray().Single();
        Assert.Equal((int)CaseQuality.Classified, content.GetProperty("quality").GetInt32());

        var tier = root.GetProperty("quantityTiers").EnumerateArray().Single();
        Assert.Equal(1200, tier.GetProperty("priceCents").GetInt64());

        var promotion = root.GetProperty("promotion");
        Assert.Equal(999, promotion.GetProperty("discountPriceCents").GetInt64());
        Assert.Equal(10, promotion.GetProperty("stock").GetInt32());
    }

    [Fact]
    public async Task Detail_excludes_expired_promotions()
    {
        var product = await SeedCrateAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var promotion = db.PromotionPlans.Single();
            promotion.EndAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync($"/api/products/{product.Id}"));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("promotion").ValueKind);
    }

    [Fact]
    public async Task Detail_of_offshelf_product_is_not_found()
    {
        var product = await SeedCrateAsync(name: "Hidden", status: ProductStatus.OffShelf);

        var client = Factory.CreateClient();
        using var response = await client.GetAsync($"/api/products/{product.Id}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Skus_contain_single_buy_and_plan_tiers()
    {
        var product = await SeedCrateAsync(status: ProductStatus.SelfOpenOnly);

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync($"/api/products/{product.Id}/skus"));
        var skus = document.RootElement.GetProperty("items");

        Assert.Equal(2, skus.GetArrayLength());

        var single = skus.EnumerateArray().First(s => s.GetProperty("optionId").ValueKind == JsonValueKind.Null);
        Assert.Equal(1300, single.GetProperty("unitPriceCents").GetInt64());
        Assert.Equal("self_open", single.GetProperty("serviceMode").GetString());
        Assert.Equal("final_price", single.GetProperty("priceSource").GetString());

        var planSku = skus.EnumerateArray().First(s => s.GetProperty("optionId").ValueKind != JsonValueKind.Null);
        Assert.Equal(3250, planSku.GetProperty("unitPriceCents").GetInt64());
        Assert.Equal(9750, planSku.GetProperty("totalPriceCents").GetInt64());
        Assert.Equal(500, planSku.GetProperty("cashbackBps").GetInt32());
        Assert.True(planSku.GetProperty("canUseCoupon").GetBoolean());
    }

    [Fact]
    public async Task Promotions_lists_active_promotions_with_product_info()
    {
        await SeedCrateAsync();

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/promotions"));

        var promotion = document.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Snakebite Case", promotion.GetProperty("productName").GetString());
        Assert.Equal(999, promotion.GetProperty("discountPriceCents").GetInt64());
        Assert.Equal(1300, promotion.GetProperty("originalPriceCents").GetInt64());
    }

    [Fact]
    public async Task Recommends_lists_enabled_recommends()
    {
        await SeedCrateAsync();

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/recommends"));

        var recommend = document.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("本周热门", recommend.GetProperty("title").GetString());
        Assert.Equal("Snakebite Case", recommend.GetProperty("productName").GetString());
        Assert.Equal("hot", recommend.GetProperty("type").GetString());
    }
}
