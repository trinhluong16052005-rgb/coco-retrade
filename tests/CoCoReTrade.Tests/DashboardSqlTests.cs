using System.Net;
using System.Net.Http.Json;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CoCoReTrade.Tests.AdminSqlAuditFactory;

namespace CoCoReTrade.Tests;

public sealed class DashboardSqlTests
{
    internal sealed class DashboardClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-11-22T10:00:00Z"); }
    [Fact]
    public async Task DashboardUsesActualSqlDatesPaidCompletedNetAmountsAndPermissionsWithoutDoubleCounting()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new AdminSqlAuditFactory(new DashboardClock()); using var bootstrap = factory.CreateClient();
        try
        {
            await factory.InitializeUsersAsync();
            await Seed(factory);
            using var admin = await factory.LoginAsync("admin");
            var data = await Success<AdminDashboardOverview>(await admin.GetAsync("/api/v1/admin/dashboard?period=7d"));
            Assert.Equal(6, data.Metrics.Count); Assert.Equal(7, data.Series.Count);
            Assert.True(data.CanViewFinancials); Assert.True(data.CanViewOrders);
            DashboardMeasure Metric(string key) => data.Metrics.Single(x => x.Key == key);
            Assert.Equal(2, Metric("pending").Value); Assert.Equal(1, Metric("pending").Previous);
            Assert.Equal(2, Metric("active").Value); Assert.Equal(1, Metric("active").Previous);
            Assert.Equal(1, Metric("held").Value); Assert.Equal(0, Metric("held").Previous);
            Assert.Equal(2, Metric("shipping").Value);
            Assert.Equal(3, Metric("sold").Value); Assert.Equal(2, Metric("sold").Previous);
            Assert.Equal(2, Metric("withdrawals").Value); Assert.Equal(1, Metric("withdrawals").Previous);
            var revenue = data.Business.Single(x => x.Key == "revenue");
            var commission = data.Business.Single(x => x.Key == "commission");
            Assert.Equal(330_000, revenue.Value); Assert.Equal(380_000, revenue.Previous); Assert.Equal(-13.2m, revenue.Percent);
            Assert.Equal(102_500, commission.Value); Assert.Equal(133_000, commission.Previous);
            Assert.Equal(revenue.Value, data.Series.Sum(x => x.Revenue));
            Assert.Equal(commission.Value, data.Series.Sum(x => x.Commission));
            Assert.Equal(Metric("sold").Value, data.Series.Sum(x => x.SoldProducts));
            Assert.Equal(data.Business.Single(x => x.Key == "orders").Value, data.OrderStatuses.Sum(x => x.Count));
            Assert.Equal(data.OrderStatuses.Sum(x => x.Count), data.Series.Sum(x => x.NewOrders));
            Assert.Equal(1, data.OrderStatuses.Single(x => x.Key == "Cancelled").Count);
            Assert.Equal(2, data.Series.First().SoldProducts); // Includes exactly Vietnam midnight, excludes the second before it.
            Assert.Equal(150000, data.Series.First().Revenue);
            Assert.Equal(1, data.Series.Last().SoldProducts);
            Assert.Equal(180000, data.Series.Last().Revenue);
            var custom = await Success<AdminDashboardOverview>(await admin.GetAsync("/api/v1/admin/dashboard?period=custom&from=2026-11-22&through=2026-11-22"));
            Assert.Single(custom.Series); Assert.Equal(180000, custom.Business.Single(x => x.Key == "revenue").Value);
            foreach (var period in new[] { "30d", "3m", "12m" })
            {
                var range = await Success<AdminDashboardOverview>(await admin.GetAsync("/api/v1/admin/dashboard?period=" + period));
                Assert.True(range.Series.Count <= 31); Assert.Equal(range.Range.From, range.Series.First().From);
                Assert.Equal(range.Range.Through, range.Series.Last().Through);
                Assert.Equal(range.Range.Days, range.Series.Sum(x => x.Through.DayNumber - x.From.DayNumber + 1));
            }
            var empty = await Success<AdminDashboardOverview>(await admin.GetAsync("/api/v1/admin/dashboard?period=custom&from=2020-01-01&through=2020-01-07"));
            Assert.All(empty.Metrics, m => Assert.Equal(0, m.Value)); Assert.All(empty.Business, m => Assert.Equal(0, m.Value));
            Assert.All(empty.OrderStatuses, x => Assert.Equal(0, x.Count)); Assert.All(empty.Series, x => Assert.Equal(0, x.Revenue));
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/dashboard?period=custom&from=2026-11-23&through=2026-11-24")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/dashboard?period=custom&from=invalid")).StatusCode);
            var html = await admin.GetStringAsync("/Admin/Dashboard");
            Assert.DoesNotContain("Thao tác nhanh", html);
            Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(html, "data-metric=").Count);
            Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "data-chart=").Count);
            var response = await admin.GetAsync("/api/v1/admin/dashboard");
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
            using var buyer = await factory.LoginAsync("buyer");
            Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync("/api/v1/admin/dashboard")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await bootstrap.GetAsync("/api/v1/admin/dashboard")).StatusCode);
            using var inspector = await factory.LoginAsync("member");
            var limited = await Success<AdminDashboardOverview>(await inspector.GetAsync("/api/v1/admin/dashboard?period=7d"));
            Assert.False(limited.CanViewFinancials); Assert.False(limited.CanViewOrders); Assert.Empty(limited.Business); Assert.Empty(limited.OrderStatuses);
            Assert.All(limited.Series, x => { Assert.Null(x.Revenue); Assert.Null(x.Commission); Assert.Null(x.NewOrders); });
            Assert.Null(limited.Metrics.Single(x => x.Key == "withdrawals").Value);
            Assert.Equal(3, limited.Metrics.Single(x => x.Key == "sold").Value);
            var inspectorPage = await inspector.GetStringAsync("/Admin/Dashboard");
            Assert.DoesNotContain("data-chart=\"revenue\"", inspectorPage);
            Assert.DoesNotContain("data-chart=\"orders\"", inspectorPage);
        }
        finally { await factory.DeleteDatabaseAsync(); }
    }

    internal static async Task Seed(AdminSqlAuditFactory factory)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync("admin@cocoretrade.vn"))!;
        var inspector = (await users.FindByIdAsync("member"))!;
        Assert.True((await users.AddToRoleAsync(inspector, "Inspector")).Succeeded);
        var sequence = 996000;
        DateTime Utc(int day, int hour = 9, int minute = 0, int second = 0) => new(2026, 11, day, hour, minute, second, DateTimeKind.Utc);
        Product Item(DateTime created, bool active = true, decimal price = 200000)
        {
            var n = ++sequence;
            var item = Product.Submit("seller", "CG-2026-" + n, "Sản phẩm thống kê " + n, "CoCo", "Áo", "M", price * 2, price, 95, true,
                [new ProductImage("a.jpg", "image/jpeg", 0), new ProductImage("b.jpg", "image/jpeg", 1), new ProductImage("c.jpg", "image/jpeg", 2)]);
            if (active)
            {
                item.ReviewPreliminarily(admin.Id, PreliminaryReviewStatus.Approved, null, created);
                item.RecordInspection(admin.Id, true, item.Name, item.Brand, item.Category, item.Size, item.OriginalPrice, item.AskingPrice, 95, "Đã kiểm tra", [], created);
                item.Publish("CC-2026-" + n, created);
            }
            db.Products.Add(item); db.Entry(item).Property(x => x.CreatedAtUtc).CurrentValue = created;
            return item;
        }
        Order NewOrder(Product item, DateTime created, bool pickup = false)
        {
            var order = Order.Create(item, null, "buyer", Guid.NewGuid(), pickup ? FulfillmentType.Pickup : FulfillmentType.Shipping,
                "Người kiểm thử", "0905551234", pickup ? null : "Đà Nẵng", pickup ? null : "Hải Châu", pickup ? null : "12 Đường kiểm thử",
                null, pickup ? "536/538 Núi Thành" : null, pickup ? 0 : 20000, 35, created);
            item.Reserve(); db.Orders.Add(order); return order;
        }
        void Sale(decimal price, DateTime created, DateTime completed, decimal discount = 0)
        {
            var item = Item(created, price: price); var order = NewOrder(item, created);
            if (discount > 0) { var batch = new CheckoutBatch("buyer", Guid.NewGuid(), new string('a', 64), created); db.CheckoutBatches.Add(batch); order.AttachCheckout(batch.Id, 20000, discount, "TEST"); }
            order.MarkReady("TEST-SHIP"); order.ConfirmPayment(admin.Id, "TEST-PAID", completed); order.Complete(item, completed);
            db.PlatformCommissionEntries.Add(new PlatformCommissionEntry(order, completed));
        }
        Sale(200000, Utc(21), Utc(22), 20000);
        Sale(100000, Utc(10), Utc(16)); // Completed this period, ordered in previous period.
        Sale(50000, Utc(10), Utc(15, 17)); // Exactly midnight Vietnam of November 16.
        Sale(300000, Utc(9), Utc(10));
        Sale(80000, Utc(8), Utc(15, 16, 59, 59)); // Immediately before the current period.
        // Pending and shipped orders never contribute to realized revenue.
        NewOrder(Item(Utc(22)), Utc(22));
        var shipped = NewOrder(Item(Utc(22)), Utc(22)); shipped.MarkReady("TEST-SHIP");
        var held = NewOrder(Item(Utc(22)), Utc(22), true);
        var expired = NewOrder(Item(Utc(10)), Utc(10), true);
        var cancelItem = Item(Utc(22)); var cancelled = NewOrder(cancelItem, Utc(22)); cancelled.Cancel(cancelItem, true, Utc(22));
        Item(Utc(22)); Item(Utc(10)); Item(Utc(22), false); Item(Utc(22), false); Item(Utc(10), false);
        // A legacy completed-but-unpaid row is not recognized as revenue or a successful sale.
        var unpaid = NewOrder(Item(Utc(22)), Utc(22));
        db.Entry(unpaid).Property(x => x.Status).CurrentValue = OrderStatus.Completed;
        db.Entry(unpaid).Property(x => x.CompletedAtUtc).CurrentValue = Utc(22);
        var wallet = await db.Wallets.SingleAsync(x => x.UserId == "seller");
        for (var i = 0; i < 5; i++)
        {
            var withdrawal = new WithdrawalRequest { Id = Guid.NewGuid(), WalletId = wallet.Id, RequestKey = Guid.NewGuid(),
                Amount = 1000, BankName = "Vietcombank", BankAccountNumber = "0123456789", BankAccountMasked = "****6789", CreatedAtUtc = i == 4 ? Utc(10) : Utc(22) };
            if (i is 1 or 2) withdrawal.Approve(admin.Id, Utc(22));
            if (i == 2) withdrawal.ConfirmTransferred(admin.Id, "TEST-TRANSFER", Utc(22));
            if (i == 3) withdrawal.Reject(admin.Id, "Kiểm tra lại", Utc(22));
            db.WithdrawalRequests.Add(withdrawal);
        }
        await db.SaveChangesAsync();
        var completedOrder = await db.Orders.FirstAsync(x => x.Status == OrderStatus.Completed && x.PaymentStatus == PaymentStatus.Paid);
        db.WalletTransactions.AddRange(new WalletTransaction { Id = Guid.NewGuid(), WalletId = wallet.Id, OrderId = completedOrder.Id, Amount = 1000, Type = "SellerPayout", CreatedAtUtc = Utc(22) },
            new WalletTransaction { Id = Guid.NewGuid(), WalletId = wallet.Id, OrderId = completedOrder.Id, Amount = 0, Type = "TestExtraLedger", CreatedAtUtc = Utc(22) });
        await db.SaveChangesAsync();
    }
}
