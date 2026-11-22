using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CoCoReTrade.Domain;

namespace CoCoReTrade.CatalogCompletion;

public sealed record CompletionManifest(string Id, string Database, string ActorEmail,
    IReadOnlyList<ProductCompletion> Products, IReadOnlyList<UserCompletion> Users);
public sealed record ProductCompletion(Guid Id, ProductSnapshot Before, ProductChanges After, string Notes);
public sealed record ProductChanges(string Brand, string Size, string Description, string Color, decimal OriginalPrice);
public sealed record UserCompletion(string Email, string BeforeName, string AfterName);
public sealed record ProductSnapshot(string Name, string Brand, string Category, string Size, decimal OriginalPrice,
    decimal AskingPrice, int DeclaredCondition, string? Description, string? Color, ProductStatus Status,
    PreliminaryReviewStatus PreliminaryStatus, string Sku)
{
    public static ProductSnapshot From(Product product) => new(product.Name, product.Brand, product.Category,
        product.Size, product.OriginalPrice, product.AskingPrice, product.DeclaredCondition, product.Description,
        product.Color, product.Status, product.PreliminaryStatus, product.Sku);

    public ProductSnapshot With(ProductChanges changes) => this with
    {
        Brand = changes.Brand, Size = changes.Size, Description = changes.Description,
        Color = changes.Color, OriginalPrice = changes.OriginalPrice
    };
}
public enum CompletionDecision { Update, Skip }

public static class CompletionValidation
{
    public const string MainDatabase = "CoCoReTrade";
    public const string DemoDatabase = "CoCoReTradeDemo";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static CompletionManifest Load(string path, string database, bool allowActive)
    {
        if (new FileInfo(path).Length > 5_000_000) throw new ArgumentException("Manifest vượt quá 5 MB.");
        var manifest = JsonSerializer.Deserialize<CompletionManifest>(File.ReadAllText(path), Json)
            ?? throw new ArgumentException("Manifest trống.");
        Validate(manifest, database, allowActive);
        return manifest;
    }

