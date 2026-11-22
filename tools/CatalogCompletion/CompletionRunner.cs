using System.Data;
using System.Text.Json;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoCoReTrade.CatalogCompletion;

public sealed record ProductCompletionReceipt(Guid Id, string SellerEmail, string Decision,
    ProductSnapshot RequestedBefore, ProductSnapshot ObservedBefore, ProductSnapshot After,
    string BeforeRowVersion, string AfterRowVersion, string Notes);
public sealed record UserCompletionReceipt(string Email, string Decision, string RequestedBeforeName,
    string ObservedBeforeName, string AfterName);
public sealed record CompletionReceipt(string ManifestId, string Database, string Server, string ActorEmail,
    DateTime CompletedAtUtc, IReadOnlyList<ProductCompletionReceipt> Products, IReadOnlyList<UserCompletionReceipt> Users);

public static class CompletionRunner
{
    public static async Task RunAsync(CompletionOptions options, CompletionManifest manifest, CancellationToken ct)
    {
        CompletionValidation.Validate(manifest, options.Database, options.AllowActive);
        ValidateExistingReceipt(options.Results, manifest);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(options.ConnectionString()).Options);
        // All preflight reads and writes share one transaction. SQL locks and EF rowversion
        // checks prevent a concurrent workflow change from slipping between review and save.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var (server, database) = await TargetAsync(db, ct);
        if (database != options.Database) throw new ArgumentException("Database kết nối khác database được chỉ định.");
        var normalizedActor = manifest.ActorEmail.ToUpperInvariant();
        var actorIds = await db.Users.AsNoTracking().Where(user => user.NormalizedEmail == normalizedActor)
            .Select(user => user.Id).ToListAsync(ct);
        if (actorIds.Count != 1) throw new ArgumentException("Không tìm thấy duy nhất tài khoản actorEmail.");
        var actorId = actorIds.Single();
        var isAdmin = await db.UserRoles.Join(db.Roles, membership => membership.RoleId, role => role.Id,
            (membership, role) => new { membership.UserId, role.Name })
            .AnyAsync(item => item.UserId == actorId && item.Name == "Admin", ct);
        if (!isAdmin) throw new ArgumentException("actorEmail không có role Admin.");

