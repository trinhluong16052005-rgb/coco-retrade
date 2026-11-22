using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CoCoReTrade.CatalogImport;

// Source-derived consignment IDs let the DB's existing unique index prevent duplicates even
// if the process ends after COMMIT but before the receipt file is written. No SKU is allocated.
public sealed class SourceProductCodeService : IProductCodeService
{
    public string? CurrentCode { get; set; }
    public Task<string> NewConsignmentCodeAsync(CancellationToken ct) => Task.FromResult(CurrentCode
        ?? throw new InvalidOperationException("Chưa chọn record để nhập."));
    public Task<string> NewProductSkuAsync(CancellationToken ct) =>
        throw new InvalidOperationException("CatalogImport không được cấp SKU hoặc đăng bán.");
}

public sealed class ImportRunner
{
    public static async Task RunAsync(ImportOptions options, PreparedManifest manifest, CancellationToken ct)
    {
        var ownerEmails = manifest.Items.Select(record => options.SellerEmailFor(record.Source).ToUpperInvariant()).Distinct().ToArray();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(builder => builder.UseSqlServer(options.ConnectionString()));
        services.AddSingleton<IHostEnvironment>(new ImportEnvironment(options.WebRoot));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Storage:Root"] = options.StorageRoot, ["Demo:Enabled"] = "false" }).Build());
        services.AddSingleton(new ShippingPolicy());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IPrivateImageStore, PrivateImageStore>();
        services.AddScoped<SourceProductCodeService>();
        services.AddScoped<IProductCodeService>(provider => provider.GetRequiredService<SourceProductCodeService>());
        services.AddScoped<IProductService, ProductService>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // No application startup, seeder, identity manager, migration, worker or sequence runs here.
        var (server, database) = await TargetAsync(db, ct);
        if (!string.Equals(database, options.Database, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Database đang kết nối không khớp --database. Dừng nhập.");
        var sellers = await db.Users.AsNoTracking().Where(user => ownerEmails.Contains(user.NormalizedEmail!))
            .Select(user => new { user.Id, user.NormalizedEmail }).ToListAsync(ct);
        if (sellers.Count != ownerEmails.Length)
            throw new ArgumentException("Có người bán không tồn tại. Công cụ không tạo hoặc sửa tài khoản.");
        var sellerIds = sellers.ToDictionary(user => user.NormalizedEmail!, user => user.Id, StringComparer.OrdinalIgnoreCase);
        var resultPath = options.Results ?? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(options.WebRoot))!,
            ".artifacts", "catalog-import", CatalogValidation.Hash($"{server.ToUpperInvariant()}|{database.ToUpperInvariant()}")[..16] + ".results.json");
        Console.WriteLine($"Mode={(options.Apply ? "APPLY" : "DRY-RUN")}; server={server}; database={database}; records={manifest.Items.Count}; sellers={sellers.Count}");
        Console.WriteLine($"Results={resultPath}");
        FileStream? ledgerLock = null;
        try
        {
            if (options.Apply)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
                ledgerLock = new FileStream(resultPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            var ledger = ImportLedger.Load(resultPath, server, database, options.StorageRoot);
            var plans = new List<(PreparedRecord Record, string SellerId, Product? Existing)>();
            // Validate every record and receipt before any write is allowed.
            foreach (var record in manifest.Items)
            {
                var sellerId = sellerIds[options.SellerEmailFor(record.Source)];
                var existing = await CheckExistingAsync(db, ledger, manifest.Id, record, sellerId, options.StorageRoot, ct);
                plans.Add((record, sellerId, existing));
                Console.WriteLine($"{record.Source.Id}: {(existing is null ? "CREATE PendingApproval / Pending" : "SKIP existing " + existing.Id)}; images={record.Images.Count}");
            }
            if (!options.Apply)
            {
                Console.WriteLine("Dry-run hoàn tất. Không ghi DB, ảnh hoặc tệp kết quả.");
                return;
            }
            var productService = scope.ServiceProvider.GetRequiredService<IProductService>();
            var codes = scope.ServiceProvider.GetRequiredService<SourceProductCodeService>();
            var imported = 0;
            foreach (var (record, sellerId, existing) in plans)
            {
                ct.ThrowIfCancellationRequested();
                if (existing is not null)
                {
                    ledger.Record(manifest.Id, record, existing.Id, existing.CreatedAtUtc, sellerId);
                    ledger.Save(resultPath);
                    continue;
                }
                var submission = CatalogValidation.CreateSubmission(record);
                codes.CurrentCode = record.ConsignmentCode;
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var product = await productService.SubmitAsync(sellerId, submission, ct);
                if (product.Status != ProductStatus.PendingApproval || product.PreliminaryStatus != PreliminaryReviewStatus.Pending ||
                    !string.IsNullOrWhiteSpace(product.Sku) || product.VerifiedCondition is not null || product.PublishedAtUtc is not null)
                    throw new InvalidOperationException("Trạng thái tạo mới không an toàn; transaction bị hủy.");
                await transaction.CommitAsync(ct);
                ledger.Record(manifest.Id, record, product.Id, product.CreatedAtUtc, sellerId);
                ledger.Save(resultPath);
                db.ChangeTracker.Clear();
                imported++;
                Console.WriteLine($"IMPORTED {record.Source.Id}; productId={product.Id}; PendingApproval / Pending");
            }
            Console.WriteLine($"Hoàn tất: tạo {imported}; bỏ qua {plans.Count - imported}. Chưa kiểm định hoặc đăng bán sản phẩm nào.");
        }
        finally { ledgerLock?.Dispose(); }
    }

    private static async Task<Product?> CheckExistingAsync(AppDbContext db, ImportLedger ledger, string manifestId,
        PreparedRecord record, string sellerId, string storageRoot, CancellationToken ct)
    {
        var receipt = ledger.Find(manifestId, record);
        var existing = await db.Products.AsNoTracking().Include(product => product.Images)
            .SingleOrDefaultAsync(product => product.ConsignmentCode == record.ConsignmentCode, ct);
        if (receipt is not null)
        {
            if (existing is null || existing.Id != receipt.ProductId || existing.SellerId != sellerId || receipt.SellerId != sellerId)
                throw new ArgumentException($"{record.Source.Id}: receipt không khớp DB. Dừng để đối chiếu; không tạo bản thay thế.");
            RequireMatchingStoredDetails(record, existing, receipt);
            return existing;
        }
        if (existing is null) return null;
        if (existing.SellerId != sellerId)
            throw new ArgumentException($"{record.Source.Id}: URL đã thuộc sản phẩm/người bán khác. Không tạo trùng.");
        RequireMatchingStoredDetails(record, existing, receipt: null);
        var hashes = new List<string>();
        foreach (var image in existing.Images.OrderBy(image => image.SortOrder))
        {
            if (!Regex.IsMatch(image.StorageKey, "^[a-f0-9]{32}\\.(jpg|jpeg|png|webp)$", RegexOptions.CultureInvariant))
                throw new ArgumentException($"{record.Source.Id}: không thể khôi phục receipt từ ảnh trong DB.");
            var path = Path.Combine(storageRoot, image.StorageKey);
            if (!File.Exists(path)) throw new ArgumentException($"{record.Source.Id}: thiếu ảnh đã nhập trong PrivateUploads.");
            await using var stream = File.OpenRead(path);
            hashes.Add(Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant());
        }
        RequireMatchingStoredCore(record, existing, hashes);
        return existing;
    }

    internal static void RequireMatchingStoredCore(PreparedRecord record, Product existing, IEnumerable<string> imageHashes)
    {
        // A category merge may have changed the stored code after import. Only equivalent
        // canonical categories may reuse the original manifest spelling for receipt recovery;
        // every other field and the ordered image hashes still match the original fingerprint.
        if (ProductCategories.Normalize(existing.Category) != ProductCategories.Normalize(record.Source.Category))
            throw new ArgumentException($"{record.Source.Id}: danh mục trong DB khác dữ liệu nhập. Cần đối chiếu receipt gốc, không tạo bản trùng.");
        var fingerprint = CatalogValidation.Fingerprint(record.CanonicalSourceUrl, existing.Name, existing.Brand,
            record.Source.Category.Trim(), existing.Size, existing.OriginalPrice, existing.AskingPrice,
            existing.DeclaredCondition, existing.AllowOffers, imageHashes);
        if (fingerprint != record.Fingerprint)
            throw new ArgumentException($"{record.Source.Id}: DB đã có mã nguồn nhưng nội dung khác. Cần đối chiếu receipt gốc, không tạo bản trùng.");
    }

    // Legacy receipts kept these fields for provenance only; never silently backfill SQL.
    // Recovery without a receipt requires matching the metadata stored by the new importer.
    internal static void RequireMatchingStoredDetails(PreparedRecord record, Product existing, ImportReceipt? receipt)
    {
        if ((receipt is null || receipt.DetailsStored) &&
            !CatalogValidation.DetailsMatch(record.Source, existing.Description, existing.Color))
            throw new ArgumentException($"{record.Source.Id}: mô tả/màu sắc trong DB khác dữ liệu nhập. Cần đối chiếu qua luồng sửa ký gửi.");
    }

    private static async Task<(string Server, string Database)> TargetAsync(AppDbContext db, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), DB_NAME()";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Không xác định được SQL target.");
        return (reader.GetString(0), reader.GetString(1));
    }

    private sealed class ImportEnvironment(string webRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "CoCoReTrade.CatalogImport";
        public string ContentRootPath { get; set; } = webRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
