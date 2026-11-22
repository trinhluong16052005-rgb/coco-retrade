using System.Text.Json;
using System.Text.Json.Nodes;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.CatalogCompletion.Tests;

public sealed class CompletionTests
{
    private static Product Product() => CoCoReTrade.Domain.Product.Submit("seller-1", "CI-12345678901234567890", "Áo sơ mi",
        "", "Áo", "M", 300000, 300000, 90, true,
        Enumerable.Range(0, 3).Select(index => new ProductImage($"{index}.jpg", "image/jpeg", index)).ToArray());
    private static ProductCompletion Item() => new(Guid.NewGuid(), ProductSnapshot.From(Product()),
        new ProductChanges("CoCo Selection", "M", "Áo sơ mi đã qua sử dụng, chờ kiểm định.", "Trắng", 300000),
        "Thông tin bổ sung theo yêu cầu chủ website; chưa xác nhận nhãn gốc.");
    private static CompletionManifest Manifest() => new("completion-1", "CoCoReTrade", "admin01@cocoretrade.test", [Item()],
        [new UserCompletion("seller01@cocoretrade.test", "Seller 01", "Nguyễn Minh Anh")]);
    private static void Validate(CompletionManifest manifest, bool allowActive = false) =>
        CompletionValidation.Validate(manifest, manifest.Database, allowActive);

    [Fact]
    public void MainBeforeAndAfterProduceUpdateThenSkipWithoutPriceChanges()
    {
        var manifest = Manifest();
        Validate(manifest);
        var item = manifest.Products.Single();
        Assert.Equal(CompletionDecision.Update, CompletionValidation.Decide(item, item.Before));
        Assert.Equal(CompletionDecision.Skip, CompletionValidation.Decide(item, item.Before.With(item.After)));
        Assert.Equal(item.Before.AskingPrice, item.Before.With(item.After).AskingPrice);
        Assert.Equal(item.Before.OriginalPrice, item.Before.With(item.After).OriginalPrice);
    }

    [Theory]
    [InlineData(299000)]
    [InlineData(400000)]
    public void MainRejectsAnyOriginalPriceChange(decimal originalPrice)
    {
        var manifest = Manifest();
        var item = manifest.Products.Single();
        Assert.Throws<ArgumentException>(() => Validate(manifest with
            { Products = [item with { After = item.After with { OriginalPrice = originalPrice } }] }));
    }

    [Fact]
    public void DemoReferencePriceRequiresExplicitActiveFlagAndApprovedSkuPrefix()
    {
        var item = Item();
        item = item with { Before = item.Before with { Status = ProductStatus.Active,
            PreliminaryStatus = PreliminaryReviewStatus.Approved, Sku = "CC-2026-900001" },
            After = item.After with { OriginalPrice = 450000 } };
        var manifest = new CompletionManifest("demo-1", "CoCoReTradeDemo", "admin@cocoretrade.vn", [item], []);
        Validate(manifest, allowActive: true);
        Assert.Throws<ArgumentException>(() => Validate(manifest));
        Assert.Throws<ArgumentException>(() => Validate(manifest with
            { Products = [item with { Before = item.Before with { Sku = "CC-2026-100001" } }] }, allowActive: true));
        Assert.Throws<ArgumentException>(() => Validate(Manifest(), allowActive: true));
    }

    [Fact]
    public void BeforeMustMatchEveryFieldOrFullAfterExactly()
    {
        var item = Item();
        Assert.Throws<ArgumentException>(() => CompletionValidation.Decide(item, item.Before with { Name = "Đổi tên" }));
        Assert.Throws<ArgumentException>(() => CompletionValidation.Decide(item, item.Before with { AskingPrice = 290000 }));
        Assert.Throws<ArgumentException>(() => CompletionValidation.Decide(item, item.Before with { Status = ProductStatus.Reserved }));
        Assert.Throws<ArgumentException>(() => CompletionValidation.Decide(item, item.Before with { Brand = item.After.Brand }));
    }

