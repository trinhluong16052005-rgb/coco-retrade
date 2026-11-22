using CoCoReTrade.Domain;

namespace CoCoReTrade.Application;

public sealed record ImageUpload(string FileName, string ContentType, long Length, Func<Stream> OpenReadStream);
public sealed record SubmitListing(string Name, string Brand, string Category, string Size, decimal OriginalPrice,
    decimal AskingPrice, int ConditionPercent, bool AllowOffers, IReadOnlyCollection<ImageUpload> Images,
    string? Description = null, string? Color = null);
public sealed record EditListing(string Name, string Brand, string Category, string Size, decimal OriginalPrice,
    decimal AskingPrice, int ConditionPercent, bool AllowOffers, IReadOnlyCollection<Guid> RetainedImageIds,
    IReadOnlyCollection<ImageUpload> AddedImages, string ExpectedRowVersion, string? Description = null, string? Color = null);
public sealed record ProductFilter(string? Search = null, string? Category = null, string? Brand = null, string? Size = null,
    decimal? MinPrice = null, decimal? MaxPrice = null, int? MinCondition = null, string Sort = "newest",
    string? Fulfillment = null, string? SellerId = null, IReadOnlyCollection<Guid>? Ids = null);
public sealed record ProductCard(Guid Id, string Sku, string Name, string Brand, string Category, string Size,
    decimal AskingPrice, int ConditionPercent, ProductStatus Status, Guid? CoverImageId, string SellerId,
    string ConsignmentCode = "", string SellerName = "", int LikeCount = 0, bool SellerHasAvatar = false,
    PreliminaryReviewStatus PreliminaryStatus = PreliminaryReviewStatus.Pending, string? PreliminaryReason = null,
    bool CanRemoveFromCloset = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? SecondaryImageId { get; init; }
}
public sealed record ProductCardMetadata(string SellerName, int LikeCount, bool SellerHasAvatar);

public sealed record ProductPage<T>(IReadOnlyList<T> Items, int PageNumber, int TotalCount)
{
    public const int PageSize = 10;
    public int TotalPages => Math.Max(1, (TotalCount - 1) / PageSize + 1);

    public static ProductPage<T> From(IReadOnlyList<T> items, int requestedPage)
    {
        var totalPages = Math.Max(1, (items.Count - 1) / PageSize + 1);
        var pageNumber = Math.Clamp(requestedPage, 1, totalPages);
        return new(items.Skip((pageNumber - 1) * PageSize).Take(PageSize).ToArray(), pageNumber, items.Count);
    }
}

public interface IProductCardMetadataRepository
{
    Task<IReadOnlyDictionary<Guid, ProductCardMetadata>> GetAsync(IReadOnlyCollection<Product> products, CancellationToken ct);
}
public sealed record ProductDetails(Guid Id, string Sku, string Name, string Brand, string Category, string Size,
    decimal OriginalPrice, decimal AskingPrice, int DeclaredCondition, int? VerifiedCondition, bool AllowOffers,
    ProductStatus Status, string SellerId, string? RejectionReason, IReadOnlyList<Guid> ImageIds, DateTime CreatedAtUtc,
    string ConsignmentCode = "", string SellerName = "", PreliminaryReviewStatus PreliminaryStatus = PreliminaryReviewStatus.Pending,
    string? PreliminaryReason = null, string RowVersion = "", DateTime? PublishedAtUtc = null,
    string? Description = null, string? Color = null);

