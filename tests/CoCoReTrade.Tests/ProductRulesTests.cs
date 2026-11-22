using CoCoReTrade.Application;
using CoCoReTrade.Domain;

namespace CoCoReTrade.Tests;

public sealed class ProductRulesTests
{
    private static Product Pending(string? description = null, string? color = null) => Product.Submit("seller-1", "CC-2026-000001", "Áo linen", "Zara", "Áo", "M",
        500_000, 300_000, 95, true, [new("a.jpg", "image/jpeg", 0), new("b.jpg", "image/jpeg", 1), new("c.jpg", "image/jpeg", 2)], description, color);

    [Fact]
    public void OptionalDescriptionAndColorAreBoundedTrimmedAndAbsentForOldSubmissions()
    {
        var old = Pending();
        Assert.Null(old.Description);
        Assert.Null(old.Color);
        var product = Pending("  Cổ bẻ, khuy phía trước. Chất liệu chưa xác nhận.  ", "  Trắng  ");
        Assert.Equal("Cổ bẻ, khuy phía trước. Chất liệu chưa xác nhận.", product.Description);
        Assert.Equal("Trắng", product.Color);
        Assert.Null(Pending("  ", " ").Description);
        Assert.Null(Pending("  ", " ").Color);
        Assert.Throws<ArgumentException>(() => Pending(new string('a', 2001), null));
        Assert.Throws<ArgumentException>(() => Pending(null, new string('a', 81)));
    }

    [Fact]
    public void OldEditsPreserveOptionalDetailsAndExplicitEmptyEditsClearThem()
    {
        var product = Pending("Mô tả đã lưu", "Đen");
        void Edit(string? description = null, string? color = null) => product.EditBySeller("seller-1",
            product.Name, product.Brand, product.Category, product.Size, product.OriginalPrice,
            product.AskingPrice, product.DeclaredCondition, product.AllowOffers,
            product.Images.Select(image => image.Id).ToList(), [], description, color);
        Edit();
        Assert.Equal("Mô tả đã lưu", product.Description);
        Assert.Equal("Đen", product.Color);
        Assert.Throws<ArgumentException>(() => Edit(new string('x', 2001), "Hồng"));
        Assert.Equal("Mô tả đã lưu", product.Description);
        Assert.Equal("Đen", product.Color);
        Edit("", "");
        Assert.Null(product.Description);
        Assert.Null(product.Color);
    }

    [Fact]
    public async Task ProductServiceCarriesOptionalDetailsThroughSubmitEditAndVisibleDetails()
    {
        var product = Pending("Ban đầu", "Đen");
        var service = new ProductService(new FakeRepository(product), new FakeImageStore(), Codes());
        var uploads = Enumerable.Range(0, 3).Select(index => new ImageUpload($"{index}.jpg", "image/jpeg", 3,
            () => new MemoryStream([1, 2, 3]))).ToList();
        var submitted = await service.SubmitAsync("seller-1", new SubmitListing(product.Name, product.Brand,
            product.Category, product.Size, product.OriginalPrice, product.AskingPrice, 95, true,
            uploads, Description: "  Quan sát qua ảnh  ", Color: "  Trắng  "), default);
        Assert.Equal("Quan sát qua ảnh", submitted.Description);
        Assert.Equal("Trắng", submitted.Color);
        var edited = await service.EditAsync("seller-1", product.Id, new EditListing(product.Name, product.Brand,
            product.Category, product.Size, product.OriginalPrice, product.AskingPrice, 95, true,
            product.Images.Select(image => image.Id).ToList(), [], "", Description: "Đã cập nhật", Color: "Hồng"), default);
        Assert.Equal("Đã cập nhật", edited.Description);
        Assert.Equal("Hồng", edited.Color);
        var visible = await service.VisibleDetailsAsync(product.Id, "seller-1", false, default);
        Assert.Equal(edited.Description, visible!.Description);
        Assert.Equal(edited.Color, visible.Color);
    }

    [Fact]
    public void SubmissionRequiresThreeImages()
    {
        Assert.Throws<ArgumentException>(() => Product.Submit("seller-1", "CC-2026-000002", "Áo linen", "Zara", "Áo", "M",
            500_000, 300_000, 95, true, [new ProductImage("a.jpg", "image/jpeg", 0)]));
    }

