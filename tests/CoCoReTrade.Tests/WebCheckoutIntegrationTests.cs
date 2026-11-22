using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoCoReTrade.Tests;

public sealed class WebCheckoutIntegrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CartAndBuyNowCanPlaceStorePickupWithoutScheduling(bool fromCart)
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new ReviewFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var buyer = new ApplicationUser { UserName = "pickup-buyer@example.test", Email = "pickup-buyer@example.test", EmailConfirmed = true, FullName = "Người nhận tại CoCo" };
            Assert.True((await users.CreateAsync(buyer, factory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(buyer, "User")).Succeeded);
            var seller = new ApplicationUser { UserName = "pickup-seller@example.test", Email = "pickup-seller@example.test", EmailConfirmed = true, FullName = "Người ký gửi" };
            Assert.True((await users.CreateAsync(seller)).Succeeded);
            var admin = (await users.FindByEmailAsync("admin@cocoretrade.vn"))!;
            var product = Product(seller.Id, admin.Id, "990211", 300000);
            db.Products.Add(product);
            if (fromCart) db.CartItems.Add(new CartItem { UserId = buyer.Id, ProductId = product.Id });
            await db.SaveChangesAsync();
            await Login(client, buyer.Email!, factory.Password);

            var html = await client.GetStringAsync(fromCart ? "/Cart" : "/Checkout?productId=" + product.Id);
            Assert.DoesNotContain("datetime-local", html);
            Assert.DoesNotContain("name=\"PickupLocal\"", html);
            var submitted = new Dictionary<string, string>
            {
                ["FromCart"] = fromCart.ToString(), ["ProductIds"] = product.Id.ToString(),
                ["IdempotencyKey"] = Guid.NewGuid().ToString(), ["Fulfillment"] = "Pickup",
                ["RecipientName"] = "Người nhận tại CoCo", ["RecipientPhone"] = "0905551234"
            };
            if (!fromCart) submitted["ProductId"] = product.Id.ToString();
            var fields = Fields(html, submitted);
            var quoteResponse = await client.PostAsync("/Checkout/Quote", new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode);
            using var quote = JsonDocument.Parse(await quoteResponse.Content.ReadAsStringAsync());
            Assert.Equal(0, quote.RootElement.GetProperty("shippingFee").GetDecimal());
            Assert.Equal(300000, quote.RootElement.GetProperty("totalAmount").GetDecimal());

            var response = await client.PostAsync("/Checkout/Index", new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Orders/Details/", response.Headers.Location!.OriginalString);
            db.ChangeTracker.Clear();
            var order = await db.Orders.SingleAsync();
            Assert.Equal(FulfillmentType.Pickup, order.Fulfillment);
            Assert.Null(order.PickupAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(order.PickupAddress));
            Assert.Equal(0, order.ShippingFee);
            Assert.Equal(ProductStatus.Reserved, (await db.Products.SingleAsync(x => x.Id == product.Id)).Status);
            Assert.False(await db.CartItems.AnyAsync(x => x.UserId == buyer.Id));
            var replay = await client.PostAsync("/Checkout/Index", new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
            Assert.Equal(1, await db.Orders.CountAsync());
        }
        finally { await factory.DeleteDatabaseAsync(); }
    }

    [Fact]
    public async Task IsolatedHttpPagesPermissionsSelectedCheckoutAndVoucher()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new ReviewFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var buyer = new ApplicationUser { UserName = "buyer@example.test", Email = "buyer@example.test", EmailConfirmed = true, FullName = "Người kiểm thử" };
            Assert.True((await users.CreateAsync(buyer, factory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(buyer, "User")).Succeeded);
            var seller = new ApplicationUser { UserName = "seller@example.test", Email = "seller@example.test", EmailConfirmed = true, FullName = "Người ký gửi" };
            Assert.True((await users.CreateAsync(seller)).Succeeded);
            var admin = (await users.FindByEmailAsync("admin@cocoretrade.vn"))!;
            admin.MustChangePassword = false; Assert.True((await users.UpdateAsync(admin)).Succeeded);
            var products = new[] { Product(seller.Id, admin.Id, "990201", 300000), Product(seller.Id, admin.Id, "990202", 250000), Product(seller.Id, admin.Id, "990203", 150000) };
            db.Products.AddRange(products); await db.SaveChangesAsync();
            foreach (var p in products) db.CartItems.Add(new CartItem { UserId = buyer.Id, ProductId = p.Id });
            await db.SaveChangesAsync();
            var home = await client.GetStringAsync("/");
            Assert.Contains("class=\"sign-hero-video\"", home); Assert.Contains("playsinline", home); Assert.DoesNotContain("data-brand-pause", home);
            Assert.DoesNotContain("class=\"category-nav\"", home);
            var shop = await client.GetStringAsync("/Shop"); Assert.Contains("<dialog", shop); Assert.Contains("Mua ngay", shop);
            Assert.Contains("class=\"category-nav\"", shop); Assert.DoesNotContain("name=\"Fulfillment\"", shop);
            var details = await client.GetStringAsync("/Shop/Details/" + products[0].Id);
            Assert.DoesNotContain("Cách xem ảnh", details); Assert.DoesNotContain(">Phóng to<", details);
            Assert.Contains("Sản phẩm bạn có thể thích", details);
            var policies = await client.GetStringAsync("/About/Policies"); Assert.DoesNotContain("coco-fee-receipt", policies);
            Assert.Contains("65", policies);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Cart")).StatusCode);
            var register = await client.GetStringAsync("/Account/Register"); Assert.Contains("Tiếp tục với Google", register);
            var failedRegistration = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(Fields(register, new()
            { ["FullName"] = "Tên kiểm thử", ["Email"] = "new@example.test", ["Phone"] = "0905554567", ["Password"] = factory.Password, ["ConfirmPassword"] = factory.Password })));
            Assert.Equal(HttpStatusCode.OK, failedRegistration.StatusCode);
            Assert.Null(await users.FindByEmailAsync("new@example.test"));
            await Login(client, "buyer@example.test", factory.Password);
            var signedInShop = await client.GetStringAsync("/Shop");
            foreach (var selected in new[] { true, false })
            {
                using var favoriteRequest = new HttpRequestMessage(HttpMethod.Post, "/Social/Favorite")
                {
                    Content = new FormUrlEncodedContent(Fields(signedInShop, new()
                    { ["productId"] = products[0].Id.ToString(), ["selected"] = selected.ToString().ToLowerInvariant() }))
                };
                favoriteRequest.Headers.Accept.ParseAdd("application/json");
                var favoriteResponse = await client.SendAsync(favoriteRequest);
                Assert.Equal(HttpStatusCode.OK, favoriteResponse.StatusCode);
                using var favorite = JsonDocument.Parse(await favoriteResponse.Content.ReadAsStringAsync());
                Assert.Equal(selected, favorite.RootElement.GetProperty("selected").GetBoolean());
                var saved = await client.GetStringAsync("/Profile/Saved");
                Assert.Equal(selected, saved.Contains(products[0].Id.ToString(), StringComparison.Ordinal));
            }
            var cart = await client.GetStringAsync("/Cart");
            Assert.Contains("Thông tin nhận hàng", cart); Assert.Contains("Chọn tất cả", cart);
            var checkoutHtml = await client.GetStringAsync("/Checkout?productId=" + products[0].Id);
            Assert.DoesNotContain("class=\"category-nav\"", checkoutHtml);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Products")).StatusCode);
            var baseFields = new Dictionary<string, string> { ["FromCart"] = "true", ["IdempotencyKey"] = Guid.NewGuid().ToString(),
                ["RecipientName"] = "Người kiểm thử", ["RecipientPhone"] = "0905551234", ["Fulfillment"] = "Shipping", ["Province"] = "Đà Nẵng",
                ["Ward"] = "Hải Châu", ["StreetAddress"] = "12 Đường kiểm thử", ["VoucherCode"] = "GIAM10" };
            var quoteFields = Fields(cart, baseFields).ToList();
            quoteFields.Add(new("ProductIds", products[0].Id.ToString())); quoteFields.Add(new("ProductIds", products[1].Id.ToString()));
            var quoteResponse = await client.PostAsync("/Checkout/Quote", new FormUrlEncodedContent(quoteFields));
            Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode);
            using var quote = JsonDocument.Parse(await quoteResponse.Content.ReadAsStringAsync());
            Assert.Equal(550000, quote.RootElement.GetProperty("merchandiseAmount").GetDecimal());
            Assert.Equal(55000, quote.RootElement.GetProperty("discountAmount").GetDecimal());
            Assert.Equal(515000, quote.RootElement.GetProperty("totalAmount").GetDecimal());
            var noToken = await client.PostAsync("/Checkout/Index", new FormUrlEncodedContent(baseFields));
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
            var placed = await client.PostAsync("/Checkout/Index", new FormUrlEncodedContent(quoteFields));
            Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
            db.ChangeTracker.Clear(); Assert.Equal(2, await db.Orders.CountAsync());
            Assert.Equal(1, await db.CartItems.CountAsync(x => x.UserId == buyer.Id));
            var replay = await client.PostAsync("/Checkout/Index", new FormUrlEncodedContent(quoteFields));
            Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode); Assert.Equal(2, await db.Orders.CountAsync());
            var order = await db.Orders.FirstAsync(); var orderHtml = await client.GetStringAsync("/Orders/Details/" + order.Id);
            Assert.Contains("GIAM10", orderHtml);
            // Admin can remove a nonreserved product, but missing/invalid tokens never remove it.
            var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await Login(adminClient, "admin@cocoretrade.vn", factory.Password);
            var adminProducts = await adminClient.GetStringAsync("/Admin/Products"); Assert.Contains("Quản lý CoCo", adminProducts); Assert.Contains("data-admin-single-delete", adminProducts);
            var third = await db.Products.AsNoTracking().SingleAsync(x => x.Id == products[2].Id);
            var deleteFields = Fields(adminProducts, new() { ["id"] = third.Id.ToString(), ["expectedRowVersion"] = Convert.ToBase64String(third.RowVersion) });
            var unauthorizedDelete = await client.PostAsync("/Admin/Products/Delete", new FormUrlEncodedContent(Fields(cart, new()
            { ["id"] = third.Id.ToString(), ["expectedRowVersion"] = Convert.ToBase64String(third.RowVersion) })));
            Assert.Equal(HttpStatusCode.Redirect, unauthorizedDelete.StatusCode);
            var missingTokenDelete = await adminClient.PostAsync("/Admin/Products/Delete", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["id"] = third.Id.ToString(), ["expectedRowVersion"] = Convert.ToBase64String(third.RowVersion) }));
            Assert.Equal(HttpStatusCode.BadRequest, missingTokenDelete.StatusCode);
            Assert.Equal(ProductStatus.Active, (await db.Products.AsNoTracking().SingleAsync(x => x.Id == third.Id)).Status);
            var deleted = await adminClient.PostAsync("/Admin/Products/Delete", new FormUrlEncodedContent(deleteFields));
            Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
            db.ChangeTracker.Clear(); Assert.Equal(ProductStatus.Removed, (await db.Products.SingleAsync(x => x.Id == third.Id)).Status);
            Assert.Equal(2, await db.Orders.CountAsync());
        }
        finally { client.Dispose(); await factory.DeleteDatabaseAsync(); }
    }
    private static async Task Login(HttpClient client, string email, string password)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var result = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(Fields(html, new() { ["Identifier"] = email, ["Password"] = password })));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }
    private static Dictionary<string, string> Fields(string html, Dictionary<string, string> values)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Antiforgery token must be rendered.");
        var fields = new Dictionary<string, string>(values)
        { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value) };
        return fields;
    }
    private static Product Product(string seller, string inspector, string code, decimal price)
    {
        var now = DateTime.UtcNow;
        var p = CoCoReTrade.Domain.Product.Submit(seller, "CG-2026-" + code, "Áo kiểm thử giao diện", "Zara", "Áo", "M", price * 2, price, 95, true,
            [new ProductImage("test-a.jpg", "image/jpeg", 0), new ProductImage("test-b.jpg", "image/jpeg", 1), new ProductImage("test-c.jpg", "image/jpeg", 2)]);
        p.ReviewPreliminarily(inspector, PreliminaryReviewStatus.Approved, null, now);
        p.RecordInspection(inspector, true, p.Name, p.Brand, p.Category, p.Size, p.OriginalPrice, p.AskingPrice, 95, "checked", [], now);
        p.Publish("CC-2026-" + code, now); return p;
    }
    private sealed class ReviewFactory : WebApplicationFactory<Program>
    {
        private readonly string database = "CoCoReviewWebTests_" + Guid.NewGuid().ToString("N");
        public string Password { get; } = "CocoTest7" + Guid.NewGuid().ToString("N");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connection = $"Server=.\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:CoCo"] = connection, ["Demo:Enabled"] = "false", ["Preview:Enabled"] = "false",
                ["Shipping:StoreAddress"] = "12 Đường kiểm thử, Đà Nẵng",
                ["Admin:InitialPassword"] = Password, ["Smtp:Host"] = "", ["Smtp:From"] = "", ["Smtp:User"] = "", ["Smtp:Password"] = "",
                ["Authentication:Google:ClientId"] = "", ["Authentication:Google:ClientSecret"] = "" }));
            builder.ConfigureServices(services =>
            {
                // Host startup can register Google before test configuration overrides run.
                // Isolate readiness and options as well as the configuration providers.
                services.RemoveAll<CoCoReTrade.Application.AccountDeliveryStatus>();
                services.AddSingleton(new CoCoReTrade.Application.AccountDeliveryStatus(false, false));
                services.PostConfigure<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>("Google", options =>
                {
                    options.ClientId = "isolated-http-test.apps.googleusercontent.com";
                    options.ClientSecret = "fake-local-test-only";
                });
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connection));
                var workers = services.Where(x => new[] { "OfferExpirationWorker", "OrderExpirationWorker", "OutboxDispatcherWorker", "ListingReminderWorker" }.Contains(x.ImplementationType?.Name)).ToList();
                foreach (var worker in workers) services.Remove(worker);
            });
        }
        public async Task DeleteDatabaseAsync()
        {
            using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.StartsWith("CoCoReviewWebTests_", db.Database.GetDbConnection().Database); await db.Database.EnsureDeletedAsync();
        }
    }
}