    [Theory]
    [InlineData("brand", 101)]
    [InlineData("size", 31)]
    [InlineData("description", 2001)]
    [InlineData("color", 81)]
    public void AfterRejectsOversizedMetadata(string field, int length)
    {
        var manifest = Manifest();
        var item = manifest.Products.Single();
        var value = new string('x', length);
        var after = field switch
        {
            "brand" => item.After with { Brand = value }, "size" => item.After with { Size = value },
            "description" => item.After with { Description = value }, _ => item.After with { Color = value }
        };
        Assert.Throws<ArgumentException>(() => Validate(manifest with { Products = [item with { After = after }] }));
    }

    [Fact]
    public void AfterRejectsNullBlankMetadataAndWhitespaceRequiringNormalization()
    {
        var manifest = Manifest();
        var item = manifest.Products.Single();
        Assert.Throws<ArgumentException>(() => Validate(manifest with
            { Products = [item with { After = item.After with { Description = null! } }] }));
        Assert.Throws<ArgumentException>(() => Validate(manifest with
            { Products = [item with { After = item.After with { Color = " " } }] }));
        Assert.Throws<ArgumentException>(() => Validate(manifest with
            { Products = [item with { After = item.After with { Size = " M " } }] }));
        Assert.Throws<ArgumentException>(() => Validate(manifest with
            { Products = [item with { Notes = new string('x', 2001) }] }));
    }

    [Fact]
    public void ManifestRejectsDuplicatesEmptyBatchesAndUnknownActorsOrUsers()
    {
        var manifest = Manifest();
        Assert.Throws<ArgumentException>(() => Validate(manifest with { Products = [], Users = [] }));
        Assert.Throws<ArgumentException>(() => Validate(manifest with { Products = [manifest.Products[0], manifest.Products[0]] }));
        Assert.Throws<ArgumentException>(() => Validate(manifest with { Users = [manifest.Users[0], manifest.Users[0] with { Email = "SELLER01@COCORETRADE.TEST" }] }));
        Assert.Throws<ArgumentException>(() => Validate(manifest with { ActorEmail = "other@example.com" }));
        Assert.Throws<ArgumentException>(() => Validate(manifest with { Users = [manifest.Users[0] with { Email = "real.customer@example.com" }] }));
    }

    [Fact]
    public void UserRenameIsIdempotentAndDoesNotAcceptUnexpectedCurrentName()
    {
        var item = Manifest().Users.Single();
        Assert.Equal(CompletionDecision.Update, CompletionValidation.Decide(item, item.BeforeName));
        Assert.Equal(CompletionDecision.Skip, CompletionValidation.Decide(item, item.AfterName));
        Assert.Throws<ArgumentException>(() => CompletionValidation.Decide(item, "Tên người khác"));
    }

    [Fact]
    public void SellerAndUserEmailScopesStayInsideExplicitAccounts()
    {
        Assert.True(CompletionValidation.AllowedSellerEmail("CoCoReTrade", "seller08@cocoretrade.test"));
        Assert.False(CompletionValidation.AllowedSellerEmail("CoCoReTrade", "seller09@cocoretrade.test"));
        Assert.False(CompletionValidation.AllowedSellerEmail("CoCoReTrade", "buyer01@cocoretrade.test"));
        Assert.True(CompletionValidation.AllowedUserEmail("CoCoReTrade", "buyer01@cocoretrade.test"));
        Assert.True(CompletionValidation.AllowedSellerEmail("CoCoReTradeDemo", "seller.demo@cocoretrade.test"));
        Assert.False(CompletionValidation.AllowedSellerEmail("CoCoReTradeDemo", "seller01@cocoretrade.test"));
        Assert.True(CompletionValidation.AllowedUserEmail("CoCoReTradeDemo", "buyer.demo@cocoretrade.test"));
    }

    [Fact]
    public void MainRuntimeScopeRejectsNonImportAndPublishedProduct()
    {
        var current = Item().Before;
        CompletionValidation.ValidateProductScope("CoCoReTrade", false, current, "CI-existing");
        Assert.Throws<ArgumentException>(() => CompletionValidation.ValidateProductScope("CoCoReTrade", false, current, "OTHER"));
        Assert.Throws<ArgumentException>(() => CompletionValidation.ValidateProductScope("CoCoReTrade", false,
            current with { PreliminaryStatus = PreliminaryReviewStatus.Approved }, "CI-existing"));
        Assert.Throws<ArgumentException>(() => CompletionValidation.ValidateProductScope("CoCoReTrade", false,
            current with { Sku = "CC-2026-900001" }, "CI-existing"));
    }

