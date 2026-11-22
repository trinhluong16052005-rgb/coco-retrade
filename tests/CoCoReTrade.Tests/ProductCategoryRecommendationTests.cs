using CoCoReTrade.Application;
using CoCoReTrade.Domain;

namespace CoCoReTrade.Tests;

public sealed class ProductCategoryRecommendationTests
{
    [Theory]
    [InlineData("Phụ kiện", "Túi", true)]
    [InlineData("Phụ kiện", "Túi xách", true)]
    [InlineData("Phụ kiện", "Phụ kiện", true)]
    [InlineData("Phụ kiện", "Set đồ", false)]
    [InlineData("Túi", "Phụ kiện", true)]
    [InlineData("Túi", "Túi xách", true)]
    [InlineData("Túi xách", "Phụ kiện", true)]
    [InlineData("Túi xách", "Túi", true)]
    [InlineData("Túi xách", "Áo", false)]
    [InlineData("Váy", "Đầm/Váy", true)]
    [InlineData("Set đồ", "Set đồ", true)]
    [InlineData("Không tồn tại", "Áo", false)]
    public void CategoryFilterTreatsLegacyBagAliasesAsAccessories(string filter, string stored, bool matches)
        => Assert.Equal(matches, ProductCategories.Matches(stored, filter));

    [Fact]
    public void AvailableCategoriesContainOnlyFiveMainCategories()
        => Assert.Equal(new[] { "Áo", "Quần", "Đầm/Váy", "Set đồ", "Phụ kiện" }, ProductCategories.All);

    [Theory]
    [InlineData("Túi")]
    [InlineData("Túi xách")]
    [InlineData(" Phụ kiện ")]
    public void BagAliasesUseAccessoriesForStorageDisplayAndSizing(string category)
    {
        Assert.Equal(ProductCategories.Accessories, ProductCategories.Normalize(category));
        Assert.Equal(ProductCategories.Accessories, ProductCategories.Display(category));
        Assert.Equal(ProductCategories.Accessories, ProductCategories.Parent(category));
        Assert.Equal("Dài 80 cm", ProductSizing.Normalize(category, " Dài 80 cm "));
        var bag = Product.Submit("seller", "CG-BAG", "Túi đeo chéo", "", category, "Free size", 500000, 250000, 95, true, Images());
        Assert.Equal(ProductCategories.Accessories, bag.Category);
    }

    [Fact]
    public void NewListingsAcceptSetsAndNormalizeBagAliasesButRejectUnknownCategories()
    {
        var set = Product.Submit("seller", "CG-SET", "Set áo và quần", "", "Set đồ", "M", 500000, 250000, 95, true, Images());
        Assert.Equal(ProductCategories.Sets, set.Category);
        Assert.Equal("3XL", ProductSizing.Normalize(set.Category, "XXXL"));
        var bag = Product.Submit("seller", "CG-BAG", "Túi đeo chéo", "", "Túi xách", "Free size", 500000, 250000, 95, true, Images());
        Assert.Equal(ProductCategories.Accessories, bag.Category);
        Assert.Equal(ProductCategories.Accessories, ProductCategories.Parent(bag.Category));
        Assert.Equal("20 x 30 cm", ProductSizing.Normalize("Túi xách", "20 x 30 cm"));
        Assert.Throws<ArgumentException>(() => Product.Submit("seller", "CG-NO", "Sản phẩm", "", "Không tồn tại", "M", 500000, 250000, 95, true, Images()));
    }