        var productIds = manifest.Products.Select(item => item.Id).ToArray();
        var products = await db.Products.Where(product => productIds.Contains(product.Id)).ToDictionaryAsync(product => product.Id, ct);
        if (products.Count != productIds.Length) throw new ArgumentException("Có product id không tồn tại; chưa ghi thay đổi nào.");
        var sellerIds = products.Values.Select(product => product.SellerId).Distinct().ToArray();
        var sellers = await db.Users.AsNoTracking().Where(user => sellerIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Email }).ToDictionaryAsync(user => user.Id, ct);
        var productPlans = new List<(ProductCompletion Item, Product Product, ProductSnapshot Current, CompletionDecision Decision)>();
        foreach (var item in manifest.Products)
        {
            var product = products[item.Id];
            if (!sellers.TryGetValue(product.SellerId, out var seller) ||
                !CompletionValidation.AllowedSellerEmail(database, seller.Email))
                throw new ArgumentException($"{item.Id}: người bán ngoài danh sách được cho phép.");
            var current = ProductSnapshot.From(product);
            CompletionValidation.ValidateProductScope(database, options.AllowActive, current, product.ConsignmentCode);
            var decision = CompletionValidation.Decide(item, current);
            productPlans.Add((item, product, current, decision));
            Console.WriteLine($"Product {item.Id}: {decision}; owner={seller.Email}; status={product.Status}/{product.PreliminaryStatus}");
        }

        var userPlans = new List<(UserCompletion Item, UserState Current, CompletionDecision Decision)>();
        foreach (var item in manifest.Users)
        {
            var normalizedEmail = item.Email.ToUpperInvariant();
            // Never materialize password hashes, security stamps, tokens, phone numbers or auth settings.
            var found = await db.Users.AsNoTracking().Where(user => user.NormalizedEmail == normalizedEmail)
                .Select(user => new UserState(user.Id, user.FullName, user.ConcurrencyStamp)).ToListAsync(ct);
            if (found.Count != 1) throw new ArgumentException($"{item.Email}: không tìm thấy duy nhất người dùng.");
            var current = found.Single();
            var decision = CompletionValidation.Decide(item, current.FullName);
            userPlans.Add((item, current, decision));
            Console.WriteLine($"User {item.Email}: {decision}; FullName only");
        }

        Console.WriteLine($"Mode={(options.Apply ? "APPLY" : "DRY-RUN")}; server={server}; database={database}; " +
            $"update={productPlans.Count(plan => plan.Decision == CompletionDecision.Update) + userPlans.Count(plan => plan.Decision == CompletionDecision.Update)}; " +
            $"skip={productPlans.Count(plan => plan.Decision == CompletionDecision.Skip) + userPlans.Count(plan => plan.Decision == CompletionDecision.Skip)}");
        if (!options.Apply)
        {
            Console.WriteLine("Dry-run hoàn tất; không ghi DB, audit hoặc tệp kết quả.");
            return;
        }

        var now = DateTime.UtcNow;
        var beforeVersions = products.ToDictionary(pair => pair.Key, pair => Convert.ToBase64String(pair.Value.RowVersion));
        foreach (var (item, product, _, decision) in productPlans)
        {
            if (decision == CompletionDecision.Skip) continue;
            ApplyProductChanges(db, product, item.After);
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(), ActorId = actorId, Action = "Catalog.MetadataCompleted",
                EntityType = "Product", EntityId = product.Id.ToString(), Details = item.Notes, CreatedAtUtc = now
            });
        }
        foreach (var (item, current, decision) in userPlans)
        {
            if (decision == CompletionDecision.Skip) continue;
            // Attach a narrow stub and mark only FullName. Identity auth fields and stamps stay untouched.
            var user = new ApplicationUser { Id = current.Id, FullName = current.FullName, ConcurrencyStamp = current.ConcurrencyStamp };
            db.Users.Attach(user);
            db.Entry(user).Property(value => value.FullName).CurrentValue = item.AfterName;
            db.Entry(user).Property(value => value.FullName).IsModified = true;
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(), ActorId = actorId, Action = "Accounts.DisplayNameUpdated",
                EntityType = "User", EntityId = current.Id,
                Details = $"Manifest {manifest.Id}; tên hiển thị: {current.FullName} → {item.AfterName}", CreatedAtUtc = now
            });
        }
        ValidateTrackedWrites(db);
        await db.SaveChangesAsync(ct);
        var receipt = new CompletionReceipt(manifest.Id, database, server, manifest.ActorEmail, now,
            productPlans.Select(plan => new ProductCompletionReceipt(plan.Item.Id, sellers[plan.Product.SellerId].Email!,
                plan.Decision.ToString(), plan.Item.Before, plan.Current, ProductSnapshot.From(plan.Product),
                beforeVersions[plan.Item.Id], Convert.ToBase64String(plan.Product.RowVersion), plan.Item.Notes)).ToArray(),
            userPlans.Select(plan => new UserCompletionReceipt(plan.Item.Email, plan.Decision.ToString(),
                plan.Item.BeforeName, plan.Current.FullName, plan.Item.AfterName)).ToArray());
        var temporary = WritePendingReceipt(options.Results, receipt);
        var committed = false;
        try
        {
            await transaction.CommitAsync(ct);
            committed = true;
            try { File.Move(temporary, options.Results, overwrite: true); }
            catch (IOException) { throw new ReceiptFinalizationException(temporary); }
            catch (UnauthorizedAccessException) { throw new ReceiptFinalizationException(temporary); }
        }
        finally
        {
            if (!committed && File.Exists(temporary)) File.Delete(temporary);
        }
        Console.WriteLine($"Đã commit toàn bộ thay đổi và audit; results={options.Results}");
    }

    internal static void ApplyProductChanges(AppDbContext db, Product product, ProductChanges after)
    {
        var entry = db.Entry(product);
        entry.Property(value => value.Brand).CurrentValue = after.Brand;
        entry.Property(value => value.Size).CurrentValue = after.Size;
        entry.Property(value => value.Description).CurrentValue = after.Description;
        entry.Property(value => value.Color).CurrentValue = after.Color;
        entry.Property(value => value.OriginalPrice).CurrentValue = after.OriginalPrice;
    }

    internal static void ValidateTrackedWrites(AppDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var allowedProduct = new HashSet<string>([nameof(Product.Brand), nameof(Product.Size), nameof(Product.Description),
            nameof(Product.Color), nameof(Product.OriginalPrice)], StringComparer.Ordinal);
        foreach (var entry in db.ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity is AuditLog && entry.State == EntityState.Added) continue;
            if (entry.State == EntityState.Modified && entry.Entity is Product &&
                entry.Properties.Where(property => property.IsModified).All(property => allowedProduct.Contains(property.Metadata.Name))) continue;
            if (entry.State == EntityState.Modified && entry.Entity is ApplicationUser &&
                entry.Properties.Where(property => property.IsModified).All(property => property.Metadata.Name == nameof(ApplicationUser.FullName))) continue;
            throw new InvalidOperationException("Phát hiện ghi ngoài metadata sản phẩm/tên hiển thị/audit; hủy transaction.");
        }
    }

    private static void ValidateExistingReceipt(string path, CompletionManifest manifest)
    {
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length > 5_000_000) throw new ArgumentException("Tệp kết quả cũ quá lớn.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        if (!json.RootElement.TryGetProperty("manifestId", out var id) || id.GetString() != manifest.Id ||
            !json.RootElement.TryGetProperty("database", out var database) || database.GetString() != manifest.Database)
            throw new ArgumentException("Tệp kết quả có sẵn thuộc manifest/database khác.");
    }

    private static string WritePendingReceipt(string path, CompletionReceipt receipt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            using var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough);
            file.Write(JsonSerializer.SerializeToUtf8Bytes(receipt, CompletionValidation.Json));
            file.Flush(flushToDisk: true);
            return temporary;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static async Task<(string Server, string Database)> TargetAsync(AppDbContext db, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), DB_NAME()";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Không xác định được SQL target.");
        return (reader.GetString(0), reader.GetString(1));
    }

    private sealed record UserState(string Id, string FullName, string? ConcurrencyStamp);
}

public sealed class ReceiptFinalizationException(string pendingPath) : IOException(
    $"DB đã COMMIT nhưng chưa chuyển được receipt cuối. Receipt tạm: {pendingPath}. Chạy lại dry-run để đối chiếu; không rollback thủ công.");
