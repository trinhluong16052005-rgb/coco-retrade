using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoCoReTrade.Tests;

// Opt-in. Creates and drops ONLY a new database whose name starts with CoCoReviewTests_.
public sealed class SqlCheckoutIntegrationTests
{
    [Fact]
    public async Task IsolatedSqlCheckoutAndOtpRegression()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        var database = "CoCoReviewTests_" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLogging(); services.AddDataProtection(); services.AddHttpContextAccessor(); services.AddAuthentication();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer($"Server=.\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True"));
        services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await db.Database.MigrateAsync();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole("User"))).Succeeded);
            foreach (var id in new[] { "buyer", "buyer2", "seller", "inspector", "admin", "otp" })
                Assert.True((await manager.CreateAsync(new ApplicationUser { Id = id, UserName = id + "@example.test", Email = id + "@example.test",
                    FullName = "Kiểm thử " + id, EmailConfirmed = id != "otp" })).Succeeded);
            var clock = new FixedClock();
            var p1 = Active("CG-2026-990101", 300000, clock.GetUtcNow().UtcDateTime);
            var p2 = Active("CG-2026-990102", 250000, clock.GetUtcNow().UtcDateTime);
            var p3 = Active("CG-2026-990103", 200000, clock.GetUtcNow().UtcDateTime);
            db.Products.AddRange(p1, p2, p3);
            db.CartItems.AddRange(new CartItem { UserId = "buyer", ProductId = p1.Id }, new CartItem { UserId = "buyer", ProductId = p2.Id }, new CartItem { UserId = "buyer", ProductId = p3.Id });
            await db.SaveChangesAsync();
            var service = Service(db, clock);
            var request = new BasketRequest([p1.Id, p2.Id], true, " giam10 ", null, Guid.NewGuid(),
                "Người kiểm thử", "0905551234", FulfillmentType.Shipping, null, "Đà Nẵng", "Hải Châu", "12 Đường kiểm thử", null);
            var quote = await service.QuoteAsync(request, "buyer", default);
            Assert.Equal(550000, quote.MerchandiseAmount); Assert.Equal(55000, quote.DiscountAmount);
            Assert.Equal(20000, quote.ShippingFee); Assert.Equal(515000, quote.TotalAmount);
            await Assert.ThrowsAsync<ArgumentException>(() => service.PlaceAsync(request with { RecipientPhone = "bad" }, "buyer", default));
            db.ChangeTracker.Clear();
            Assert.Equal(0, await db.Orders.CountAsync()); Assert.Equal(3, await db.Products.CountAsync(x => x.Status == ProductStatus.Active));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.QuoteAsync(request, "buyer2", default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QuoteAsync(request with { FromCart = false }, "seller", default));
            await Assert.ThrowsAsync<ArgumentException>(() => service.PlaceAsync(request with { ProductIds = [] }, "buyer", default));
            var result = await service.PlaceAsync(request, "buyer", default);
            Assert.Equal(2, result.OrderIds.Count); Assert.Equal(515000, result.TotalAmount);
            db.ChangeTracker.Clear();
            var orders = await db.Orders.OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(20000, orders.Sum(x => x.ShippingFee)); Assert.Equal(55000, orders.Sum(x => x.DiscountAmount));
            Assert.Equal(357500, orders.Sum(x => x.ItemAmount - x.CommissionAmount));
            Assert.Equal(1, await db.CartItems.CountAsync(x => x.UserId == "buyer"));
            Assert.Equal(p3.Id, (await db.CartItems.SingleAsync()).ProductId);
            var replay = await service.PlaceAsync(request, "buyer", default);
            Assert.Equal(result.BatchId, replay.BatchId); Assert.Equal(2, await db.Orders.CountAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceAsync(request with { RecipientName = "Đã đổi tên" }, "buyer", default));
            foreach (var order in orders)
            {
                var product = await db.Products.SingleAsync(x => x.Id == order.ProductId);
                order.MarkReady("TRACK-TEST"); order.ConfirmPayment("admin", "CASH-TEST", clock.GetUtcNow().UtcDateTime);
                order.Complete(product, clock.GetUtcNow().UtcDateTime);
                db.PlatformCommissionEntries.Add(new PlatformCommissionEntry(order, clock.GetUtcNow().UtcDateTime));
            }
            await db.SaveChangesAsync();
            Assert.Equal(137500, await db.PlatformCommissionEntries.SumAsync(x => x.Amount));
            // Conflict on any selected item must leave every other item untouched.
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceAsync(request with { ProductIds = [p1.Id, p3.Id], FromCart = false, IdempotencyKey = Guid.NewGuid() }, "buyer2", default));
            Assert.Equal(ProductStatus.Active, (await db.Products.SingleAsync(x => x.Id == p3.Id)).Status);
            var report = await new AdminFinancialReportRepository(db).SalesTotalsAsync(clock.GetUtcNow().UtcDateTime.AddDays(-1), clock.GetUtcNow().UtcDateTime.AddDays(1), default);
            Assert.Equal(137500, report.CommissionAmount);
            async Task<BasketResult?> Compete(string buyer)
            {
                await using var competitor = provider.CreateAsyncScope();
                try { return await Service(competitor.ServiceProvider.GetRequiredService<AppDbContext>(), clock).PlaceAsync(
                    request with { ProductIds = [p3.Id], FromCart = false, VoucherCode = null, IdempotencyKey = Guid.NewGuid() }, buyer, default); }
                catch (InvalidOperationException) { return null; }
            }
            var competitors = await Task.WhenAll(Compete("buyer"), Compete("buyer2"));
            Assert.Single(competitors, x => x is not null);
            Assert.Equal(1, await db.Orders.CountAsync(x => x.ProductId == p3.Id));
            // OTP is hashed, rate limited, limited to five attempts, single use, and expires after five minutes.
            var otpService = new AccountOtpService(db, manager, clock);
            var issued = await otpService.IssueActivationAsync("otp", default);
            Assert.NotEqual(issued.Code, (await db.AccountOtps.SingleAsync()).CodeHash);
            await Assert.ThrowsAsync<InvalidOperationException>(() => otpService.IssueActivationAsync("otp", default));
            var incorrect = issued.Code == "000000" ? "000001" : "000000";
            for (var i = 0; i < 5; i++) Assert.False((await otpService.ConfirmActivationAsync("otp", incorrect, default)).Succeeded);
            Assert.False((await otpService.ConfirmActivationAsync("otp", issued.Code, default)).Succeeded);
            clock.Advance(TimeSpan.FromSeconds(61));
            issued = await otpService.IssueActivationAsync("otp", default);
            clock.Advance(TimeSpan.FromMinutes(5));
            Assert.False((await otpService.ConfirmActivationAsync("otp", issued.Code, default)).Succeeded);
            clock.Advance(TimeSpan.FromSeconds(1)); issued = await otpService.IssueActivationAsync("otp", default);
            Assert.True((await otpService.ConfirmActivationAsync("otp", issued.Code, default)).Succeeded);
            Assert.False((await otpService.ConfirmActivationAsync("otp", issued.Code, default)).Succeeded);
            Assert.True((await manager.FindByIdAsync("otp"))!.EmailConfirmed);
            var external = new ExternalAccountService(db, manager, scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>());
            Assert.False((await external.GoogleSignInAsync(new("provider", "buyer@example.test", "Tên", false), default)).Succeeded);
            Assert.False((await external.GoogleSignInAsync(new("provider", "buyer@example.test", "Tên", true), default)).Succeeded);
            Assert.Empty(await db.UserLogins.ToListAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => new SmtpAccountMessageSender(new ConfigurationBuilder().Build()).SendOtpAsync("test@example.test", "123456", "activation"));
            // Removing the appointment control must still permit a real, idempotent pickup checkout.
            var pickupProduct = Active("CG-2026-990104", 200000, clock.GetUtcNow().UtcDateTime);
            var offerProduct = Active("CG-2026-990105", 300000, clock.GetUtcNow().UtcDateTime);
            var expiredProduct = Active("CG-2026-990106", 300000, clock.GetUtcNow().UtcDateTime);
            db.Products.AddRange(pickupProduct, offerProduct, expiredProduct);
            var acceptedOffer = Offer.Create(offerProduct, "buyer", 250000, 225000, 0, clock.GetUtcNow().UtcDateTime);
            acceptedOffer.Accept("seller", offerProduct.Status, clock.GetUtcNow().UtcDateTime);
            var expiredOffer = Offer.Create(expiredProduct, "buyer", 250000, 225000, 0, clock.GetUtcNow().UtcDateTime);
            expiredOffer.Accept("seller", expiredProduct.Status, clock.GetUtcNow().UtcDateTime);
            db.Offers.AddRange(acceptedOffer, expiredOffer);
            await db.SaveChangesAsync();
            var pickupRequest = request with { ProductIds = [pickupProduct.Id], FromCart = false, VoucherCode = null,
                IdempotencyKey = Guid.NewGuid(), Fulfillment = FulfillmentType.Pickup, PickupLocal = null,
                Province = null, Ward = null, StreetAddress = null };
            var pickupQuote = await service.QuoteAsync(pickupRequest, "buyer", default);
            Assert.Equal(0, pickupQuote.ShippingFee); Assert.Equal(200000, pickupQuote.TotalAmount);
            await Assert.ThrowsAsync<ArgumentException>(() => service.PlaceAsync(pickupRequest with { PickupLocal = "invalid" }, "buyer", default));
            db.ChangeTracker.Clear();
            var pickupResult = await service.PlaceAsync(pickupRequest, "buyer", default);
            var pickupOrder = await db.Orders.SingleAsync(x => x.Id == pickupResult.OrderIds[0]);
            Assert.Null(pickupOrder.PickupAtUtc); Assert.Null(pickupOrder.PickupDeadlineAtUtc);
            Assert.Equal("536/538 Núi Thành", pickupOrder.PickupAddress); Assert.Equal(0, pickupOrder.ShippingFee);
            Assert.Equal(pickupResult.BatchId, (await service.PlaceAsync(pickupRequest, "buyer", default)).BatchId);
            Assert.Equal(1, await db.Orders.CountAsync(x => x.ProductId == pickupProduct.Id));
            var offerRequest = pickupRequest with { ProductIds = [offerProduct.Id], OfferId = acceptedOffer.Id, IdempotencyKey = Guid.NewGuid() };
            var offerResult = await service.PlaceAsync(offerRequest, "buyer", default);
            var offerOrder = await db.Orders.SingleAsync(x => x.Id == offerResult.OrderIds[0]);
            Assert.Null(offerOrder.PickupAtUtc); Assert.Equal(acceptedOffer.ExpiresAtUtc, offerOrder.PickupDeadlineAtUtc);
            Assert.Equal(250000, offerOrder.ItemAmount);
            clock.Advance(TimeSpan.FromHours(24));
            var orderRepository = new OrderRepository(db);
            Assert.Contains(offerOrder.Id, await orderRepository.DueAcceptedPickupOrdersAsync(clock.GetUtcNow().UtcDateTime, 100, default));
            Assert.Contains(pickupOrder.Id, await orderRepository.DueUnpaidOrdersAsync(clock.GetUtcNow().UtcDateTime.AddHours(-24), 100, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceAsync(pickupRequest with {
                ProductIds = [expiredProduct.Id], OfferId = expiredOffer.Id, IdempotencyKey = Guid.NewGuid() }, "buyer", default));
        }
        finally
        {
            db.ChangeTracker.Clear();
            Assert.StartsWith("CoCoReviewTests_", db.Database.GetDbConnection().Database);
            await db.Database.EnsureDeletedAsync();
        }
    }
    private static BasketCheckoutService Service(AppDbContext db, TimeProvider clock) => new(new OrderRepository(db), new BasketRepository(db),
        new MarketplacePolicy(25, 35), new ShippingPolicy(storeAddress: "536/538 Núi Thành"), new TestShipping(), clock);
    private static Product Active(string code, decimal price, DateTime now)
    {
        var p = Product.Submit("seller", code, "Áo kiểm thử", "Zara", "Áo", "M", price * 2, price, 95, true,
            [new ProductImage("test-a.jpg", "image/jpeg", 0), new ProductImage("test-b.jpg", "image/jpeg", 1), new ProductImage("test-c.jpg", "image/jpeg", 2)]);
        p.ReviewPreliminarily("admin", PreliminaryReviewStatus.Approved, null, now);
        p.RecordInspection("inspector", true, p.Name, p.Brand, p.Category, p.Size, p.OriginalPrice, p.AskingPrice, 95, "checked", [], now);
        p.Publish(code.Replace("CG-", "CC-"), now); return p;
    }
    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 19, 3, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan time) => now += time;
    }
    private sealed class TestShipping : IShippingFeeService
    { public Task<decimal> FeeAsync(FulfillmentType type, string? province, string? ward, CancellationToken ct) => Task.FromResult(type == FulfillmentType.Pickup ? 0m : 20000m); }
}