    [Theory]
    [InlineData("Áo")]
    [InlineData("Quần")]
    [InlineData("Đầm/Váy")]
    [InlineData("Set đồ")]
    public async Task ClothingPrioritizesAvailableAccessoriesEvenWhenTheyAreNotOnNewestShopPage(string category)
    {
        var selected = Card(1, category, 300000);
        var sameCategory = Card(2, category, 300000);
        var accessory = Card(3, "Phụ kiện", 100000);
        var bag = Card(4, "Túi", 400000);
        var service = new ProductRecommendationService(new Catalog([selected, sameCategory, accessory, bag], [sameCategory]));

        var recommendations = await service.CrossSellAsync([selected.Id], "buyer", default);

        Assert.Equal(2, recommendations.TakeWhile(x => ProductCategories.IsAccessory(x.Category)).Count());
        Assert.Contains(accessory.Id, recommendations.Select(x => x.Id));
        Assert.Contains(bag.Id, recommendations.Select(x => x.Id));
        Assert.DoesNotContain(selected.Id, recommendations.Select(x => x.Id));
    }

    [Theory]
    [InlineData("Phụ kiện")]
    [InlineData("Túi")]
    public async Task AccessoriesPrioritizeClothingAndNeverSuggestCurrentCartOwnedOrUnavailableItems(string category)
    {
        var selected = Card(1, category, 300000);
        var currentCart = Card(2, "Set đồ", 250000);
        var owned = Card(3, "Áo", 300000) with { SellerId = "buyer" };
        var reserved = Card(4, "Quần", 250000) with { Status = ProductStatus.Reserved };
        var sold = Card(5, "Đầm/Váy", 250000) with { Status = ProductStatus.Sold };
        var top = Card(6, "Áo", 350000);
        var bag = Card(7, "Túi", 300000);
        var service = new ProductRecommendationService(new Catalog([selected, currentCart, owned, reserved, sold, top, bag]));

        var recommendations = await service.CrossSellAsync([selected.Id], "buyer", default, [currentCart.Id]);

        Assert.Equal(top.Id, recommendations[0].Id);
        Assert.DoesNotContain(recommendations, x => x.Id == selected.Id || x.Id == currentCart.Id || x.SellerId == "buyer" || x.Status != ProductStatus.Active);
    }

    [Fact]
    public async Task DetailCanRequestFiveAndCheckoutSixAvailableSuggestionsWhileExistingCallersKeepFour()
    {
        var selected = Card(1, "Áo", 300000);
        var accessories = Enumerable.Range(2, 8).Select(id => Card(id, "Phụ kiện", 300000 + id * 1000)).ToArray();
        var excluded = accessories[0];
        var unavailable = Card(10, "Phụ kiện", 300000) with { Status = ProductStatus.Reserved };
        var owned = Card(11, "Phụ kiện", 300000) with { SellerId = "buyer" };
        var service = new ProductRecommendationService(new Catalog([selected, .. accessories, unavailable, owned]));

        var existing = await service.CrossSellAsync([selected.Id], "buyer", default, [excluded.Id]);
        var detail = await service.CrossSellAsync([selected.Id], "buyer", default, [excluded.Id], limit: 5);
        var checkout = await service.CrossSellAsync([selected.Id], "buyer", default, [excluded.Id], limit: 6);

        Assert.Equal(4, existing.Count);
        Assert.Equal(5, detail.Count);
        Assert.Equal(6, checkout.Count);
        Assert.Equal(existing.Select(x => x.Id), checkout.Take(4).Select(x => x.Id));
        Assert.Equal(detail.Select(x => x.Id), checkout.Take(5).Select(x => x.Id));
        Assert.All(checkout, x => Assert.True(ProductCategories.IsAccessory(x.Category)));
        Assert.DoesNotContain(checkout, x => x.Id == selected.Id || x.Id == excluded.Id || x.SellerId == "buyer" || x.Status != ProductStatus.Active);
    }