    public static void Validate(CompletionManifest manifest, string database, bool allowActive)
    {
        ValidateDatabase(database);
        if (manifest.Database != database) throw new ArgumentException("Database trong manifest khác --database.");
        if (allowActive && database != DemoDatabase) throw new ArgumentException("--allow-active chỉ dùng với CoCoReTradeDemo.");
        if (manifest.Id is null || !Regex.IsMatch(manifest.Id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$"))
            throw new ArgumentException("Manifest id cần 1–100 chữ/số hoặc . _ -.");
        if (!EmailEquals(manifest.ActorEmail, ActorEmail(database)))
            throw new ArgumentException("actorEmail ngoài tài khoản quản trị được cho phép cho database này.");
        if (manifest.Products is null || manifest.Users is null || manifest.Products.Count + manifest.Users.Count is < 1 or > 100)
            throw new ArgumentException("Manifest cần 1–100 mục sản phẩm/người dùng.");
        if (manifest.Products.Any(item => item is null || item.Id == Guid.Empty) ||
            manifest.Products.Select(item => item.Id).Distinct().Count() != manifest.Products.Count)
            throw new ArgumentException("Product id trống hoặc trùng.");
        if (manifest.Users.Any(item => item is null || !AllowedUserEmail(database, item.Email)) ||
            manifest.Users.Select(item => item.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Users.Count)
            throw new ArgumentException("Email người dùng ngoài phạm vi hoặc trùng.");
        foreach (var item in manifest.Products)
        {
            if (item.Before is null || item.After is null) throw new ArgumentException($"{item.Id}: thiếu before/after.");
            var before = item.Before;
            var after = item.After;
            if (before.Name is null || before.Brand is null || before.Category is null || before.Size is null || before.Sku is null ||
                !Enum.IsDefined(before.Status) || !Enum.IsDefined(before.PreliminaryStatus))
                throw new ArgumentException($"{item.Id}: before không đầy đủ.");
            if (string.IsNullOrWhiteSpace(item.Notes) || item.Notes.Length > 2000)
                throw new ArgumentException($"{item.Id}: notes cần 1–2.000 ký tự.");
            RequireCanonicalText(after.Brand, 100, "brand");
            RequireCanonicalText(after.Size, 30, "size");
            RequireCanonicalText(after.Description, 2000, "description");
            RequireCanonicalText(after.Color, 80, "color");
            if (database == MainDatabase && after.OriginalPrice != before.OriginalPrice)
                throw new ArgumentException($"{item.Id}: không được đổi giá gốc của hàng bán thật.");
            // Reuse the same model limits as normal submissions without creating persisted data.
            Product.Submit("validation-only", "metadata-validation", before.Name, after.Brand, before.Category,
                after.Size, after.OriginalPrice, before.AskingPrice, before.DeclaredCondition, false,
                Enumerable.Range(0, 3).Select(index => new ProductImage($"{index}.jpg", "image/jpeg", index)).ToArray(),
                after.Description, after.Color);
            ValidateProductScope(database, allowActive, before, consignmentCode: "CI-validation");
        }
        foreach (var user in manifest.Users)
        {
            if (user.BeforeName is null || user.BeforeName.Length > 150)
                throw new ArgumentException("beforeName không hợp lệ.");
            RequireCanonicalText(user.AfterName, 150, "afterName");
        }
    }

    public static void ValidateDatabase(string database)
    {
        if (database is not (MainDatabase or DemoDatabase))
            throw new ArgumentException("--database phải là CoCoReTrade hoặc CoCoReTradeDemo.");
    }

    public static void ValidateProductScope(string database, bool allowActive, ProductSnapshot current, string consignmentCode)
    {
        if (database == MainDatabase)
        {
            if (!consignmentCode.StartsWith("CI-", StringComparison.Ordinal) || current.Status != ProductStatus.PendingApproval ||
                current.PreliminaryStatus != PreliminaryReviewStatus.Pending || current.Sku.Length != 0)
                throw new ArgumentException("Hàng bán thật chỉ được hoàn thiện ở PendingApproval/Pending, mã CI- và SKU trống.");
        }
        else if (database != DemoDatabase || !allowActive || current.Status != ProductStatus.Active ||
            !Regex.IsMatch(current.Sku, "^CC-2026-900[0-9]{3}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Demo chỉ được sửa sản phẩm Active có SKU CC-2026-900xxx khi có --allow-active.");
    }

    public static CompletionDecision Decide(ProductCompletion item, ProductSnapshot current)
    {
        if (current == item.Before.With(item.After)) return CompletionDecision.Skip;
        if (current == item.Before) return CompletionDecision.Update;
        throw new ArgumentException($"{item.Id}: snapshot DB khác cả before và after. Dừng để đối chiếu.");
    }

    public static CompletionDecision Decide(UserCompletion item, string currentName)
    {
        if (currentName == item.AfterName) return CompletionDecision.Skip;
        if (currentName == item.BeforeName) return CompletionDecision.Update;
        throw new ArgumentException($"{item.Email}: tên trong DB khác cả beforeName và afterName.");
    }

    public static string ActorEmail(string database) => database == MainDatabase ? "admin01@cocoretrade.test" : "admin@cocoretrade.vn";
    public static bool AllowedSellerEmail(string database, string? email) => database == MainDatabase
        ? email is not null && Regex.IsMatch(email, "^seller0[1-8]@cocoretrade\\.test$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        : EmailEquals(email, "seller.demo@cocoretrade.test");
    public static bool AllowedUserEmail(string database, string? email) => AllowedSellerEmail(database, email) ||
        EmailEquals(email, ActorEmail(database)) || EmailEquals(email,
            database == MainDatabase ? "buyer01@cocoretrade.test" : "buyer.demo@cocoretrade.test");
    private static bool EmailEquals(string? actual, string expected) => actual is not null &&
        MailAddress.TryCreate(actual, out var address) && address.Address == actual &&
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    private static void RequireCanonicalText(string? text, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength || text != text.Trim())
            throw new ArgumentException($"{field} cần 1–{maxLength} ký tự, không có khoảng trắng hai đầu.");
    }
}
