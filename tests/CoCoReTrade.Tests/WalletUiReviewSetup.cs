using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using QRCoder;
using static CoCoReTrade.Tests.AdminSqlAuditFactory;

namespace CoCoReTrade.Tests;

// Optional browser review: isolated loopback server/database, with a five-minute watchdog.
public sealed class WalletUiReviewSetup
{
    [Fact]
    public async Task IsolatedBrowserReview()
    {
        var qa = Environment.GetEnvironmentVariable("COCO_ADMIN_UI_REVIEW");
        if (string.IsNullOrWhiteSpace(qa)) return;
        var root = Path.GetFullPath(qa); Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "review-config.json");
        var resultPath = Path.Combine(root, "review-done.json");
        Assert.False(File.Exists(resultPath), "Use a fresh review folder.");
        await using var factory = new AdminSqlAuditFactory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var bootstrap = factory.CreateClient();
        try
        {
            await factory.InitializeUsersAsync();
            var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            factory.ReviewAddress = new Uri(address);
            using var seller = await factory.LoginAsync("seller"); using var admin = await factory.LoginAsync("admin");
            for (var i = 0; i < 7; i++) await Success<SavedBankAccountDto>(await seller.PostAsJsonAsync("/api/v1/wallet/bank-accounts",
                new BankAccountInput(null, i % 2 == 0 ? "Vietcombank" : "ACB", "012345678" + i, "NGUYEN VAN A")));
            for (var i = 0; i < 13; i++)
            {
                var item = await Success<WithdrawalDto>(await seller.PostAsJsonAsync("/api/v1/wallet/withdrawal-requests",
                    new WalletWithdrawalInput(Guid.NewGuid(), 1000 + i * 1000, "Vietcombank", "0123456789", "NGUYEN VAN A")));
                if (i % 4 is 0 or 1)
                {
                    await admin.PostAsync($"/api/v1/admin/wallets/withdrawals/{item.Id}/approve", null);
                    if (i % 4 == 1) await admin.PostAsJsonAsync($"/api/v1/admin/wallets/withdrawals/{item.Id}/confirm-transfer", new { Confirmed = true, TransferReference = "REVIEW-" + i });
                }
                if (i % 4 == 2) await admin.PostAsJsonAsync($"/api/v1/admin/wallets/withdrawals/{item.Id}/reject", new { Reason = "Kiểm tra lại thông tin tài khoản" });
            }
            Guid ownedId, otherId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var actor = (await users.FindByEmailAsync("admin@cocoretrade.vn"))!;
                var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
                var photoPath = Path.Combine(environment.WebRootPath, "images", "coco", "coco-accessories-800.webp");
                var photoBytes = await File.ReadAllBytesAsync(photoPath);
                var stored = await scope.ServiceProvider.GetRequiredService<IPrivateImageStore>().SaveAsync(
                    new("review.webp", "image/webp", photoBytes.Length, () => new MemoryStream(photoBytes)), default);
                var now = DateTime.UtcNow;
                Product Item(int i, ProductStatus status, string owner = "seller")
                {
                    var item = Product.Submit(owner, "CG-2026-" + (995000 + i), "Món đồ có tên dài để kiểm tra thẻ " + i, "CoCo", "Áo", "M", 400000, 200000, 95, true,
                        [new ProductImage(stored.Key, stored.ContentType, 0), new ProductImage(stored.Key, stored.ContentType, 1), new ProductImage(stored.Key, stored.ContentType, 2)]);
                    if (status == ProductStatus.Rejected) item.Reject("Ảnh chưa phù hợp yêu cầu");
                    else if (status != ProductStatus.PendingApproval)
                    {
                        item.ReviewPreliminarily(actor.Id, PreliminaryReviewStatus.Approved, null, now);
                        item.RecordInspection(actor.Id, true, item.Name, item.Brand, item.Category, item.Size, item.OriginalPrice, item.AskingPrice, 95, "Đã kiểm định", [], now);
                        item.Publish("CC-2026-" + (995000 + i), now);
                        if (status is ProductStatus.Reserved or ProductStatus.Sold) item.Reserve();
                        if (status == ProductStatus.Sold) item.MarkSold();
                    }
                    return item;
                }
                var owned = Item(1, ProductStatus.Active); var other = Item(6, ProductStatus.Active, "buyer");
                db.Products.AddRange(owned, Item(2, ProductStatus.Sold), Item(3, ProductStatus.Reserved), Item(4, ProductStatus.PendingApproval), Item(5, ProductStatus.Rejected), other);
                db.Favorites.Add(new Favorite { UserId = "seller", ProductId = owned.Id, CreatedAtUtc = now });
                db.Favorites.Add(new Favorite { UserId = "seller", ProductId = other.Id, CreatedAtUtc = now });
                await db.SaveChangesAsync(); ownedId = owned.Id; otherId = other.Id;
            }
            var qrPath = Path.Combine(root, "vietqr-test.png");
            using (var generator = new QRCodeGenerator())
            using (var qr = generator.CreateQrCode(BankQrReaderTests.Payload(), QRCodeGenerator.ECCLevel.M))
            using (var png = new PngByteQRCode(qr)) await File.WriteAllBytesAsync(qrPath, png.GetGraphic(8));
            var url = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { url, password = factory.Password, qa = root, ownedId, otherId, qrPath }));
            var stopAt = DateTime.UtcNow.AddMinutes(5);
            while (!File.Exists(resultPath) && DateTime.UtcNow < stopAt) await Task.Delay(500);
            Assert.True(File.Exists(resultPath), "Browser review timed out.");
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
            Assert.True(result.RootElement.GetProperty("success").GetBoolean(), "Browser review found an issue; inspect review-done.json.");
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
            await factory.DeleteDatabaseAsync();
        }
    }
}
