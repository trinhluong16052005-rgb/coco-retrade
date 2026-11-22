using System.Security.Cryptography;
using System.Text.Json;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var repo = Directory.GetCurrentDirectory();
var catalogPath = Path.Combine(repo, "src", "CoCoReTrade.Web", "demo-products.json");
if (!File.Exists(catalogPath)) throw new InvalidOperationException("Run from CoCoReTrade repository root.");
var connection = Environment.GetEnvironmentVariable("COCO_DEMO_CONNECTION")
    ?? @"Server=.\SQLEXPRESS;Database=CoCoReTradeDemo;Trusted_Connection=True;TrustServerCertificate=True";
var sql = new SqlConnectionStringBuilder(connection);
if (!string.Equals(sql.InitialCatalog, "CoCoReTradeDemo", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Demo seed only accepts database CoCoReTradeDemo.");

var items = JsonSerializer.Deserialize<List<DemoItem>>(File.ReadAllText(catalogPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
if (items.Count != 25 || items.Select(x => x.Sku).Distinct().Count() != 25)
    throw new InvalidOperationException("Demo catalog must contain 25 unique products.");

var uploadRoot = Path.Combine(repo, "src", "CoCoReTrade.Web", "PrivateUploads");
Directory.CreateDirectory(uploadRoot);
if (args.Contains("--refresh-images", StringComparer.Ordinal))
{
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
    await using var imageDb = new AppDbContext(options);
    var skus = items.Select(x => x.Sku).ToArray();
    var products = await imageDb.Products.Include(x => x.Images).Where(x => x.Sku != null && skus.Contains(x.Sku)).ToListAsync();
    var refreshed = 0;
    foreach (var product in products)
    {
        var item = items.Single(x => x.Sku == product.Sku);
        if (item.ImageSources.Count < 3) throw new InvalidOperationException($"Missing original photo sources for {item.Sku}.");
        foreach (var image in product.Images)
        {
            // Change only original demo image references. Keep IDs, inspection
            // links, custom uploads, product state, orders and users untouched.
            var prefix = $"demo/{item.Sku.ToLowerInvariant()}-";
            if (!image.StorageKey.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var view = Path.GetFileNameWithoutExtension(image.StorageKey)[prefix[5..].Length..];
            if (!int.TryParse(view, out var index) || index is < 1 or > 3) continue;
            var key = $"{prefix}{index}.webp";
            if (!File.Exists(Path.Combine(uploadRoot, key.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidOperationException($"Download original photos before refreshing {item.Sku}.");
            imageDb.Entry(image).Property(x => x.StorageKey).CurrentValue = key;
            imageDb.Entry(image).Property(x => x.ContentType).CurrentValue = "image/webp";
            refreshed++;
        }
    }
    await imageDb.SaveChangesAsync();
    Console.WriteLine($"Refreshed {refreshed} original photo references for {products.Count} demo products; product/image IDs, users and orders preserved.");
    return;
}
var credentialPath = Path.Combine(uploadRoot, "demo-credentials.txt");
DemoCredentials credentials;
if (File.Exists(credentialPath))
    credentials = JsonSerializer.Deserialize<DemoCredentials>(File.ReadAllText(credentialPath))
        ?? throw new InvalidOperationException("Invalid demo credential file.");
else
{
    credentials = new DemoCredentials(NewPassword(), NewPassword(), NewPassword());
    File.WriteAllText(credentialPath, JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true }));
}

var services = new ServiceCollection();
services.AddLogging();
services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connection));
services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.User.RequireUniqueEmail = true;
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Admin:InitialPassword"] = credentials.AdminPassword }).Build();
await IdentitySeeder.SeedAsync(db, roles, users, config);

var seller = await EnsureUser("seller.demo@cocoretrade.test", "Người ký gửi demo", credentials.SellerPassword);
await EnsureUser("buyer.demo@cocoretrade.test", "Người mua demo", credentials.BuyerPassword);
var admin = await users.FindByEmailAsync("admin@cocoretrade.vn")
    ?? throw new InvalidOperationException("Demo admin missing.");

var imageDir = Path.Combine(uploadRoot, "demo");
Directory.CreateDirectory(imageDir);
var added = 0;
foreach (var item in items)
{
    var images = new List<ProductImage>();
    for (var view = 1; view <= 3; view++)
    {
        var fileName = $"{item.Sku.ToLowerInvariant()}-{view}.webp";
        if (item.ImageSources.Count < 3 || !File.Exists(Path.Combine(imageDir, fileName)))
            throw new InvalidOperationException("Run tools/prepare_demo_photos.py before seeding products.");
        images.Add(new ProductImage($"demo/{fileName}", "image/webp", view - 1));
    }
    if (await db.Products.AnyAsync(x => x.Sku == item.Sku)) continue;
    var index = int.Parse(item.Sku[^6..]);
    var product = Product.Submit(seller.Id, $"DEMO-{index}", item.Name, item.Brand,
        item.Category, item.Size, item.PriceVnd, item.PriceVnd, item.ConditionPercent, true, images);
    var now = DateTime.UtcNow.AddMinutes(-items.Count + added);
    product.ReviewPreliminarily(admin.Id, PreliminaryReviewStatus.Approved, null, now);
    product.RecordInspection(admin.Id, true, item.Name, item.Brand, item.Category, item.Size,
        item.PriceVnd, item.PriceVnd, item.ConditionPercent, "Kiểm định mô phỏng cho môn học", [], now);
    product.Publish(item.Sku, now);
    db.Products.Add(product);
    added++;
}
await db.SaveChangesAsync();
Console.WriteLine($"Demo database ready: {items.Count} catalog products, {added} added this run.");
Console.WriteLine($"Credentials saved locally: {credentialPath}");

async Task<ApplicationUser> EnsureUser(string email, string name, string password)
{
    var existing = await users.FindByEmailAsync(email);
    if (existing is not null) return existing;
    var user = new ApplicationUser
    {
        UserName = email, Email = email, EmailConfirmed = true, FullName = name,
        MustChangePassword = true
    };
    var created = await users.CreateAsync(user, password);
    if (!created.Succeeded)
        throw new InvalidOperationException(string.Join("; ", created.Errors.Select(x => x.Description)));
    var assigned = await users.AddToRoleAsync(user, "User");
    if (!assigned.Succeeded)
        throw new InvalidOperationException(string.Join("; ", assigned.Errors.Select(x => x.Description)));
    return user;
}

static string NewPassword() => "CoCo9!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

internal sealed record DemoCredentials(string AdminPassword, string SellerPassword, string BuyerPassword);
internal sealed record DemoItem(string Sku, string Name, string Brand, string Category, string Size,
    string Color, int ConditionPercent, string Description, decimal ReferencePriceGbp,
    decimal PriceVnd, string SourceTitle, string SourceUrl, string ImageCredit,
    IReadOnlyList<string> ImageSources);