    [Fact]
    public async Task SimilarCanRequestFiveAfterExcludingAlreadyShownProductsWhileDefaultRemainsFour()
    {
        var current = Card(1, ProductCategories.Accessories, 300000);
        var candidates = Enumerable.Range(2, 8).Select(id => Card(id, current.Category, 300000 + id * 1000)).ToArray();
        var excluded = candidates.Take(2).Select(x => x.Id).ToArray();
        var owned = Card(10, current.Category, 300000) with { SellerId = "buyer" };
        var reserved = Card(11, current.Category, 300000) with { Status = ProductStatus.Reserved };
        var sold = Card(12, current.Category, 300000) with { Status = ProductStatus.Sold };
        var service = new ProductRecommendationService(new Catalog([current, .. candidates, owned, reserved, sold]));
        var product = new ProductDetails(current.Id, current.Sku, current.Name, current.Brand, current.Category,
            current.Size, 500000, current.AskingPrice, 95, 95, true, current.Status, current.SellerId,
            null, [], new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var existing = await service.SimilarAsync(product, "buyer", default, excluded);
        var detail = await service.SimilarAsync(product, "buyer", default, excluded, limit: 5);

        Assert.Equal(4, existing.Count);
        Assert.Equal(5, detail.Count);
        Assert.Equal(candidates.Skip(2).Take(5).Select(x => x.Id), detail.Select(x => x.Id));
        Assert.Equal(existing.Select(x => x.Id), detail.Take(4).Select(x => x.Id));
        Assert.DoesNotContain(detail, x => x.Id == current.Id || excluded.Contains(x.Id) ||
            x.SellerId == "buyer" || x.Status != ProductStatus.Active);
        Assert.Empty(await service.SimilarAsync(product with { Status = ProductStatus.Sold }, "buyer", default, excluded, limit: 5));
    }

    [Fact]
    public async Task EmptyOrUnavailableSelectionProducesNoCrossSell()
    {
        var sold = Card(1, "Áo", 300000) with { Status = ProductStatus.Sold };
        var service = new ProductRecommendationService(new Catalog([sold, Card(2, "Túi", 200000)]));
        Assert.Empty(await service.CrossSellAsync([], "buyer", default));
        Assert.Empty(await service.CrossSellAsync([sold.Id], "buyer", default));
    }

    private static ProductCard Card(int id, string category, decimal price) => new(
        Guid.Parse($"00000000-0000-0000-0000-{id:D12}"), $"CC-2026-{id:D6}", "Sản phẩm " + id, "CoCo",
        category, ProductSizing.IsOneSize(category) ? "Free size" : "M", price, 95, ProductStatus.Active, null, "seller");
    private static ProductImage[] Images() => [new("a.jpg", "image/jpeg", 0), new("b.jpg", "image/jpeg", 1), new("c.jpg", "image/jpeg", 2)];

    private sealed class Catalog(IReadOnlyList<ProductCard> cards, IReadOnlyList<ProductCard>? newest = null) : IProductService
    {
        public Task<IReadOnlyList<Guid>> RemoveFromClosetAsync(string sellerId, IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductCard>> ShopAsync(ProductFilter filter, CancellationToken ct)
        {
            IEnumerable<ProductCard> query = filter.Ids is null && filter.Category is null ? newest ?? cards : cards;
            if (filter.Ids is { } ids) query = query.Where(x => ids.Contains(x.Id));
            if (filter.Category is { } category) query = query.Where(x => ProductCategories.Matches(x.Category, category));
            return Task.FromResult<IReadOnlyList<ProductCard>>(query.ToArray());
        }
        public Task<ProductDetails> SubmitAsync(string sellerId, SubmitListing request, CancellationToken ct) => throw new NotSupportedException();
        public async Task<ProductPage<ProductCard>> ShopPageAsync(ProductFilter filter, int pageNumber, CancellationToken ct) =>
            ProductPage<ProductCard>.From(await ShopAsync(filter, ct), pageNumber);
        public Task<ProductDetails> EditAsync(string sellerId, Guid id, EditListing request, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductCard>> ClosetAsync(string sellerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductCard>> PendingAsync(string? search, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProductDetails?> VisibleDetailsAsync(Guid id, string? viewerId, bool isAdmin, CancellationToken ct) => throw new NotSupportedException();
        public Task<(Stream Stream, string ContentType)?> VisibleImageAsync(Guid imageId, string? viewerId, bool canInspectPending, bool canManageAll, CancellationToken ct) => throw new NotSupportedException();
    }
}
