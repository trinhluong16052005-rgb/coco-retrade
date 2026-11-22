using System.Data;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class ProductRepository(AppDbContext db, ShippingPolicy shipping) : IProductRepository, IProductCardMetadataRepository
{
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var result = await work();
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task AddAsync(Product product, CancellationToken ct) => await db.Products.AddAsync(product, ct);
    public Task<Product?> FindAsync(Guid id, CancellationToken ct) => db.Products.Include(x => x.Images).Include(x => x.Inspections).FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<Product?> FindByImageAsync(Guid imageId, CancellationToken ct) => db.Products.Include(x => x.Images).FirstOrDefaultAsync(x => x.Images.Any(i => i.Id == imageId), ct);
    public async Task<IReadOnlyList<Product>> ActiveAsync(ProductFilter filter, CancellationToken ct) =>
        await ActiveQuery(filter).Take(100).ToListAsync(ct);

    public async Task<ProductPage<Product>> ActivePageAsync(ProductFilter filter, int requestedPage, CancellationToken ct)
    {
        var query = ActiveQuery(filter);
        var totalCount = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (totalCount - 1) / ProductPage<Product>.PageSize + 1);
        var pageNumber = Math.Clamp(requestedPage, 1, totalPages);
        var items = await query.Skip((pageNumber - 1) * ProductPage<Product>.PageSize)
            .Take(ProductPage<Product>.PageSize).ToListAsync(ct);
        return new(items, pageNumber, totalCount);
    }

    private IQueryable<Product> ActiveQuery(ProductFilter f)
    {
        var q = db.Products.AsNoTracking().Include(x => x.Images).Where(x => x.Status == ProductStatus.Active);
        if (f.Ids is { } ids)
        {
            if (ids.Count == 0) return q.Where(x => false);
            var selectedIds = ids.Take(100).ToArray();
            q = q.Where(x => selectedIds.Contains(x.Id));
        }
        if (!string.IsNullOrWhiteSpace(f.SellerId)) q = q.Where(x => x.SellerId == f.SellerId);
        if (string.Equals(f.Fulfillment, "pickup", StringComparison.OrdinalIgnoreCase) && !shipping.CanPickup)
            return q.Where(x => false);
        if (!string.IsNullOrWhiteSpace(f.Fulfillment) &&
            !string.Equals(f.Fulfillment, "pickup", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(f.Fulfillment, "shipping", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Hình thức nhận hàng không hợp lệ.");
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            var skuSearch = s.ToUpperInvariant();
            var compactCode = skuSearch.Replace("-", "").Replace(" ", "");
            q = q.Where(x => x.Name.Contains(s) || x.Brand.Contains(s) ||
                x.Sku.Replace("-", "").Replace(" ", "").ToUpper().Contains(compactCode));
        }
        if (!string.IsNullOrWhiteSpace(f.Category))
        {
            var categories = ProductCategories.MatchingStoredValues(f.Category);
            q = q.Where(x => categories.Contains(x.Category));
        }
        if (!string.IsNullOrWhiteSpace(f.Brand)) q = q.Where(x => x.Brand == f.Brand);
        if (!string.IsNullOrWhiteSpace(f.Size)) q = q.Where(x => x.Size == f.Size);
        if (f.MinPrice.HasValue) q = q.Where(x => x.AskingPrice >= f.MinPrice.Value);
        if (f.MaxPrice.HasValue) q = q.Where(x => x.AskingPrice <= f.MaxPrice.Value);
        if (f.MinCondition.HasValue) q = q.Where(x => (x.VerifiedCondition ?? x.DeclaredCondition) >= f.MinCondition.Value);
        return f.Sort switch { "price_asc" => q.OrderBy(x => x.AskingPrice).ThenBy(x => x.Id),
            "price_desc" => q.OrderByDescending(x => x.AskingPrice).ThenBy(x => x.Id),
            "popular" => q.OrderByDescending(x => db.Favorites.Count(fav => fav.ProductId == x.Id))
                .ThenByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Id),
            _ => q.OrderByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Id) };
    }
    public async Task<IReadOnlyDictionary<Guid, ProductCardMetadata>> GetAsync(IReadOnlyCollection<Product> products, CancellationToken ct)
    {
        var ids = products.Select(x => x.Id).ToList();
        var sellerIds = products.Select(x => x.SellerId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => sellerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName, HasAvatar = x.AvatarStorageKey != null })
            .ToDictionaryAsync(x => x.Id, ct);
        var likes = await db.Favorites.AsNoTracking().Where(x => ids.Contains(x.ProductId))
            .GroupBy(x => x.ProductId).Select(x => new { ProductId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count, ct);
        return products.ToDictionary(x => x.Id, x => new ProductCardMetadata(
            names.TryGetValue(x.SellerId, out var seller) ? seller.FullName : "Người bán",
            likes.GetValueOrDefault(x.Id), names.TryGetValue(x.SellerId, out var user) && user.HasAvatar));
    }
    public async Task<IReadOnlyList<Product>> OwnedAsync(string userId, CancellationToken ct) =>
        await db.Products.AsNoTracking().Include(x => x.Images).Include(x => x.Inspections).Where(x => x.SellerId == userId && x.Status != ProductStatus.Removed).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    public async Task<IReadOnlyList<Product>> PendingAsync(string? search, CancellationToken ct)
    {
        var q = db.Products.AsNoTracking().Include(x => x.Images).Where(x => x.Status == ProductStatus.PendingApproval);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var code = s.Replace("-", "").Replace(" ", "").ToUpperInvariant();
            q = q.Where(x => x.ConsignmentCode.Replace("-", "").Replace(" ", "").ToUpper().Contains(code) ||
                x.Name.Contains(s) || db.Users.Any(u => u.Id == x.SellerId && u.FullName.Contains(s)));
        }
        return await q.OrderBy(x => x.CreatedAtUtc).Take(100).ToListAsync(ct);
    }
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task<IReadOnlyList<string>> InspectorUserIdsAsync(CancellationToken ct) =>
        await db.UserRoles.Join(db.Roles, ur => ur.RoleId, role => role.Id,
                (ur, role) => new { ur.UserId, role.Name })
            .Where(x => x.Name == "Inspector" || x.Name == "Admin")
            .Select(x => x.UserId).Distinct().ToListAsync(ct);
    public void AddNotification(MarketplaceNotification notification) => db.MarketplaceNotifications.Add(notification);
}