    [Fact]
    public void OptionsRequireAllowedExplicitDatabaseAndUseOnlyFixedWindowsAuthentication()
    {
        var options = CompletionOptions.Parse(["--manifest", "batch.json", "--database", "CoCoReTrade"]);
        Assert.False(options.Apply);
        var connection = new SqlConnectionStringBuilder(options.ConnectionString());
        Assert.Equal(@".\SQLEXPRESS", connection.DataSource);
        Assert.True(connection.IntegratedSecurity);
        Assert.Empty(connection.UserID);
        Assert.Empty(connection.Password);
        Assert.Throws<ArgumentException>(() => CompletionOptions.Parse(["--manifest", "batch.json"]));
        Assert.Throws<ArgumentException>(() => CompletionOptions.Parse(["--manifest", "batch.json", "--database", "other"]));
        Assert.Throws<ArgumentException>(() => CompletionOptions.Parse(["--manifest", "batch.json", "--database", "CoCoReTrade", "--server", "remote"]));
        Assert.Throws<ArgumentException>(() => CompletionOptions.Parse(["--manifest", "batch.json", "--database", "CoCoReTrade", "--apply", "--validate-only"]));
        Assert.Throws<ArgumentException>(() => CompletionValidation.Validate(Manifest(), "CoCoReTradeDemo", true));
    }

    [Fact]
    public void JsonRejectsMissingSnapshotPropertiesUnknownAuthFieldsAndNumericStatus()
    {
        var json = JsonSerializer.Serialize(Manifest(), CompletionValidation.Json);
        Assert.NotNull(JsonSerializer.Deserialize<CompletionManifest>(json, CompletionValidation.Json));
        var missing = JsonNode.Parse(json)!;
        missing["products"]![0]!["after"]!.AsObject().Remove("color");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CompletionManifest>(missing.ToJsonString(), CompletionValidation.Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CompletionManifest>(json.Replace("\"afterName\":", "\"password\": \"forbidden\", \"afterName\":"), CompletionValidation.Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CompletionManifest>(json.Replace("\"PendingApproval\"", "0"), CompletionValidation.Json));
    }

    [Fact]
    public void TrackedProductUpdateTouchesOnlyAllowedMetadataAndPreservesWorkflowAndImages()
    {
        using var db = Context();
        var product = Product();
        var before = ProductSnapshot.From(product);
        var originalImages = product.Images.Select(image => image.Id).ToArray();
        db.Attach(product);
        var changes = Item().After;
        CompletionRunner.ApplyProductChanges(db, product, changes);
        CompletionRunner.ValidateTrackedWrites(db);
        Assert.Equal(before.With(changes), ProductSnapshot.From(product));
        Assert.Equal(originalImages, product.Images.Select(image => image.Id));
        Assert.All(db.Entry(product).Properties.Where(property => property.IsModified), property =>
            Assert.Contains(property.Metadata.Name, new[] { "Brand", "Size", "Description", "Color", "OriginalPrice" }));
        db.Entry(product).Property(value => value.AskingPrice).CurrentValue = 290000;
        Assert.Throws<InvalidOperationException>(() => CompletionRunner.ValidateTrackedWrites(db));
    }

    [Fact]
    public void TrackedUserUpdatePermitsOnlyDisplayNameAndRejectsAuthChanges()
    {
        using var db = Context();
        var user = new ApplicationUser { Id = "user-1", FullName = "Before", ConcurrencyStamp = "concurrency" };
        db.Attach(user);
        user.FullName = "Nguyễn Minh Anh";
        CompletionRunner.ValidateTrackedWrites(db);
        Assert.Single(db.Entry(user).Properties, property => property.IsModified);
        Assert.Equal("concurrency", user.ConcurrencyStamp);
        user.Email = "changed@example.com";
        Assert.Throws<InvalidOperationException>(() => CompletionRunner.ValidateTrackedWrites(db));
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=.\\SQLEXPRESS;Database=NoConnectionUnitTests;Integrated Security=True;TrustServerCertificate=True").Options);
}
