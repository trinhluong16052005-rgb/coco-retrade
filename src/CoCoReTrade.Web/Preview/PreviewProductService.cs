using CoCoReTrade.Application;
using CoCoReTrade.Domain;

namespace CoCoReTrade.Web.Preview;

// Development-only catalogue. It is registered only when Preview:Enabled is true.
public sealed class PreviewProductService : IProductService
{
    public Task<IReadOnlyList<Guid>> RemoveFromClosetAsync(string sellerId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        throw new NotSupportedException("Preview mode is read-only.");
    private sealed record Sample(ProductCard Card, decimal OriginalPrice);
    private static readonly Sample[] Samples =
    [
        Create(1, "Áo khoác denim vintage", "Zara", "Áo", "M", 320_000, 650_000, 95),
        Create(2, "Đầm midi hoa nhí", "Mango", "Đầm/Váy", "S", 420_000, 890_000, 99),
        Create(3, "Quần ống rộng linen", "Uniqlo", "Quần", "M", 260_000, 590_000, 95),
        Create(4, "Túi đeo vai cổ điển", "Charles & Keith", "Phụ kiện", "Free size", 540_000, 1_100_000, 90),
        Create(5, "Áo sơ mi cotton", "COS", "Áo", "L", 380_000, 790_000, 99),
        Create(6, "Blazer dáng suông", "Massimo Dutti", "Áo", "S", 750_000, 1_700_000, 95)
    ];

    private static Sample Create(int number, string name, string brand, string category, string size,
        decimal askingPrice, decimal originalPrice, int condition)
    {
        var id = Guid.Parse($"00000000-0000-0000-0000-{number:D12}");
        return new Sample(new ProductCard(id, $"CC-2026-{number:D6}", name, brand, category, size,
            askingPrice, condition, ProductStatus.Active, null, "preview-seller"), originalPrice);
    }

    public Task<IReadOnlyList<ProductCard>> ShopAsync(ProductFilter filter, CancellationToken ct)
    {
        IEnumerable<ProductCard> cards = Samples.Select(x => x.Card);
        if (!string.IsNullOrWhiteSpace(filter.Search))
            cards = cards.Where(x => new[] { x.Name, x.Brand, x.Sku }.Any(y => y.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(filter.Category)) cards = cards.Where(x => ProductCategories.Matches(x.Category, filter.Category));
        if (filter.Ids is { } ids) cards = cards.Where(x => ids.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(filter.SellerId)) cards = cards.Where(x => x.SellerId == filter.SellerId);
        if (!string.IsNullOrWhiteSpace(filter.Brand)) cards = cards.Where(x => x.Brand.Contains(filter.Brand, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.Size)) cards = cards.Where(x => x.Size.Equals(filter.Size, StringComparison.OrdinalIgnoreCase));
        if (filter.MinPrice.HasValue) cards = cards.Where(x => x.AskingPrice >= filter.MinPrice.Value);
        if (filter.MaxPrice.HasValue) cards = cards.Where(x => x.AskingPrice <= filter.MaxPrice.Value);
        if (filter.MinCondition.HasValue) cards = cards.Where(x => x.ConditionPercent >= filter.MinCondition.Value);
        cards = filter.Sort switch
        {
            "price_asc" => cards.OrderBy(x => x.AskingPrice),
            "price_desc" => cards.OrderByDescending(x => x.AskingPrice),
            _ => cards.OrderByDescending(x => x.Sku)
        };
        return Task.FromResult<IReadOnlyList<ProductCard>>(cards.ToList());
    }

    public async Task<ProductPage<ProductCard>> ShopPageAsync(ProductFilter filter, int pageNumber, CancellationToken ct) =>
        ProductPage<ProductCard>.From(await ShopAsync(filter, ct), pageNumber);

    public Task<ProductDetails?> VisibleDetailsAsync(Guid id, string? viewerId, bool isAdmin, CancellationToken ct)
    {
        var sample = Samples.FirstOrDefault(x => x.Card.Id == id);
        ProductDetails? details = sample is null ? null : new ProductDetails(sample.Card.Id, sample.Card.Sku,
            sample.Card.Name, sample.Card.Brand, sample.Card.Category, sample.Card.Size, sample.OriginalPrice,
            sample.Card.AskingPrice, sample.Card.ConditionPercent, sample.Card.ConditionPercent, true,
            ProductStatus.Active, sample.Card.SellerId, null, [], DateTime.UtcNow);
        return Task.FromResult(details);
    }

    public Task<(Stream Stream, string ContentType)?> VisibleImageAsync(Guid imageId, string? viewerId,
        bool canInspectPending, bool canManageAll, CancellationToken ct) =>
        Task.FromResult<(Stream, string)?>(null);

    public Task<ProductDetails> SubmitAsync(string sellerId, SubmitListing request, CancellationToken ct) =>
        throw new NotSupportedException("Preview mode is read-only.");
    public Task<ProductDetails> EditAsync(string sellerId, Guid id, EditListing request, CancellationToken ct) =>
        throw new NotSupportedException("Preview mode is read-only.");
    public Task<IReadOnlyList<ProductCard>> ClosetAsync(string sellerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProductCard>>([]);
    public Task<IReadOnlyList<ProductCard>> PendingAsync(string? search, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProductCard>>([]);
}
