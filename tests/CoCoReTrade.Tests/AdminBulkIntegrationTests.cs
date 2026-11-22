using System.Net;
using System.Text.RegularExpressions;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoCoReTrade.Tests;

// Opt-in: creates and drops only its own fresh CoCoAdminBulkTests_ database.
public sealed class AdminBulkIntegrationTests
{
    [Fact]
    public async Task IsolatedAdminBulkDeletionPreservesHistoryAndEnforcesPermissionsAndAtomicity()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new BulkFactory();
        using var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var buyerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await users.FindByEmailAsync("admin@cocoretrade.vn"))!;
            admin.MustChangePassword = false;
            Assert.True((await users.UpdateAsync(admin)).Succeeded);
            foreach (var id in new[] { "member-a", "member-b", "member-c", "member-d", "seller", "wallet-user" })
            {
                var user = new ApplicationUser { Id = id, UserName = id + "@example.test", Email = id + "@example.test",
                    EmailConfirmed = id != "member-b", FullName = "Thành viên " + id };
                Assert.True((await users.CreateAsync(user, factory.Password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, "User")).Succeeded);
            }
            var first = Active("seller", admin.Id, "991001");
            var second = Active("seller", admin.Id, "991002");
            var reserved = Active("seller", admin.Id, "991003");
            reserved.Reserve();
            db.Products.AddRange(first, second, reserved);
            db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = "wallet-user", Balance = 100000 });
            db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = "member-c", Balance = 0 });
            db.Favorites.Add(new Favorite { UserId = "member-a", ProductId = first.Id, CreatedAtUtc = DateTime.UtcNow });
            db.CartItems.Add(new CartItem { UserId = "member-a", ProductId = second.Id, CreatedAtUtc = DateTime.UtcNow });
            db.Addresses.Add(new Address { Id = Guid.NewGuid(), UserId = "member-a", RecipientName = "Thành viên kiểm thử",
                RecipientPhone = "0905551234", Province = "Đà Nẵng", Ward = "Hải Châu", StreetAddress = "12 Đường kiểm thử" });
            db.SellerFollows.AddRange(new SellerFollow { FollowerId = "member-c", SellerId = "seller" },
                new SellerFollow { FollowerId = "member-d", SellerId = "member-c" });
            var privateNotificationId = Guid.NewGuid();
            db.MarketplaceNotifications.Add(new MarketplaceNotification { Id = privateNotificationId, UserId = "member-b",
                ProductId = first.Id, Category = "Listings", Message = "Thông báo kiểm thử", CreatedAtUtc = DateTime.UtcNow });
            db.SupportTickets.Add(SupportTicket.Create("member-d", "Yêu cầu hỗ trợ kiểm thử", "Nội dung hỗ trợ cần giữ lại", DateTime.UtcNow));
            await db.SaveChangesAsync();
            var memberC = (await users.FindByIdAsync("member-c"))!;
            Assert.True((await users.AddLoginAsync(memberC, new UserLoginInfo("Google", "isolated-member-c", "Google"))).Succeeded);
            Assert.True((await users.SetAuthenticationTokenAsync(memberC, "Google", "test-token", "fake-local-test-only")).Succeeded);
            Assert.True((await users.AddClaimAsync(memberC, new System.Security.Claims.Claim("test-private-claim", "member-c"))).Succeeded);
            var otpService = scope.ServiceProvider.GetRequiredService<IAccountOtpService>();
            var resetBeforeDelete = await otpService.IssueResetAsync("member-a", default);
            var activationBeforeDelete = await otpService.IssueActivationAsync("member-b", default);
            var oldUnconfirmed = (await users.FindByIdAsync("member-b"))!;
            var legacyConfirmationToken = await users.GenerateEmailConfirmationTokenAsync(oldUnconfirmed);

            await Login(adminClient, "admin@cocoretrade.vn", factory.Password);
            await Login(buyerClient, "member-a@example.test", factory.Password);
            var buyerProfile = await buyerClient.GetStringAsync("/Profile");
            var adminProducts = await adminClient.GetStringAsync("/Admin/Products");
            Assert.Contains("data-admin-bulk", adminProducts);
            Assert.Contains("admin-product-thumb", adminProducts);
            Assert.Contains("/images/" + first.Images.OrderBy(x => x.SortOrder).First().Id, adminProducts);
            Assert.DoesNotContain(">Xóa</button>", adminProducts);
            var adminUsers = await adminClient.GetStringAsync("/Admin/Users");
            Assert.Contains("bulk-user-delete", adminUsers);
            Assert.Contains("data-bulk-all", adminUsers);
            Assert.DoesNotContain(">Xóa</button>", adminUsers);

            // A normal account and a missing CSRF token cannot invoke admin deletion.
            var denied = await buyerClient.PostAsync("/Admin/Users/DeleteSelected", Form(buyerProfile,
                [("selectedIds", "member-b")]));
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            var noToken = await adminClient.PostAsync("/Admin/Products/DeleteSelected", new FormUrlEncodedContent(
                new[] { new KeyValuePair<string, string>("selectedIds", first.Id.ToString()) }));
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

            // A stale version in a batch prevents every selected product from changing.
            var stale = await adminClient.PostAsync("/Admin/Products/DeleteSelected", Form(adminProducts,
                [("selectedIds", first.Id.ToString()), ("selectedIds", second.Id.ToString()),
                 ($"versions[{first.Id}]", Convert.ToBase64String(first.RowVersion)), ($"versions[{second.Id}]", Convert.ToBase64String([1, 2]))]));
            Assert.Equal(HttpStatusCode.Redirect, stale.StatusCode);
            Assert.Equal(3, await db.Products.AsNoTracking().CountAsync(x => x.Status != ProductStatus.Removed));
            var reservedBatch = await adminClient.PostAsync("/Admin/Products/DeleteSelected", Form(adminProducts,
                [("selectedIds", first.Id.ToString()), ("selectedIds", reserved.Id.ToString()),
                 ($"versions[{first.Id}]", Convert.ToBase64String(first.RowVersion)), ($"versions[{reserved.Id}]", Convert.ToBase64String(reserved.RowVersion))]));
            Assert.Equal(HttpStatusCode.Redirect, reservedBatch.StatusCode);
            Assert.Equal(3, await db.Products.AsNoTracking().CountAsync(x => x.Status != ProductStatus.Removed));
            var removed = await adminClient.PostAsync("/Admin/Products/DeleteSelected", Form(adminProducts,
                [("selectedIds", first.Id.ToString()), ("selectedIds", second.Id.ToString()),
                 ($"versions[{first.Id}]", Convert.ToBase64String(first.RowVersion)), ($"versions[{second.Id}]", Convert.ToBase64String(second.RowVersion))]));
            Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
            Assert.Equal(2, await db.Products.AsNoTracking().CountAsync(x => x.Status == ProductStatus.Removed));
            Assert.Equal(9, await db.ProductImages.CountAsync());
            Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "Products.Deleted"));
            Assert.Single(await new AdminProductRepository(db).ListAsync(null, null, default));

            // A protected account, unsettled wallet or historical reference aborts the whole batch.
            await adminClient.PostAsync("/Admin/Users/DeleteSelected", Form(adminUsers,
                [("selectedIds", "member-b"), ("selectedIds", admin.Id)]));
            Assert.False(await db.UserClaims.AnyAsync(x => x.ClaimType == AdminAccountDeletion.DeletedAtClaim));
            await adminClient.PostAsync("/Admin/Users/DeleteSelected", Form(adminUsers,
                [("selectedIds", "member-b"), ("selectedIds", "wallet-user")]));
            Assert.False(await db.UserClaims.AnyAsync(x => x.ClaimType == AdminAccountDeletion.DeletedAtClaim));
            await adminClient.PostAsync("/Admin/Users/Delete", Form(adminUsers, [("id", "seller")]));
            Assert.False(await db.UserClaims.AnyAsync(x => x.ClaimType == AdminAccountDeletion.DeletedAtClaim));
            await adminClient.PostAsync("/Admin/Users/DeleteSelected", Form(adminUsers,
                [("selectedIds", "member-b"), ("selectedIds", "member-d")]));
            Assert.True(await db.Users.AnyAsync(x => x.Id == "member-b"));
            Assert.True(await db.Users.AnyAsync(x => x.Id == "member-d"));
            Assert.Single(await db.SupportTickets.ToListAsync());
            Assert.False(await db.AuditLogs.AnyAsync(x => x.Action == "Users.Deleted"));

            var userRemoved = await adminClient.PostAsync("/Admin/Users/DeleteSelected", Form(adminUsers,
                [("selectedIds", "member-b"), ("selectedIds", "member-c")]));
            Assert.Equal(HttpStatusCode.Redirect, userRemoved.StatusCode);
            Assert.False(await db.Users.AnyAsync(x => x.Id == "member-b" || x.Id == "member-c"));
            var oneRemoved = await adminClient.PostAsync("/Admin/Users/Delete", Form(adminUsers, [("id", "member-a")]));
            Assert.Equal(HttpStatusCode.Redirect, oneRemoved.StatusCode);
            db.ChangeTracker.Clear();
            Assert.False(await db.Users.AnyAsync(x => x.Id == "member-a"));
            Assert.Null(await users.FindByIdAsync("member-a"));
            Assert.Equal(3, await db.AuditLogs.CountAsync(x => x.Action == "Users.Deleted"));
            Assert.False(await db.UserClaims.AnyAsync(x => x.UserId == "member-a" || x.UserId == "member-b" || x.UserId == "member-c"));
            Assert.False(await db.UserRoles.AnyAsync(x => x.UserId == "member-a" || x.UserId == "member-b" || x.UserId == "member-c"));
            Assert.False(await db.UserLogins.AnyAsync(x => x.UserId == "member-c"));
            Assert.False(await db.UserTokens.AnyAsync(x => x.UserId == "member-c"));
            Assert.False(await db.Favorites.AnyAsync(x => x.UserId == "member-a"));
            Assert.False(await db.CartItems.AnyAsync(x => x.UserId == "member-a"));
            Assert.False(await db.Addresses.AnyAsync(x => x.UserId == "member-a"));
            Assert.False(await db.SellerFollows.AnyAsync(x => x.FollowerId == "member-c" || x.SellerId == "member-c"));
            Assert.False(await db.MarketplaceNotifications.AnyAsync(x => x.UserId == "member-b"));
            Assert.False(await db.OutboxMessages.AnyAsync(x => x.NotificationId == privateNotificationId));
            Assert.False(await db.Wallets.AnyAsync(x => x.UserId == "member-c"));
            var repository = new AdminUserRepository(db, users);
            Assert.Null(await repository.FindAsync("member-a", default));
            Assert.DoesNotContain(await repository.SearchAsync(null, default), x => x.Id.StartsWith("member-") && x.Id != "member-d");
            Assert.Equal(100000, (await db.Wallets.SingleAsync()).Balance);
            Assert.Equal(3, await db.Products.CountAsync());
            Assert.Equal(HttpStatusCode.Redirect, (await buyerClient.GetAsync("/Profile")).StatusCode);
            var accountService = scope.ServiceProvider.GetRequiredService<IAccountService>();
            Assert.False((await accountService.LoginAsync("member-a@example.test", factory.Password, false)).Succeeded);
            Assert.Null((await accountService.CreateResetTokenAsync("member-a@example.test")).UserId);
            Assert.False((await accountService.ResetPasswordAsync("member-a", "invalid-token", factory.Password)).Succeeded);
            var otpCount = await db.AccountOtps.CountAsync();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => otpService.IssueResetAsync("member-a", default));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => otpService.IssueActivationAsync("member-b", default));
            Assert.False((await otpService.VerifyResetAsync("member-a", resetBeforeDelete.Code, default)).Succeeded);
            Assert.False((await otpService.ConfirmActivationAsync("member-b", activationBeforeDelete.Code, default)).Succeeded);
            Assert.False((await accountService.ConfirmEmailAsync("member-b", legacyConfirmationToken)).Succeeded);
            db.ChangeTracker.Clear();
            Assert.Null(await users.FindByIdAsync("member-b"));
            Assert.Equal(0, otpCount);
            Assert.Equal(otpCount, await db.AccountOtps.CountAsync());
            Assert.False(await db.AccountOtps.AnyAsync(x => x.ConsumedAtUtc != null));
        }
        finally { await factory.DeleteDatabaseAsync(); }
    }

    [Fact]
    public async Task PermanentDeletionPreservesArchivedProductsAndSettledWalletHistory()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new BulkFactory();
        using var startupClient = factory.CreateClient();
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await manager.FindByEmailAsync("admin@cocoretrade.vn"))!;
            foreach (var id in new[] { "fresh-candidate", "historic-owner", "historic-wallet" })
                Assert.True((await manager.CreateAsync(new ApplicationUser { Id = id, UserName = id + "@example.test",
                    Email = id + "@example.test", FullName = "Thành viên kiểm thử " + id, EmailConfirmed = true })).Succeeded);
            var product = Product.Submit("historic-owner", "CG-2026-998991", "Áo đã gỡ khỏi cửa hàng", "CoCo", "Áo", "M",
                600000, 300000, 90, true, [new ProductImage("history-a.jpg", "image/jpeg", 0),
                    new ProductImage("history-b.jpg", "image/jpeg", 1), new ProductImage("history-c.jpg", "image/jpeg", 2)]);
            product.RemoveFromCatalog();
            db.Products.Add(product);
            var wallet = new Wallet { Id = Guid.NewGuid(), UserId = "historic-wallet", Balance = 0 };
            db.Wallets.Add(wallet);
            db.WalletTransactions.AddRange(
                new WalletTransaction { Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 100000, Type = "Adjustment", CreatedAtUtc = DateTime.UtcNow },
                new WalletTransaction { Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = -100000, Type = "Withdrawal", CreatedAtUtc = DateTime.UtcNow });
            db.SellerFollows.Add(new SellerFollow { FollowerId = "fresh-candidate", SellerId = "historic-owner" });
            await db.SaveChangesAsync();
            var repository = new AdminUserRepository(db, manager);
            foreach (var protectedId in new[] { "historic-owner", "historic-wallet" })
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteManyAsync(
                    ["fresh-candidate", protectedId], admin.Id, DateTime.UtcNow, default));
                Assert.Contains("Không thể xóa vĩnh viễn", error.Message);
                Assert.True(await db.Users.AnyAsync(x => x.Id == "fresh-candidate"));
                Assert.True(await db.Users.AnyAsync(x => x.Id == protectedId));
                Assert.Single(await db.SellerFollows.ToListAsync());
                Assert.False(await db.AuditLogs.AnyAsync(x => x.Action == "Users.Deleted"));
            }
            Assert.Equal(ProductStatus.Removed, (await db.Products.SingleAsync()).Status);
            Assert.Equal(3, await db.ProductImages.CountAsync());
            Assert.Equal(2, await db.WalletTransactions.CountAsync());
            Assert.Equal(0, (await db.Wallets.SingleAsync()).Balance);
            await repository.DeleteManyAsync(["fresh-candidate"], admin.Id, DateTime.UtcNow, default);
            Assert.False(await db.Users.AnyAsync(x => x.Id == "fresh-candidate"));
            Assert.True(await db.Users.AnyAsync(x => x.Id == "historic-owner"));
            Assert.True(await db.Users.AnyAsync(x => x.Id == "historic-wallet"));
            Assert.Single(await db.Products.ToListAsync());
            Assert.Equal(2, await db.WalletTransactions.CountAsync());
        }
        finally { await factory.DeleteDatabaseAsync(); }
    }

    private static FormUrlEncodedContent Form(string html, IEnumerable<(string Key, string Value)> values)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        return new FormUrlEncodedContent(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value))
            .Append(new("__RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value))));
    }
    private static async Task Login(HttpClient client, string email, string password)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var result = await client.PostAsync("/Account/Login", Form(html, [("Identifier", email), ("Password", password)]));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }
    private static Product Active(string seller, string inspector, string code)
    {
        var now = DateTime.UtcNow;
        var p = Product.Submit(seller, "CG-2026-" + code, "Áo kiểm thử quản trị", "Zara", "Áo", "M", 600000, 300000, 95, true,
            [new ProductImage("a.jpg", "image/jpeg", 0), new ProductImage("b.jpg", "image/jpeg", 1), new ProductImage("c.jpg", "image/jpeg", 2)]);
        p.ReviewPreliminarily(inspector, PreliminaryReviewStatus.Approved, null, now);
        p.RecordInspection(inspector, true, p.Name, p.Brand, p.Category, p.Size, p.OriginalPrice, p.AskingPrice, 95, "Đã kiểm tra", [], now);
        p.Publish("CC-2026-" + code, now);
        return p;
    }
    private sealed class BulkFactory : WebApplicationFactory<Program>
    {
        private readonly string database = "CoCoAdminBulkTests_" + Guid.NewGuid().ToString("N");
        public string Password { get; } = "CocoBulk7" + Guid.NewGuid().ToString("N");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connection = $"Server=.\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:CoCo"] = connection, ["Preview:Enabled"] = "false", ["Demo:Enabled"] = "false",
                    ["Admin:InitialPassword"] = Password, ["Smtp:Host"] = "", ["Smtp:From"] = "", ["Smtp:User"] = "", ["Smtp:Password"] = "",
                    ["Notifications:MarketplaceEmailEnabled"] = "false", ["Authentication:Google:ClientId"] = "", ["Authentication:Google:ClientSecret"] = ""
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AccountDeliveryStatus>();
                services.AddSingleton(new AccountDeliveryStatus(false, false));
                services.PostConfigure<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>("Google", options =>
                {
                    options.ClientId = "isolated-admin-test.apps.googleusercontent.com";
                    options.ClientSecret = "fake-local-test-only";
                });
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connection));
                foreach (var worker in services.Where(x => new[] { "OfferExpirationWorker", "OrderExpirationWorker", "OutboxDispatcherWorker", "ListingReminderWorker" }.Contains(x.ImplementationType?.Name)).ToList())
                    services.Remove(worker);
            });
        }
        public async Task DeleteDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.StartsWith("CoCoAdminBulkTests_", db.Database.GetDbConnection().Database);
            await db.Database.EnsureDeletedAsync();
        }
    }
}
