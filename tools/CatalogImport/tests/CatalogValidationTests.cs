using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using SkiaSharp;
using System.Text.Json;

namespace CoCoReTrade.CatalogImport.Tests;

public sealed class CatalogValidationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "coco-import-tests-" + Guid.NewGuid().ToString("N"));
    public CatalogValidationTests() => Directory.CreateDirectory(root);

    private CatalogRecord Record(string id = "item-1", string url = "https://www.depop.com/products/one/")
    {
        var images = new List<CatalogImage>();
        for (var index = 0; index < 3; index++)
        {
            var path = Path.Combine(root, $"{id}-{index}.png");
            using var image = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
                image.SetPixel(x, y, new SKColor((byte)(x * 3 + index), (byte)(y * 3), (byte)(index * 70), 255));
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var file = File.Create(path)) data.SaveTo(file);
            images.Add(new CatalogImage(path, $"https://example.com/{id}-{index}.png"));
        }
        return new CatalogRecord(id, url, "Áo sơ mi cotton", "Zara", "Áo", "M", 450000, 300000, 95, true, images);
    }

    private PreparedManifest Prepare(params CatalogRecord[] records) =>
        CatalogValidation.Prepare(new CatalogManifest(1, "manifest-1", records), root);

    [Theory]
    [InlineData("Áo", "M")]
    [InlineData("Quần", "M")]
    [InlineData("Đầm/Váy", "M")]
    [InlineData("Set đồ", "M")]
    [InlineData("Phụ kiện", "Free size")]
    [InlineData("Túi", "Free size")]
    [InlineData("Túi xách", "Free size")]
    public void CurrentAndLegacyCategoriesKeepOriginalSourceAndFingerprint(string category, string size)
    {
        var source = Record() with { Category = category, Size = size };
        var prepared = Prepare(source).Items.Single();
        Assert.Same(source, prepared.Source);
        Assert.Equal(category, prepared.Source.Category);
        Assert.Equal(CatalogValidation.Fingerprint(prepared.CanonicalSourceUrl, source.Name, source.Brand,
            category, size, source.OriginalPriceVnd, source.AskingPriceVnd, source.ConditionPercent,
            source.AllowOffers, prepared.Images.Select(image => image.Sha256)), prepared.Fingerprint);
    }

    [Fact]
    public void UnknownCategoryFailsBeforeSql() =>
        Assert.Throws<ArgumentException>(() => Prepare(Record() with { Category = "Không tồn tại" }));

    [Theory]
    [InlineData("Túi")]
    [InlineData("Túi xách")]
    public void RecoveryAcceptsMergedAccessoryCategoryWithoutChangingLegacyReceipt(string category)
    {
        var prepared = Prepare(Record() with { Category = category, Size = "Free size" }).Items.Single();
        var source = prepared.Source;
        var stored = Product.Submit("seller-1", prepared.ConsignmentCode, source.Name, source.Brand,
            ProductCategories.Accessories, source.Size, source.OriginalPriceVnd, source.AskingPriceVnd,
            source.ConditionPercent, source.AllowOffers,
            Enumerable.Range(0, 3).Select(i => new ProductImage($"{i}.jpg", "image/jpeg", i)).ToArray());
        ImportRunner.RequireMatchingStoredCore(prepared, stored, prepared.Images.Select(image => image.Sha256));
        Assert.Equal(category, prepared.Source.Category);
        Assert.Equal(ProductCategories.Accessories, stored.Category);
    }

    [Theory]
    [InlineData("category")]
    [InlineData("name")]
    [InlineData("brand")]
    [InlineData("size")]
    [InlineData("original")]
    [InlineData("asking")]
    [InlineData("condition")]
    [InlineData("offers")]
    [InlineData("images")]
    public void RecoveryStillRejectsChangedCategoryCoreDataOrImages(string change)
    {
        var prepared = Prepare(Record() with { Category = "Túi", Size = "Free size" }).Items.Single();
        var source = prepared.Source;
        var stored = Product.Submit("seller-1", prepared.ConsignmentCode,
            change == "name" ? "Tên khác" : source.Name, change == "brand" ? "Thương hiệu khác" : source.Brand,
            change == "category" ? ProductCategories.Tops : ProductCategories.Accessories,
            change == "size" ? "M" : source.Size,
            change == "original" ? source.OriginalPriceVnd + 1000 : source.OriginalPriceVnd,
            change == "asking" ? source.AskingPriceVnd - 1000 : source.AskingPriceVnd,
            change == "condition" ? 90 : source.ConditionPercent,
            change == "offers" ? !source.AllowOffers : source.AllowOffers,
            Enumerable.Range(0, 3).Select(i => new ProductImage($"{i}.jpg", "image/jpeg", i)).ToArray());
        var hashes = prepared.Images.Select(image => image.Sha256).ToArray();
        if (change == "images") Array.Reverse(hashes);
        Assert.Throws<ArgumentException>(() => ImportRunner.RequireMatchingStoredCore(prepared, stored, hashes));
    }

    [Fact]
    public void ValidRecordHasStableSourceCodeDespiteTrackingAndRecordRename()
    {
        var record = Record();
        var one = Prepare(record).Items.Single();
        var two = Prepare(record with { Id = "renamed", SourceUrl = "https://www.depop.com/products/one?utm_source=test#photos" }).Items.Single();
        Assert.Equal(one.ConsignmentCode, two.ConsignmentCode);
        Assert.Equal(one.Fingerprint, two.Fingerprint);
        Assert.StartsWith("CI-", one.ConsignmentCode);
        Assert.Equal(23, one.ConsignmentCode.Length);
    }

    [Fact]
    public void HttpIdentityAndFingerprintRemainCompatibleWithLegacyReceipts()
    {
        const string source = "https://www.depop.com/products/one";
        Assert.Equal(source, CatalogValidation.CanonicalUrl("https://WWW.DEPOP.COM/products/one/?utm_source=test#photos"));
        Assert.Equal("http://example.com/item?color=red&size=M",
            CatalogValidation.CanonicalUrl("http://EXAMPLE.com/item/?size=M&fbclid=tracking&color=red"));
        Assert.Equal("CI-a6f84b0f58773292353b", Prepare(Record()).Items.Single().ConsignmentCode);
        // Existing receipts use System.Text.Json's Windows CRLF indentation.
        Assert.Equal("dd13de9ba1599cc7ccf2df2f684a640b8b01c8e594f5d5f1b7fadf113358b29f",
            CatalogValidation.Fingerprint(source, "Shirt", "Zara", "Top", "M", 450000, 300000, 95, true,
                ["front", "back", "label"]));
    }

    [Fact]
    public void WindowsFileSourceCanonicalizesUnicodeEncodingAndCase()
    {
        const string expected = "file:///D:/D%E1%BB%B0%20%C3%81N/%C3%81O%2001.JPG";
        Assert.Equal(expected, CatalogValidation.CanonicalUrl("file:///d:/Dự án/Áo 01.jpg"));
        Assert.Equal(expected, CatalogValidation.CanonicalUrl("file:///D:/D%E1%BB%B1%20%C3%A1n/%C3%81o%2001.jpg"));
        Assert.Equal(expected, CatalogValidation.CanonicalUrl("FILE:///d:/DỰ ÁN/ÁO 01.JPG"));
    }

    [Fact]
    public void LocalProvenanceIsPreservedWithoutFetchingSourceUris()
    {
        var record = Record();
        var originals = record.Images.Select((image, index) => image with
            { SourceUrl = $"file:///D:/DỰ ÁN/Áo 01 ({index + 1}).jpg" }).ToArray();
        record = record with { SourceUrl = originals[0].SourceUrl, Images = originals };
        var prepared = Prepare(record).Items.Single();
        var encoded = Prepare(record with { SourceUrl = CatalogValidation.CanonicalUrl(record.SourceUrl) }).Items.Single();
        Assert.Equal(prepared.ConsignmentCode, encoded.ConsignmentCode);
        Assert.Equal(prepared.Fingerprint, encoded.Fingerprint);
        Assert.All(prepared.Images, image => Assert.StartsWith("file:///D:/", image.SourceUrl));
        var ledger = new ImportLedger();
        ledger.Record("manifest-1", prepared, Guid.NewGuid(), DateTime.UtcNow, "seller-1");
        Assert.Equal(originals, ledger.Entries.Single().Source.Images);
        Assert.NotNull(ledger.Find("manifest-1", encoded));
        Assert.Equal(3, CatalogValidation.ReadUploads(prepared).Count);
    }

    [Fact]
    public void RepeatedPhotoUnderAnotherNameFails()
    {
        var record = Record();
        var copy = Path.Combine(root, "renamed.png");
        File.Copy(record.Images[0].File, copy);
        var images = record.Images.ToArray();
        images[2] = images[2] with { File = copy };
        Assert.Throws<ArgumentException>(() => Prepare(record with { Images = images }));
    }

    [Fact]
    public void DuplicatePixelsWithDifferentFileBytesFails()
    {
        var record = Record();
        var copy = Path.Combine(root, "renamed.png");
        File.Copy(record.Images[0].File, copy);
        File.AppendAllText(copy, "different file bytes; same decoded pixels");
        var images = record.Images.ToArray();
        images[2] = images[2] with { File = copy };
        Assert.Throws<ArgumentException>(() => Prepare(record with { Images = images }));
    }

    [Fact]
    public void FewerThanThreePhotosFails() => Assert.Throws<ArgumentException>(() =>
        Prepare(Record() with { Images = Record().Images.Take(2).ToList() }));

    [Fact]
    public void CorruptOrMislabeledPhotoFails()
    {
        var record = Record();
        var wrong = Path.Combine(root, "wrong.jpg");
        File.Copy(record.Images[0].File, wrong);
        var images = record.Images.ToArray(); images[2] = images[2] with { File = wrong };
        Assert.Throws<ArgumentException>(() => Prepare(record with { Images = images }));
    }

    [Fact]
    public void SourceDuplicatesAndRecordDuplicatesFailBeforeSql()
    {
        var record = Record();
        Assert.Throws<ArgumentException>(() => Prepare(record, record with { Id = "other" }));
        Assert.Throws<ArgumentException>(() => Prepare(record, record with { SourceUrl = "https://www.depop.com/products/two" }));
    }

    [Theory]
    [InlineData("https://name:password@example.com/product")]
    [InlineData("file:///tmp/p.png")]
    [InlineData("/products/one")]
    [InlineData("file://server/share/photo.jpg")]
    [InlineData("file://localhost/D:/photo.jpg")]
    [InlineData("file://user:password@server/share/photo.jpg")]
    [InlineData("file:////server/share/photo.jpg")]
    [InlineData("file:///D:photo.jpg")]
    [InlineData("file:///D|/photo.jpg")]
    [InlineData("D:\\photo.jpg")]
    [InlineData("file:///D:/")]
    [InlineData("file:///D:/photos/")]
    [InlineData("file:///D:/photo.jpg?key=value")]
    [InlineData("file:///D:/photo.jpg?")]
    [InlineData("file:///D:/photo.jpg#front")]
    [InlineData("file:///D:/photo.jpg#")]
    [InlineData("file:////?/D:/photo.jpg")]
    [InlineData("file:////./D:/photo.jpg")]
    [InlineData("file:///D:/NUL")]
    [InlineData("file:///D:/con.jpg")]
    [InlineData("file:///D:/COM1/photo.jpg")]
    [InlineData("file:///D:/LPT%C2%B9.jpg")]
    [InlineData("file:///D:/photo.jpg%3Astream")]
    [InlineData("file:///D:/photo%00.jpg")]
    [InlineData("file:///D:/photo%3F.jpg")]
    [InlineData("ftp://example.com/photo.jpg")]
    public void UnsafeOrRelativeSourcesFail(string source) => Assert.Throws<ArgumentException>(() => CatalogValidation.CanonicalUrl(source));

    [Fact]
    public void ChangedImageAfterValidationFailsBeforeSubmission()
    {
        var record = Prepare(Record()).Items.Single();
        File.AppendAllText(record.Images[0].FilePath, "changed");
        Assert.Throws<ArgumentException>(() => CatalogValidation.ReadUploads(record));
    }

    [Fact]
    public void ReceiptRejectsReusedRecordAndChangedData()
    {
        var record = Record();
        var prepared = Prepare(record).Items.Single();
        var ledger = new ImportLedger();
        ledger.Record("manifest-1", prepared, Guid.NewGuid(), DateTime.UtcNow, "seller-1");
        Assert.NotNull(ledger.Find("manifest-1", prepared));
        var changed = Prepare(record with { AskingPriceVnd = 290000 }).Items.Single();
        Assert.Throws<ArgumentException>(() => ledger.Find("manifest-1", changed));
        var changedSource = Prepare(record with { SourceUrl = "https://www.depop.com/products/new-source" }).Items.Single();
        Assert.Throws<ArgumentException>(() => ledger.Find("manifest-1", changedSource));
        var alias = Prepare(record with { Id = "alias" }).Items.Single();
        Assert.NotNull(ledger.Find("manifest-2", alias));
    }

    [Theory]
    [InlineData("Mô tả khác", "Trắng")]
    [InlineData("Áo cotton, cần kiểm định.", "Đen")]
    public void ReceiptRejectsChangedDetailsWithoutChangingLegacyFingerprint(string description, string color)
    {
        var source = Record() with { Description = "Áo cotton, cần kiểm định.", Color = "Trắng" };
        var prepared = Prepare(source).Items.Single();
        var ledger = new ImportLedger();
        ledger.Record("manifest-1", prepared, Guid.NewGuid(), DateTime.UtcNow, "seller-1");
        var changed = Prepare(source with { Description = description, Color = color }).Items.Single();
        Assert.Equal(prepared.Fingerprint, changed.Fingerprint);
        Assert.Throws<ArgumentException>(() => ledger.Find("manifest-1", changed));
        Assert.Throws<ArgumentException>(() => ledger.Find("manifest-2", changed with
            { Source = changed.Source with { Id = "alias" } }));
        Assert.Throws<ArgumentException>(() => ledger.Record("manifest-1", changed,
            ledger.Entries.Single().ProductId, DateTime.UtcNow, "seller-1"));
    }

    [Fact]
    public void DetailNormalizationMatchesDomainStorage()
    {
        var source = Record() with { Description = "  Áo cotton. \r\n", Color = " Trắng " };
        var prepared = Prepare(source).Items.Single();
        var ledger = new ImportLedger();
        ledger.Record("manifest-1", prepared, Guid.NewGuid(), DateTime.UtcNow, "seller-1");
        Assert.NotNull(ledger.Find("manifest-1", Prepare(source with
            { Description = "Áo cotton.", Color = "Trắng" }).Items.Single()));
        Assert.True(CatalogValidation.DetailsMatch(source, "Áo cotton.", "Trắng"));
        Assert.True(CatalogValidation.DetailsMatch(source with { Description = null, Color = "  " }, "\r\n", null));
        Assert.False(CatalogValidation.DetailsMatch(source, "áo cotton.", "Trắng"));
    }

    [Theory]
    [InlineData(2001, 80)]
    [InlineData(2000, 81)]
    public void OversizedDetailsFailManifestValidationBeforeSql(int descriptionLength, int colorLength)
    {
        var source = Record() with { Description = new string('x', descriptionLength), Color = new string('y', colorLength) };
        Assert.Throws<ArgumentException>(() => Prepare(source));
    }

    [Fact]
    public void DetailLimitsApplyAfterTrimming()
    {
        var source = Record() with { Description = " " + new string('x', 2000) + " ", Color = " " + new string('y', 80) + " " };
        Assert.Single(Prepare(source).Items);
        Assert.Single(Prepare(source with { Description = null, Color = null }).Items);
    }

    [Fact]
    public void LedgerRoundTripAndTargetGuard()
    {
        var path = Path.Combine(root, "results.json");
        var ledger = ImportLedger.Load(path, "LOCAL\\SQLEXPRESS", "CoCoReTrade", root);
        Assert.False(File.Exists(path));
        ledger.Record("manifest-1", Prepare(Record()).Items.Single(), Guid.NewGuid(), DateTime.UtcNow, "seller-1");
        Assert.True(ledger.Entries.Single().DetailsStored);
        ledger.Save(path);
        Assert.True(Assert.Single(ImportLedger.Load(path, "LOCAL\\SQLEXPRESS", "CoCoReTrade", root).Entries).DetailsStored);
        Assert.Throws<ArgumentException>(() => ImportLedger.Load(path, "LOCAL\\SQLEXPRESS", "CoCoReTradeDemo", root));
    }

    [Fact]
    public void LegacyHttpReceiptLoadsFindsAndResavesWithoutSchemaOrIdentityChanges()
    {
        var path = Path.Combine(root, "legacy-results.json");
        var prepared = Prepare(Record() with { Description = "Legacy description in receipt only.", Color = "Trắng" }).Items.Single();
        var productId = Guid.NewGuid();
        // Explicit v1 shape predates file-URI support; no new provenance fields are required.
        var legacy = new
        {
            schemaVersion = 1, server = "LOCAL\\SQLEXPRESS", database = "CoCoReTrade", storageRoot = root,
            entries = new[] { new
            {
                manifestId = "manifest-1", recordId = "item-1", sourceUrl = "https://www.depop.com/products/one",
                fingerprint = prepared.Fingerprint, productId, consignmentCode = "CI-a6f84b0f58773292353b",
                importedAtUtc = DateTime.Parse("2026-11-17T00:00:00Z").ToUniversalTime(),
                source = prepared.Source, images = prepared.Images, sellerId = "seller-1"
            } }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(legacy, CatalogValidation.Json));
        var ledger = ImportLedger.Load(path, "LOCAL\\SQLEXPRESS", "CoCoReTrade", root);
        Assert.Equal(productId, ledger.Find("manifest-1", prepared)?.ProductId);
        Assert.False(ledger.Entries.Single().DetailsStored);
        ledger.Save(path);
        var restored = ImportLedger.Load(path, "LOCAL\\SQLEXPRESS", "CoCoReTrade", root);
        Assert.Equal(1, restored.SchemaVersion);
        var entry = Assert.Single(restored.Entries);
        Assert.Equal("https://www.depop.com/products/one", entry.SourceUrl);
        Assert.Equal("CI-a6f84b0f58773292353b", entry.ConsignmentCode);
        Assert.Equal(prepared.Fingerprint, entry.Fingerprint);
        Assert.False(entry.DetailsStored);
        Assert.Equal(prepared.Source.Description, entry.Source.Description);
        Assert.Equal(prepared.Source.Color, entry.Source.Color);
        Assert.Equal(productId, restored.Find("manifest-1", prepared)?.ProductId);
        restored.Record("manifest-1", prepared, productId, DateTime.UtcNow, "seller-1");
        Assert.False(restored.Entries.Single().DetailsStored);
        var alias = prepared with { Source = prepared.Source with { Id = "alias" } };
        restored.Record("manifest-2", alias, productId, DateTime.UtcNow, "seller-1");
        Assert.Equal(2, restored.Entries.Count);
        Assert.All(restored.Entries, receipt => Assert.False(receipt.DetailsStored));
        var changed = prepared with { Source = prepared.Source with { Description = "Changed legacy details." } };
        Assert.Throws<ArgumentException>(() => restored.Find("manifest-1", changed));
    }

    [Fact]
    public void StoredDetailsAreCheckedForNewReceiptsAndRecoveryButNotLegacyReceipts()
    {
        var prepared = Prepare(Record() with { Description = "Mô tả trên sản phẩm.", Color = "Trắng" }).Items.Single();
        var source = prepared.Source;
        var images = Enumerable.Range(0, 3).Select(i => new ProductImage($"{i}.jpg", "image/jpeg", i)).ToArray();
        Product ProductWithDetails(string? description, string? color) => Product.Submit("seller-1", prepared.ConsignmentCode,
            source.Name, source.Brand, source.Category, source.Size, source.OriginalPriceVnd, source.AskingPriceVnd,
            source.ConditionPercent, source.AllowOffers, images, description, color);
        var legacyProduct = ProductWithDetails(null, null);
        var legacyReceipt = new ImportReceipt("manifest-1", source.Id, prepared.CanonicalSourceUrl,
            prepared.Fingerprint, legacyProduct.Id, prepared.ConsignmentCode, legacyProduct.CreatedAtUtc,
            source, prepared.Images, "seller-1");
        ImportRunner.RequireMatchingStoredDetails(prepared, legacyProduct, legacyReceipt);
        Assert.Throws<ArgumentException>(() => ImportRunner.RequireMatchingStoredDetails(prepared, legacyProduct,
            legacyReceipt with { DetailsStored = true }));
        Assert.Throws<ArgumentException>(() => ImportRunner.RequireMatchingStoredDetails(prepared, legacyProduct, receipt: null));
        var matching = ProductWithDetails(" Mô tả trên sản phẩm. ", " Trắng ");
        ImportRunner.RequireMatchingStoredDetails(prepared, matching, legacyReceipt with { DetailsStored = true });
        ImportRunner.RequireMatchingStoredDetails(prepared, matching, receipt: null);
    }

    [Fact]
    public void DryRunIsDefaultAndPerItemSellerOverridesFallback()
    {
        File.WriteAllText(Path.Combine(root, "CoCoReTrade.Web.csproj"), "<Project />");
        var options = ImportOptions.Parse(["--manifest", Path.Combine(root, "manifest.json"), "--database", "CoCoReTrade",
            "--web-root", root, "--seller-email", "fallback@example.com"]);
        Assert.False(options.Apply);
        Assert.False(options.ValidateOnly);
        Assert.Equal("fallback@example.com", options.SellerEmailFor(Record()));
        Assert.Equal("owner@example.com", options.SellerEmailFor(Record() with { SellerEmail = "owner@example.com" }));
        Assert.Throws<ArgumentException>(() => (options with { SellerEmail = null }).SellerEmailFor(Record()));
        Assert.Equal("owner@example.com", (options with { SellerEmail = null }).SellerEmailFor(Record() with { SellerEmail = "owner@example.com" }));
    }

    [Fact]
    public void DatabaseRequiredAndRemoteServersRejected()
    {
        File.WriteAllText(Path.Combine(root, "CoCoReTrade.Web.csproj"), "<Project />");
        Assert.Throws<ArgumentException>(() => ImportOptions.Parse(["--manifest", "manifest.json", "--web-root", root]));
        Assert.Throws<ArgumentException>(() => ImportOptions.Parse(["--manifest", "manifest.json", "--database", "CoCoReTrade",
            "--web-root", root, "--server", "remote.example.com"]));
        Assert.Throws<ArgumentException>(() => ImportOptions.Parse(["--manifest", "manifest.json", "--database", "CoCoReTrade",
            "--web-root", root, "--apply", "--validate-only"]));
    }

    [Fact]
    public async Task SubmissionAdapterCannotApproveInspectOrPublish()
    {
        var record = Prepare(Record() with { Description = "  Áo cotton, cần kiểm định.  ", Color = " Trắng " }).Items.Single();
        var repository = new MemoryRepository();
        var service = new ProductService(repository, new MemoryImages(),
            new SourceProductCodeService { CurrentCode = record.ConsignmentCode });
        var result = await service.SubmitAsync("seller-1", CatalogValidation.CreateSubmission(record), CancellationToken.None);
        Assert.Equal(ProductStatus.PendingApproval, result.Status);
        Assert.Equal(PreliminaryReviewStatus.Pending, result.PreliminaryStatus);
        Assert.Empty(result.Sku);
        Assert.Null(result.VerifiedCondition);
        Assert.Null(result.PublishedAtUtc);
        Assert.Empty(repository.Product!.Inspections);
        Assert.False(repository.Product.CanPublish);
        Assert.Equal("Áo cotton, cần kiểm định.", result.Description);
        Assert.Equal("Trắng", result.Color);
        Assert.Equal(result.Description, repository.Product.Description);
        Assert.Equal(result.Color, repository.Product.Color);
    }

    public void Dispose()
    {
        var absolute = Path.GetFullPath(root);
        var allowed = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(absolute).StartsWith("coco-import-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        Directory.Delete(absolute, recursive: true);
    }

    private sealed class MemoryImages : IPrivateImageStore
    {
        public Task<(string Key, string ContentType)> SaveAsync(ImageUpload image, CancellationToken ct) =>
            Task.FromResult((Guid.NewGuid().ToString("N") + ".png", image.ContentType));
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream> OpenAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class MemoryRepository : IProductRepository
    {
        public Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) => work();
        public Product? Product { get; private set; }
        public Task AddAsync(Product product, CancellationToken ct) { Product = product; return Task.CompletedTask; }
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> InspectorUserIdsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
        public void AddNotification(MarketplaceNotification notification) { }
        public Task<Product?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Product?> FindByImageAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Product>> ActiveAsync(ProductFilter filter, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProductPage<Product>> ActivePageAsync(ProductFilter filter, int pageNumber, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Product>> OwnedAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Product>> PendingAsync(string? search, CancellationToken ct) => throw new NotSupportedException();
    }
}
