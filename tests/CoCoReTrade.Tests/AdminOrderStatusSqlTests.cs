using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoCoReTrade.Tests;

public sealed class AdminOrderStatusSqlTests
{
    [Fact]
    public async Task OrderGroupsMatchSqlStatusesWithoutOverlapOrLosingOlderOrders()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new AdminSqlAuditFactory(new DashboardSqlTests.DashboardClock());
        using var bootstrap = factory.CreateClient();
        try
        {
            await factory.InitializeUsersAsync(); await DashboardSqlTests.Seed(factory);
            await SeedStatusCases(factory, true);
            using var admin = await factory.LoginAsync("admin");
            var snapshot = await Snapshot(factory);
            var rows = await factory.ReadAsync(db => db.Orders.AsNoTracking().ToListAsync());
            using var scope = factory.Services.CreateScope();
            var query = scope.ServiceProvider.GetRequiredService<IDashboardDrilldown>();
            var actorId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("admin@cocoretrade.vn"))!.Id;
            var groups = new[] { ("active", OrderStatus.PendingProcessing), ("shipping", OrderStatus.Shipping),
                ("ready", OrderStatus.ReadyForPickup), ("completed", OrderStatus.Completed), ("cancelled", OrderStatus.Cancelled) };
            foreach (var (group, status) in groups)
            {
                var expected = rows.Where(x => x.Status == status).Select(x => x.Id).ToHashSet();
                var actual = await query.OrdersAsync(actorId, null, null, status, null, null, default);
                Assert.True(expected.SetEquals(actual.Select(x => x.Id)));
                var html = await admin.GetStringAsync("/Admin/Orders?group=" + group);
                Assert.Contains($">{expected.Count} kết quả</span>", WebUtility.HtmlDecode(html));
                var visible = Regex.Matches(html, "href=\"/Admin/Orders/Details/([a-f0-9-]{36})\"").Select(x => Guid.Parse(x.Groups[1].Value)).ToList();
                Assert.Equal(Math.Min(10, expected.Count), visible.Count);
                Assert.All(visible, id => Assert.Contains(id, expected));
                Assert.Contains("admin-list-badge--" + (group == "active" ? "pending" : group == "completed" ? "pickup" : group), html);
            }
            Assert.True(rows.Count > 300);
            var oldestPending = rows.Where(x => x.Status == OrderStatus.PendingProcessing).MinBy(x => x.CreatedAtUtc)!;
            Assert.True(rows.Count(x => x.CreatedAtUtc > oldestPending.CreatedAtUtc) > 300);
            Assert.Contains(oldestPending.Id.ToString(), await admin.GetStringAsync("/Admin/Orders?group=active"));
            var dated = await admin.GetStringAsync("/Admin/Orders?group=active&from=2026-11-22&through=2026-11-22");
            Assert.DoesNotContain(oldestPending.Id.ToString(), dated);
            Assert.Contains("data-dashboard-selection", dated);
            var searched = await admin.GetStringAsync("/Admin/Orders?group=active&search=" + oldestPending.OrderCode);
            Assert.Contains(oldestPending.Id.ToString(), searched);
            Assert.Single(Regex.Matches(searched, "href=\"/Admin/Orders/Details/"));
            var conflict = await admin.GetStringAsync("/Admin/Orders?group=shipping&status=PendingProcessing");
            Assert.DoesNotContain("href=\"/Admin/Orders/Details/", conflict);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/Admin/Orders?from=2026-11-22")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/Admin/Orders?status=999")).StatusCode);
            Assert.Equal(snapshot, await Snapshot(factory));
        }
        finally { await factory.DeleteDatabaseAsync(); }
    }

    internal static async Task SeedStatusCases(AdminSqlAuditFactory factory, bool includeQueueLimitCase = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("admin@cocoretrade.vn"))!;
        var now = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
        Product Item(int n)
        {
            var item = Product.Submit("seller", "CG-2026-" + n, "Sản phẩm nhận tại Shop " + n, "CoCo", "Áo", "M", 400000, 200000, 95, true,
                [new ProductImage("a.jpg", "image/jpeg", 0), new ProductImage("b.jpg", "image/jpeg", 1), new ProductImage("c.jpg", "image/jpeg", 2)]);
            item.ReviewPreliminarily(admin.Id, PreliminaryReviewStatus.Approved, null, now);
            item.RecordInspection(admin.Id, true, item.Name, item.Brand, item.Category, item.Size, item.OriginalPrice, item.AskingPrice, 95, "Đã kiểm tra", [], now);
            item.Publish("CC-2026-" + n, now); db.Products.Add(item); return item;
        }
        Order Pickup(Product item)
        {
            var order = Order.Create(item, null, "buyer", Guid.NewGuid(), FulfillmentType.Pickup,
                "Người kiểm thử", "0905551234", null, null, null, null, "536/538 Núi Thành", 0, 35, now);
            item.Reserve(); order.MarkReady(); db.Orders.Add(order); return order;
        }
        Pickup(Item(998001));
        var completedItem = Item(998002); var completed = Pickup(completedItem);
        completed.ConfirmPayment(admin.Id, "TEST-PICKUP", now); completed.Complete(completedItem, now);
        if (includeQueueLimitCase)
        {
            var item = Item(998003);
            for (var i = 0; i < 301; i++)
            {
                var order = Order.Create(item, null, "buyer", Guid.NewGuid(), FulfillmentType.Shipping,
                    "Người kiểm thử", "0905551234", "Đà Nẵng", "Hải Châu", "12 Đường kiểm thử", null, null, 20000, 35, now);
                item.Reserve(); order.Cancel(item, true, now); db.Orders.Add(order);
            }
        }
        await db.SaveChangesAsync();
    }

    internal static Task<string> Snapshot(AdminSqlAuditFactory factory) => factory.ReadAsync(async db =>
        JsonSerializer.Serialize(await db.Orders.AsNoTracking().OrderBy(x => x.Id).Select(x => new {
            x.Id, x.Status, x.PaymentStatus, x.ItemAmount, x.TotalAmount, x.CommissionAmount, x.DiscountAmount,
            x.OrderCode, x.CreatedAtUtc, x.CompletedAtUtc, x.CancelledAtUtc }).ToListAsync()));

    internal static async Task WriteBrowserExpectations(AdminSqlAuditFactory factory, string root)
    {
        var rows = await factory.ReadAsync(db => db.Orders.AsNoTracking().Select(x => new { x.Id, x.Status, x.Fulfillment, x.OrderCode }).ToListAsync());
        await File.WriteAllTextAsync(Path.Combine(root, "order-expectations.json"), JsonSerializer.Serialize(rows, AdminSqlAuditFactory.Json));
    }
}