    [Fact]
    public void PreliminaryReviewRequiresReasonAndPhysicalInspectionWaitsForApproval()
    {
        var product = Pending();
        Assert.Throws<InvalidOperationException>(() => product.RecordInspection("inspector-1", true,
            product.Name, product.Brand, product.Category, product.Size, product.OriginalPrice,
            product.AskingPrice, 95, null, [], DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => product.ReviewPreliminarily("admin", PreliminaryReviewStatus.ChangesRequested,
            "", DateTime.UtcNow));
        product.ReviewPreliminarily("admin", PreliminaryReviewStatus.ChangesRequested, "Cần chụp rõ nhãn và đường may.", DateTime.UtcNow);
        Assert.True(product.CanSellerEdit);
        Assert.Equal(PreliminaryReviewStatus.ChangesRequested, product.PreliminaryStatus);
        product.ReviewPreliminarily("admin", PreliminaryReviewStatus.Approved, null, DateTime.UtcNow);
        Assert.False(product.CanSellerEdit);
        Assert.Throws<InvalidOperationException>(() => product.EditBySeller("seller-1", product.Name, "", product.Category,
            product.Size, product.OriginalPrice, product.AskingPrice, 95, true,
            product.Images.Select(i => i.Id).ToList(), []));
    }

    [Fact]
    public void SellerMayEditPhotosAndLeaveBrandEmptyBeforePreliminaryApproval()
    {
        var product = Pending();
        var kept = product.Images.Skip(1).Select(i => i.Id).ToList();
        product.EditBySeller("seller-1", "Áo linen cập nhật", "", product.Category, product.Size,
            product.OriginalPrice, product.AskingPrice, 95, true, kept,
            [new ProductImage("d.jpg", "image/jpeg", 0)]);
        Assert.Empty(product.Brand);
        Assert.Equal(3, product.Images.Count);
        Assert.DoesNotContain(product.Images, i => i.StorageKey == "a.jpg");
        Assert.Throws<UnauthorizedAccessException>(() => product.EditBySeller("buyer", product.Name, "", product.Category,
            product.Size, product.OriginalPrice, product.AskingPrice, 95, true,
            product.Images.Select(i => i.Id).ToList(), []));
    }

    [Fact]
    public void OnlyPendingProductsCanBePublished()
    {
        var product = Pending();
        product.ReviewPreliminarily("admin", PreliminaryReviewStatus.Approved, null, DateTime.UtcNow);
        product.RecordInspection("inspector-1", true, "Áo linen", "Zara", "Áo", "M", 500_000, 280_000,
            90, "Đã kiểm tra", [], DateTime.UtcNow);
        product.Publish("CC-2026-000001", DateTime.UtcNow);
        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.Equal(90, product.VerifiedCondition);
        Assert.Single(product.Inspections);
        Assert.Throws<InvalidOperationException>(() => product.Publish("CC-2026-000002", DateTime.UtcNow));
    }

    [Fact]
    public async Task PendingProductAndImageAreOnlyVisibleToOwnerOrAdmin()
    {
        var product = Pending();
        var service = new ProductService(new FakeRepository(product), new FakeImageStore(), Codes());
        Assert.Null(await service.VisibleDetailsAsync(product.Id, null, false, default));
        Assert.Null(await service.VisibleDetailsAsync(product.Id, "buyer-1", false, default));
        Assert.Null(await service.VisibleImageAsync(product.Images[0].Id, "buyer-1", false, false, default));
        Assert.NotNull(await service.VisibleImageAsync(product.Images[0].Id, "inspector-1", true, false, default));
        Assert.NotNull(await service.VisibleDetailsAsync(product.Id, "seller-1", false, default));
        Assert.NotNull(await service.VisibleDetailsAsync(product.Id, "admin-1", true, default));
        Assert.Empty(await service.ShopAsync(new ProductFilter(), default));
    }

    [Fact]
    public async Task InspectorCannotReadRejectedImages()
    {
        var product = Pending();
        product.ReviewPreliminarily("admin", PreliminaryReviewStatus.Approved, null, DateTime.UtcNow);
        product.RecordInspection("inspector-1", false, product.Name, product.Brand, product.Category,
            product.Size, product.OriginalPrice, product.AskingPrice, 90, "Damaged fabric", [], DateTime.UtcNow);
        product.Reject("Damaged fabric");
        var service = new ProductService(new FakeRepository(product), new FakeImageStore(), Codes());
        Assert.Null(await service.VisibleImageAsync(product.Images[0].Id, "inspector-1", true, false, default));
        Assert.NotNull(await service.VisibleImageAsync(product.Images[0].Id, "admin-1", true, true, default));
    }

    [Fact]
    public async Task PublishedProductIsVisibleInPublicShop()
    {
        var product = Pending();
        product.ReviewPreliminarily("admin", PreliminaryReviewStatus.Approved, null, DateTime.UtcNow);
        product.RecordInspection("inspector-1", true, "Áo linen", "Zara", "Áo", "M", 500_000, 300_000,
            95, "Đạt kiểm định", [], DateTime.UtcNow);
        product.Publish("CC-2026-000001", DateTime.UtcNow);
        var service = new ProductService(new FakeRepository(product), new FakeImageStore(), Codes());
        Assert.Single(await service.ShopAsync(new ProductFilter(), default));
        Assert.NotNull(await service.VisibleDetailsAsync(product.Id, null, false, default));
    }

    private static IProductCodeService Codes() => new ProductCodeService(new FakeSequence(), TimeProvider.System);
    private sealed class FakeSequence : IProductCodeSequence
    {
        public Task<long> NextAsync(CancellationToken ct) => Task.FromResult(1L);
    }
    private sealed class FakeRepository(Product product) : IProductRepository
    {
        public Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) => work();
        public Task AddAsync(Product item, CancellationToken ct) => Task.CompletedTask;
        public Task<Product?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Product?>(id == product.Id ? product : null);
        public Task<Product?> FindByImageAsync(Guid id, CancellationToken ct) => Task.FromResult<Product?>(product.Images.Any(i => i.Id == id) ? product : null);
        public Task<IReadOnlyList<Product>> ActiveAsync(ProductFilter filter, CancellationToken ct) => Task.FromResult<IReadOnlyList<Product>>(product.Status == ProductStatus.Active ? [product] : []);
        public async Task<ProductPage<Product>> ActivePageAsync(ProductFilter filter, int pageNumber, CancellationToken ct) =>
            ProductPage<Product>.From(await ActiveAsync(filter, ct), pageNumber);
        public Task<IReadOnlyList<Product>> OwnedAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Product>>(product.SellerId == userId ? [product] : []);
        public Task<IReadOnlyList<Product>> PendingAsync(string? search, CancellationToken ct) => Task.FromResult<IReadOnlyList<Product>>(product.Status == ProductStatus.PendingApproval ? [product] : []);
        public Task<IReadOnlyList<string>> InspectorUserIdsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
        public void AddNotification(MarketplaceNotification notification) { }
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class FakeImageStore : IPrivateImageStore
    {
        public Task<(string Key, string ContentType)> SaveAsync(ImageUpload image, CancellationToken ct) => Task.FromResult(("test.jpg", "image/jpeg"));
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream> OpenAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
    }
}