public interface IProductRepository
{
    Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken);
    Task AddAsync(Product product, CancellationToken cancellationToken);
    Task<Product?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<Product?> FindByImageAsync(Guid imageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> ActiveAsync(ProductFilter filter, CancellationToken cancellationToken);
    Task<ProductPage<Product>> ActivePageAsync(ProductFilter filter, int pageNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> OwnedAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> PendingAsync(string? search, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> InspectorUserIdsAsync(CancellationToken cancellationToken);
    void AddNotification(MarketplaceNotification notification);
    Task SaveAsync(CancellationToken cancellationToken);
}

public interface IPrivateImageStore
{
    Task<(string Key, string ContentType)> SaveAsync(ImageUpload image, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    Task<Stream> OpenAsync(string key, CancellationToken cancellationToken);
}

public interface IProductService
{
    Task<IReadOnlyList<Guid>> RemoveFromClosetAsync(string sellerId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<ProductDetails> SubmitAsync(string sellerId, SubmitListing request, CancellationToken cancellationToken);
    Task<ProductDetails> EditAsync(string sellerId, Guid id, EditListing request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductCard>> ShopAsync(ProductFilter filter, CancellationToken cancellationToken);
    Task<ProductPage<ProductCard>> ShopPageAsync(ProductFilter filter, int pageNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductCard>> ClosetAsync(string sellerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductCard>> PendingAsync(string? search, CancellationToken cancellationToken);
    Task<ProductDetails?> VisibleDetailsAsync(Guid id, string? viewerId, bool isAdmin, CancellationToken cancellationToken);
    Task<(Stream Stream, string ContentType)?> VisibleImageAsync(Guid imageId, string? viewerId,
        bool canInspectPending, bool canManageAll, CancellationToken cancellationToken);
}

public sealed class ProductService(IProductRepository repository, IPrivateImageStore images,
    IProductCodeService codes, IProductCardMetadataRepository? metadata = null) : IProductService
{
    public Task<IReadOnlyList<Guid>> RemoveFromClosetAsync(string sellerId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sellerId)) throw new UnauthorizedAccessException("Hãy đăng nhập.");
        var selected = ids.Distinct().Order().ToArray();
        if (selected.Length is < 1 or > 500 || selected.Contains(Guid.Empty))
            throw new ArgumentException("Chọn từ 1 đến 500 sản phẩm để xóa khỏi tủ.");
        return repository.InTransactionAsync<IReadOnlyList<Guid>>(async () =>
        {
            var products = new List<Product>();
            foreach (var id in selected)
            {
                var product = await repository.FindAsync(id, ct) ?? throw new KeyNotFoundException("Không tìm thấy món đồ. Hãy tải lại tủ đồ.");
                if (product.SellerId != sellerId) throw new UnauthorizedAccessException("Bạn không sở hữu sản phẩm này.");
                // A repeated request is safe, but every other item must still pass the current server-side rule.
                if (product.Status == ProductStatus.Removed) continue;
                if (!product.CanSellerRemove)
                    throw new InvalidOperationException("Có món đã kiểm định hoặc đăng bán. Chưa xóa món nào; hãy tải lại tủ đồ.");
                products.Add(product);
            }
            foreach (var product in products) product.RemoveBySeller(sellerId);
            await repository.SaveAsync(ct);
            return selected;
        }, ct);
    }

    public async Task<ProductDetails> SubmitAsync(string sellerId, SubmitListing request, CancellationToken ct)
    {
        request = request with { Category = ProductCategories.Normalize(request.Category), Size = ProductSizing.Normalize(request.Category, request.Size) };
        if (request.Images.Count is < 3 or > 10) throw new ArgumentException("Tải từ 3 đến 10 ảnh sản phẩm.");
        var keys = new List<string>();
        try
        {
            var productImages = new List<ProductImage>();
            foreach (var image in request.Images)
            {
                var (key, type) = await images.SaveAsync(image, ct);
                keys.Add(key);
                productImages.Add(new ProductImage(key, type, productImages.Count));
            }
            var code = await codes.NewConsignmentCodeAsync(ct);
            var product = Product.Submit(sellerId, code, request.Name, request.Brand, request.Category, request.Size,
                request.OriginalPrice, request.AskingPrice, request.ConditionPercent, request.AllowOffers, productImages,
                request.Description, request.Color);
            await repository.AddAsync(product, ct);
            foreach (var inspectorId in await repository.InspectorUserIdsAsync(ct))
                if (inspectorId != sellerId) repository.AddNotification(new MarketplaceNotification
                {
                    Id = Guid.NewGuid(), UserId = inspectorId, Category = "Listings", ProductId = product.Id,
                    Message = $"Ký gửi mới ({product.Name}) {code} đang chờ duyệt sơ bộ.", CreatedAtUtc = DateTime.UtcNow
                });
            await repository.SaveAsync(ct);
            return ToDetails(product);
        }
        catch
        {
            foreach (var key in keys) await images.DeleteAsync(key, CancellationToken.None);
            throw;
        }
    }

    public async Task<ProductDetails> EditAsync(string sellerId, Guid id, EditListing request, CancellationToken ct)
    {
        var product = await repository.FindAsync(id, ct) ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
        if (product.SellerId != sellerId) throw new UnauthorizedAccessException("Bạn không sở hữu sản phẩm này.");
        request = request with { Category = ProductCategories.Normalize(request.Category) };
        if (ProductSizing.IsOneSize(request.Category) || request.Category != product.Category || request.Size != product.Size)
            request = request with { Size = ProductSizing.Normalize(request.Category, request.Size) };
        if (!product.CanSellerEdit) throw new InvalidOperationException("Sản phẩm đã duyệt sơ bộ, không thể chỉnh sửa.");
        if (request.AddedImages.Count > 10 || request.RetainedImageIds.Count + request.AddedImages.Count is < 3 or > 10)
            throw new ArgumentException("Cần giữ từ 3 đến 10 ảnh sản phẩm.");
        if (request.ExpectedRowVersion != Convert.ToBase64String(product.RowVersion))
            throw new InvalidOperationException("Sản phẩm đã thay đổi. Hãy tải lại trang trước khi sửa.");
        var retained = request.RetainedImageIds.ToHashSet();
        var removedKeys = product.Images.Where(x => !retained.Contains(x.Id)).Select(x => x.StorageKey).ToList();
        var newKeys = new List<string>();
        var saved = false;
        try
        {
            var added = new List<ProductImage>();
            foreach (var upload in request.AddedImages)
            {
                var (key, type) = await images.SaveAsync(upload, ct);
                newKeys.Add(key);
                added.Add(new ProductImage(key, type, 0));
            }
            product.EditBySeller(sellerId, request.Name, request.Brand, request.Category, request.Size,
                request.OriginalPrice, request.AskingPrice, request.ConditionPercent, request.AllowOffers,
                request.RetainedImageIds, added, request.Description, request.Color);
            foreach (var inspectorId in await repository.InspectorUserIdsAsync(ct))
                if (inspectorId != sellerId) repository.AddNotification(new MarketplaceNotification
                {
                    Id = Guid.NewGuid(), UserId = inspectorId, Category = "Listings", ProductId = product.Id,
                    Message = $"Ký gửi ({product.Name}) {product.ConsignmentCode} đã được người bán cập nhật, đang chờ duyệt sơ bộ lại.",
                    CreatedAtUtc = DateTime.UtcNow
                });
            await repository.SaveAsync(ct);
            saved = true;
            foreach (var key in removedKeys) await images.DeleteAsync(key, ct);
            return ToDetails(product);
        }
        catch
        {
            if (!saved) foreach (var key in newKeys) await images.DeleteAsync(key, CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProductCard>> ShopAsync(ProductFilter filter, CancellationToken ct) =>
        await CardsAsync(await repository.ActiveAsync(filter, ct), ct);
    public async Task<ProductPage<ProductCard>> ShopPageAsync(ProductFilter filter, int pageNumber, CancellationToken ct)
    {
        var page = await repository.ActivePageAsync(filter, pageNumber, ct);
        return new(await CardsAsync(page.Items, ct), page.PageNumber, page.TotalCount);
    }
    public async Task<IReadOnlyList<ProductCard>> ClosetAsync(string sellerId, CancellationToken ct) =>
        await CardsAsync(await repository.OwnedAsync(sellerId, ct), ct);
    public async Task<IReadOnlyList<ProductCard>> PendingAsync(string? search, CancellationToken ct) =>
        await CardsAsync(await repository.PendingAsync(search, ct), ct);
    private async Task<IReadOnlyList<ProductCard>> CardsAsync(IReadOnlyList<Product> products, CancellationToken ct)
    {
        if (metadata is null) return products.Select(ToCard).ToList();
        var values = await metadata.GetAsync(products, ct);
        return products.Select(p => values.TryGetValue(p.Id, out var value)
            ? ToCard(p) with { SellerName = value.SellerName, LikeCount = value.LikeCount,
                SellerHasAvatar = value.SellerHasAvatar }
            : ToCard(p)).ToList();
    }
    public async Task<ProductDetails?> VisibleDetailsAsync(Guid id, string? viewerId, bool isAdmin, CancellationToken ct)
    {
        var product = await repository.FindAsync(id, ct);
        if (product is null || !CanView(product, viewerId, isAdmin)) return null;
        var details = ToDetails(product);
        if (metadata is null) return details;
        var values = await metadata.GetAsync([product], ct);
        return values.TryGetValue(product.Id, out var value)
            ? details with { SellerName = value.SellerName } : details;
    }
    public async Task<(Stream Stream, string ContentType)?> VisibleImageAsync(Guid imageId, string? viewerId,
        bool canInspectPending, bool canManageAll, CancellationToken ct)
    {
        // Repository lookup is deliberately by image owner/product, never by a caller-provided storage path.
        var product = await repository.FindByImageAsync(imageId, ct);
        var image = product?.Images.FirstOrDefault(x => x.Id == imageId);
        if (product is null || image is null ||
            !(CanView(product, viewerId, canManageAll) || canInspectPending && product.Status == ProductStatus.PendingApproval)) return null;
        try
        {
            return (await images.OpenAsync(image.StorageKey, ct), image.ContentType);
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
    private static bool CanView(Product p, string? viewerId, bool isAdmin) =>
        p.Status == ProductStatus.Active || isAdmin || (viewerId is not null && p.SellerId == viewerId);
    private static ProductCard ToCard(Product p)
    {
        var photos = p.Images.OrderBy(i => i.SortOrder).Take(2).ToArray();
        return new ProductCard(p.Id, p.Sku, p.Name, p.Brand, p.Category, p.Size,
            p.AskingPrice, p.VerifiedCondition ?? p.DeclaredCondition, p.Status, photos.FirstOrDefault()?.Id, p.SellerId,
            p.Status == ProductStatus.Active ? string.Empty : p.ConsignmentCode, PreliminaryStatus: p.PreliminaryStatus,
            PreliminaryReason: p.PreliminaryReason, CanRemoveFromCloset: p.CanSellerRemove)
        {
            SecondaryImageId = photos.ElementAtOrDefault(1)?.Id
        };
    }
    private static ProductDetails ToDetails(Product p) => new(p.Id, p.Sku, p.Name, p.Brand, p.Category, p.Size,
        p.OriginalPrice, p.AskingPrice, p.DeclaredCondition, p.VerifiedCondition, p.AllowOffers, p.Status, p.SellerId,
        p.RejectionReason, p.Images.OrderBy(i => i.SortOrder).Select(i => i.Id).ToList(), p.CreatedAtUtc,
        p.Status == ProductStatus.Active ? string.Empty : p.ConsignmentCode, PreliminaryStatus: p.PreliminaryStatus,
        PreliminaryReason: p.PreliminaryReason, RowVersion: Convert.ToBase64String(p.RowVersion),
        PublishedAtUtc: p.PublishedAtUtc, Description: p.Description, Color: p.Color);
}
