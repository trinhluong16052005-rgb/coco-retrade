using CoCoReTrade.Domain;

namespace CoCoReTrade.Application;

public interface IProductRecommendationService
{
    Task<IReadOnlyList<ProductCard>> SimilarAsync(ProductDetails product, string? viewerId, CancellationToken ct,
        IReadOnlyCollection<Guid>? excludedIds = null, int limit = 4);
    Task<IReadOnlyList<ProductCard>> CrossSellAsync(IReadOnlyCollection<Guid> selectedIds, string? viewerId, CancellationToken ct,
        IReadOnlyCollection<Guid>? excludedIds = null, int limit = 4);
    Task<IReadOnlyList<ProductCard>> SimilarForBasketAsync(IReadOnlyList<ProductCard> items, string viewerId,
        IReadOnlyCollection<Guid> excludedIds, CancellationToken ct, int limit = 6);
}
public sealed class ProductRecommendationService(IProductService products) : IProductRecommendationService
{
    public async Task<IReadOnlyList<ProductCard>> SimilarForBasketAsync(IReadOnlyList<ProductCard> items, string viewerId,
        IReadOnlyCollection<Guid> excludedIds, CancellationToken ct, int limit = 6)
    {
        if (items.Count == 0) return [];
        var storedCategories = items.SelectMany(x => ProductCategories.MatchingStoredValues(x.Category)).ToHashSet();
        var categories = ProductCategories.Main.Where(storedCategories.Contains);
        var excluded = items.Select(x => x.Id).Concat(excludedIds).ToHashSet();
        var brands = items.Where(x => !string.IsNullOrWhiteSpace(x.Brand))
            .Select(x => x.Brand).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var words = items.SelectMany(x => NameWords(x.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasClothing = items.Any(x => ProductCategories.IsClothing(x.Category));
        var candidates = (await products.ShopAsync(new ProductFilter(), ct)).ToList();
        foreach (var category in categories)
            candidates.AddRange(await products.ShopAsync(new ProductFilter(Category: category), ct));

        return candidates.DistinctBy(x => x.Id)
            .Where(x => !excluded.Contains(x.Id) && x.SellerId != viewerId && x.Status == ProductStatus.Active)
            .Select(x => new
            {
                Product = x,
                Score = (storedCategories.Contains(x.Category) ? 100 : 0) + (brands.Contains(x.Brand) ? 30 : 0)
                    + NameWords(x.Name).Count(words.Contains) * 5
                    + (hasClothing && ProductCategories.IsClothing(x.Category) ? 1 : 0)
            })
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score)
            .ThenBy(x => items.Min(item => Math.Abs(x.Product.AskingPrice - item.AskingPrice)))
            .ThenBy(x => x.Product.Id).Take(limit).Select(x => x.Product).ToArray();
    }

    private static IEnumerable<string> NameWords(string name) =>
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 2);

    public async Task<IReadOnlyList<ProductCard>> SimilarAsync(ProductDetails product, string? viewerId, CancellationToken ct,
        IReadOnlyCollection<Guid>? excludedIds = null, int limit = 4)
    {
        if (product.Status != CoCoReTrade.Domain.ProductStatus.Active) return [];
        var candidates = await products.ShopAsync(new ProductFilter(), ct);
        var excluded = (excludedIds ?? []).ToHashSet();
        var words = product.Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length > 2).ToHashSet();
        return candidates.Where(x => x.Id != product.Id && !excluded.Contains(x.Id) && x.SellerId != viewerId && x.Status == CoCoReTrade.Domain.ProductStatus.Active)
            .OrderByDescending(x => (x.Category == product.Category ? 100 : 0) + (x.Brand == product.Brand ? 30 : 0) +
                x.Name.ToLowerInvariant().Split(' ').Count(words.Contains) * 5)
            .ThenBy(x => Math.Abs(x.AskingPrice - product.AskingPrice)).ThenBy(x => x.Id).Take(limit).ToArray();
    }
    public async Task<IReadOnlyList<ProductCard>> CrossSellAsync(IReadOnlyCollection<Guid> selectedIds, string? viewerId, CancellationToken ct,
        IReadOnlyCollection<Guid>? excludedIds = null, int limit = 4)
    {
        if (selectedIds.Count == 0) return [];
        // Resolve selected items explicitly: an older item may not appear in the newest shop page.
        var selected = (await products.ShopAsync(new ProductFilter(Ids: selectedIds), ct))
            .Where(x => selectedIds.Contains(x.Id) && x.Status == ProductStatus.Active).ToArray();
        if (selected.Length == 0) return [];
        var preferAccessories = selected.Any(x => ProductCategories.IsClothing(x.Category));
        var preferClothing = !preferAccessories && selected.Any(x => ProductCategories.IsAccessory(x.Category));
        var all = (await products.ShopAsync(new ProductFilter(), ct)).ToList();
        IReadOnlyList<string> preferredCategories = preferAccessories ? [ProductCategories.Accessories]
            : preferClothing ? ProductCategories.Clothing : Array.Empty<string>();
        foreach (var category in preferredCategories)
            all.AddRange(await products.ShopAsync(new ProductFilter(Category: category), ct));
        var excluded = selectedIds.Concat(excludedIds ?? []).ToHashSet();
        var average = selected.Average(x => x.AskingPrice);
        return all.DistinctBy(x => x.Id)
            .Where(x => !excluded.Contains(x.Id) && x.SellerId != viewerId && x.Status == ProductStatus.Active)
            .OrderByDescending(x => preferAccessories && ProductCategories.IsAccessory(x.Category) ||
                preferClothing && ProductCategories.IsClothing(x.Category))
            .ThenBy(x => Math.Abs(x.AskingPrice - average)).ThenBy(x => x.Id).Take(limit).ToArray();
    }
}
