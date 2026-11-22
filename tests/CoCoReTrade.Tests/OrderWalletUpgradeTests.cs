using System.Data;
using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using CoCoReTrade.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoCoReTrade.Tests;

public sealed class OrderWalletUpgradeTests
{
    private static int skuSequence;
    [Theory]
    [InlineData("2026-11-21T16:59:59Z", 1, "ORD-20261121-001")]
    [InlineData("2026-11-21T17:00:00Z", 1, "ORD-20261122-001")]
    [InlineData("2026-11-22T16:59:59Z", 999, "ORD-20261122-999")]
    [InlineData("2026-11-22T17:00:00Z", 1, "ORD-20261123-001")]
    [InlineData("2026-11-21T17:00:00Z", 1000, "ORD-20261122-1000")]
    public void CodeUsesVietnamDayAndDoesNotTruncateLargeSequences(string utc, long number, string expected) =>
        Assert.Equal(expected, OrderCodeRules.Format(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal), number));

    [Theory]
    [InlineData("Admin", "Admin")]
    [InlineData("User", "")]
    public async Task OrderNotificationsHaveRoleSpecificDestinationsEvenWhenOfferAlsoExists(string role, string area)
    {
        var id = Guid.NewGuid();
        var item = new NotificationDto(Guid.NewGuid(), "Orders", "Đơn mới", Guid.NewGuid(), id, false, DateTime.UtcNow);
        var controller = new ProfileController(new OneNotification(item), null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "viewer"), new(ClaimTypes.Role, role)], "test"))
            } }
        };
        var result = Assert.IsType<RedirectToActionResult>(await controller.OpenNotification(item.Id, default));
        Assert.Equal("Orders", result.ControllerName); Assert.Equal("Details", result.ActionName);
        Assert.Equal(area, result.RouteValues!["area"]); Assert.Equal(id, result.RouteValues["id"]);
    }

    [Fact]
    public void DiscountsUsePlatformShareAndAllocationNeverExceedsLineCommission()
    {
        var now = new DateTime(2026, 11, 22, 3, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => new CheckoutVoucher("TOOMUCH", 36, 0, now.AddDays(1)));
        Assert.Equal(20000, new CheckoutVoucher("GIAM10", 10, 0, now.AddDays(1)).Discount(200000, now));
        foreach (var prices in new decimal[][] { [200000, 300000], [1001, 1001, 1001], [1003, 1003, 1003, 1003] })
        {
            var caps = prices.Select(p => CommissionRules.Calculate(p, 35)).ToArray();
            foreach (var percent in new[] { 10, 25, 35 })
            {
                var discount = Math.Min(decimal.Floor(prices.Sum() * percent / 100m), caps.Sum());
                var split = CheckoutAllocation.WithinCommission(discount, prices, caps);
                Assert.Equal(discount, split.Sum());
                for (var i = 0; i < split.Length; i++) Assert.InRange(split[i], 0, caps[i]);
            }
        }
    }

    [Fact]
    public async Task SqlMigrationSequencesWalletOwnershipAndDiscountAccounting()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        var name = "CoCoOrderUpgradeTests_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            $"Server=.\\SQLEXPRESS;Database={name};Trusted_Connection=True;TrustServerCertificate=True").Options;
        await using var db = new AppDbContext(options);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261122073033_NewsletterSubscriptions");
            foreach (var id in new[] { "buyer", "seller", "other", "admin" })
                db.Users.Add(new ApplicationUser { Id = id, UserName = id + "@example.test", Email = id + "@example.test", FullName = "Kiểm thử " + id });
            var dates = new[] { Utc(21,16,59,59), Utc(21,17,0,0), Utc(21,17,1,0) };
            var legacy = new List<Order>();
            for (var i = 0; i < dates.Length; i++)
            {
                var product = Active("legacy-" + i, 200000 + i * 50000, dates[i]);
                db.Products.Add(product);
                legacy.Add(NewOrder(product, dates[i]));
            }
            await db.SaveChangesAsync();
            foreach (var order in legacy) await InsertLegacyAsync(db, order);
            var snapshots = legacy.ToDictionary(o => o.Id, o => (o.ProductId, o.BuyerId, o.SellerId, o.ItemAmount, o.CommissionAmount, o.TotalAmount));
            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();
            var migrated = await db.Orders.OrderBy(o => o.CreatedAtUtc).ToArrayAsync();
            Assert.Equal(new[] { "ORD-20261121-001", "ORD-20261122-001", "ORD-20261122-002" }, migrated.Select(o => o.OrderCode));
            foreach (var order in migrated)
                Assert.Equal(snapshots[order.Id], (order.ProductId, order.BuyerId, order.SellerId, order.ItemAmount, order.CommissionAmount, order.TotalAmount));

            var products = Enumerable.Range(0, 10).Select(i => Active("concurrent-" + i, 200000, Utc(22,3))).ToArray();
            db.Products.AddRange(products); await db.SaveChangesAsync();
            var codes = await Task.WhenAll(products.Take(8).Select(async product =>
            {
                await using var worker = new AppDbContext(options);
                var found = (await worker.Products.FindAsync(product.Id))!;
                var order = NewOrder(found, Utc(22,3));
                found.Reserve(); worker.Orders.Add(order); await worker.SaveChangesAsync();
                return order.OrderCode;
            }));
            Assert.Equal(8, codes.Distinct().Count());
            Assert.Equal(Enumerable.Range(3,8).Select(i => $"ORD-20261122-{i:D3}").Order(), codes.Order());

            // A failed unique request must roll the allocated daily number back as well.
            await using (var duplicate = new AppDbContext(options))
            {
                var p = (await duplicate.Products.FindAsync(products[8].Id))!;
                var order = NewOrder(p, Utc(22,3), migrated[1].IdempotencyKey);
                duplicate.Orders.Add(order);
                await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            }
            db.ChangeTracker.Clear();
            Assert.Equal(10, (await db.OrderCodeCounters.SingleAsync(c => c.Day == new DateOnly(2026,11,22))).LastNumber);
            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.OrderCodeCounters SET LastNumber=999 WHERE [Day]='20261122'");
            var thousand = NewOrder(products[8], Utc(22,4)); db.Orders.Add(thousand); await db.SaveChangesAsync();
            Assert.Equal("ORD-20261122-1000", thousand.OrderCode);
            var nextDay = NewOrder(products[9], Utc(22,17)); db.Orders.Add(nextDay); await db.SaveChangesAsync();
            Assert.Equal("ORD-20261123-001", nextDay.OrderCode);

            var wallet = new Wallet { Id=Guid.NewGuid(), UserId="seller", Balance=292500 };
            var otherWallet = new Wallet { Id=Guid.NewGuid(), UserId="other" };
            db.Wallets.AddRange(wallet, otherWallet);
            db.WalletTransactions.AddRange(
                Credit(wallet.Id, migrated[0].Id, 130000, Utc(22,5)),
                Credit(wallet.Id, migrated[1].Id, 162500, Utc(22,6)),
                Credit(otherWallet.Id, migrated[2].Id, 195000, Utc(22,7)),
                new WalletTransaction { Id=Guid.NewGuid(), WalletId=wallet.Id, Amount=10000, Type="WithdrawalReleased", CreatedAtUtc=Utc(22,8) });
            await db.SaveChangesAsync();
            var repository = new WalletWithdrawalRepository(db);
            var history = await repository.CreditsAsync("seller", default);
            Assert.Equal(new decimal[] {162500,130000}, history.Select(h=>h.Amount));
            Assert.All(history,h=>Assert.NotEqual(Guid.Empty,h.ProductId));
            Assert.Empty(await repository.CreditsAsync("other", default));
            Assert.Empty(await repository.CreditsAsync("stranger", default));

            // 200,000 - 20,000 = 180,000: seller 130,000; CoCo 50,000.
            var sale = Active("discount-accounting", 200000, Utc(22,9), "other"); db.Products.Add(sale);
            var batch = new CheckoutBatch("buyer", Guid.NewGuid(), new string('a',64), Utc(22,9)); db.CheckoutBatches.Add(batch);
            var discounted = NewOrder(sale, Utc(22,9));
            discounted.AttachCheckout(batch.Id,0,20000,"GIAM10");
            sale.Reserve(); discounted.MarkReady(); discounted.ConfirmPayment("admin","QA-TRANSFER",Utc(22,9));
            discounted.Complete(sale,Utc(22,10)); db.Orders.Add(discounted); await db.SaveChangesAsync();
            var payout = new SellerPayoutService(new AdminWalletRepository(db),new PlatformCommissionRepository(db));
            await payout.CreditCompletedOrderAsync(discounted,"admin",Utc(22,10),default); await db.SaveChangesAsync();
            Assert.Equal(180000, discounted.TotalAmount);
            Assert.Equal(130000, await db.WalletTransactions.Where(t=>t.OrderId==discounted.Id).Select(t=>t.Amount).SingleAsync());
            Assert.Equal(50000, await db.PlatformCommissionEntries.Where(e=>e.OrderId==discounted.Id).Select(e=>e.Amount).SingleAsync());
            var totals = await new AdminFinancialReportRepository(db).SalesTotalsAsync(Utc(22,0),Utc(23,0),default);
            Assert.Equal(50000,totals.CommissionAmount); Assert.Equal(130000,totals.SellerNetAmount); Assert.Equal(130000,totals.SellerWalletCreditedAmount);
            var notification = new MarketplaceNotification { Id=Guid.NewGuid(), UserId="admin", Category="Orders",OrderId=discounted.Id,Message="Có đơn mới",CreatedAtUtc=Utc(22,10) };
            db.MarketplaceNotifications.Add(notification); await db.SaveChangesAsync();
            Assert.StartsWith(discounted.OrderCode+" · ",notification.Message);
        }
        finally
        {
            db.ChangeTracker.Clear();
            Assert.StartsWith("CoCoOrderUpgradeTests_",db.Database.GetDbConnection().Database);
            await db.Database.EnsureDeletedAsync();
        }
    }

    internal static Product Active(string code, decimal amount, DateTime now, string seller="seller", string? photo=null)
    {
        var p = Product.Submit(seller,"CD-"+code,"Sản phẩm kiểm thử "+code,"CoCo","Áo","M",amount*2,amount,95,true,
            [new ProductImage(photo??"qa-a.webp","image/webp",0),new ProductImage(photo??"qa-b.webp","image/webp",1),new ProductImage(photo??"qa-c.webp","image/webp",2)]);
        p.ReviewPreliminarily("admin",PreliminaryReviewStatus.Approved,null,now);
        p.RecordInspection("admin",true,p.Name,p.Brand,p.Category,p.Size,p.OriginalPrice,p.AskingPrice,95,"Đã kiểm tra",[],now);
        p.Publish("CC-2026-"+Interlocked.Increment(ref skuSequence).ToString("D6"),now); return p;
    }
    internal static Order NewOrder(Product p, DateTime now, Guid? key=null) => Order.Create(p,null,"buyer",key??Guid.NewGuid(),
        FulfillmentType.Pickup,"Người nhận kiểm thử","0905551234",null,null,null,null,"536/538 Núi Thành",0,35,now);
    internal static DateTime Utc(int day,int hour,int minute=0,int second=0) => new(2026,11,day,hour,minute,second,DateTimeKind.Utc);
    private static WalletTransaction Credit(Guid wallet,Guid order,decimal amount,DateTime now) =>
        new() {Id=Guid.NewGuid(),WalletId=wallet,OrderId=order,Amount=amount,Type="SellerPayout",CreatedAtUtc=now};
    private static async Task InsertLegacyAsync(AppDbContext db,Order order)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command=db.Database.GetDbConnection().CreateCommand();
            var properties=db.Entry(order).Properties.Where(p=>p.Metadata.Name is not ("OrderCode" or "RowVersion")).ToArray();
            var columns=new List<string>();var values=new List<string>();
            for(var i=0;i<properties.Length;i++)
            {
                var property=properties[i];columns.Add("["+property.Metadata.GetColumnName(StoreObjectIdentifier.Table("Orders",null))+"]");
                var parameter=command.CreateParameter();parameter.ParameterName="@p"+i;
                var converter=property.Metadata.GetTypeMapping().Converter;
                parameter.Value=(converter is null?property.CurrentValue:converter.ConvertToProvider(property.CurrentValue))??DBNull.Value;
                command.Parameters.Add(parameter);values.Add(parameter.ParameterName);
            }
            command.CommandText="INSERT dbo.Orders ("+string.Join(",",columns)+") VALUES ("+string.Join(",",values)+")";
            await command.ExecuteNonQueryAsync();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
    private sealed class OneNotification(NotificationDto item) : INotificationCenter
    {
        public Task<NotificationDto> OpenAsync(string userId,Guid id,CancellationToken ct)=>Task.FromResult(item);
        public Task<IReadOnlyList<NotificationDto>> ListAsync(string userId,string? group,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> HasUnreadAsync(string userId,CancellationToken ct)=>throw new NotSupportedException();
        public Task MarkAllReadAsync(string userId,CancellationToken ct)=>throw new NotSupportedException();
        public Task MarkReadAsync(string userId,Guid id,CancellationToken ct)=>throw new NotSupportedException();
    }
}

