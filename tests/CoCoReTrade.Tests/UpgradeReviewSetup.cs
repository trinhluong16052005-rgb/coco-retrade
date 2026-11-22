using System.Text.Json;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Tests;

// Opt-in local UI fixtures. Never seed the configured live database.
public sealed class UpgradeReviewSetup
{
    [Fact]
    public async Task ReadOnlyReviewProjectionsHaveOrderCodesAndCorrectNetCommission()
    {
        var path=Environment.GetEnvironmentVariable("COCO_UPGRADE_REVIEW_CONFIG");
        if(string.IsNullOrWhiteSpace(path)) return;
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var connection=config.RootElement.GetProperty("ConnectionStrings").GetProperty("CoCo").GetString()!;
        Assert.StartsWith("CoCoOrdersWalletTests_",new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog);
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        var wallet=await db.Wallets.AsNoTracking().SingleAsync(w=>w.UserId=="seller");
        var details=await new AdminWalletRepository(db).DetailsAsync(wallet.Id,default);
        Assert.NotNull(details);
        Assert.All(details.Transactions.Where(t=>t.OrderId.HasValue),t=>Assert.StartsWith("ORD-",t.OrderCode));
        var platform=await new PlatformCommissionRepository(db).SnapshotAsync("admin",default);
        Assert.NotEmpty(platform.RecentEntries);
        Assert.All(platform.RecentEntries,e=>Assert.StartsWith("ORD-",e.OrderCode));
        var complete=await db.Orders.AsNoTracking().Where(o=>o.Status==OrderStatus.Completed&&o.PaymentStatus==PaymentStatus.Paid).ToListAsync();
        Assert.Equal(complete.Sum(o=>o.CommissionAmount-o.DiscountAmount),platform.TotalCommission);
        var history=await new WalletWithdrawalRepository(db).CreditsAsync("seller",default);
        Assert.Equal(complete.Sum(o=>o.ItemAmount-o.CommissionAmount),history.Sum(h=>h.Amount));
    }

    [Fact]
    public async Task SeedIsolatedReview()
    {
        var path=Environment.GetEnvironmentVariable("COCO_UPGRADE_REVIEW_CONFIG");
        if(string.IsNullOrWhiteSpace(path)) return;
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root=config.RootElement;
        var connection=root.GetProperty("ConnectionStrings").GetProperty("CoCo").GetString()!;
        var database=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog;
        Assert.StartsWith("CoCoOrdersWalletTests_",database);
        var password=root.GetProperty("Review").GetProperty("Password").GetString()!;
        var photo=root.GetProperty("Review").GetProperty("Photo").GetString()!;
        var photoType=root.GetProperty("Review").GetProperty("PhotoType").GetString()!;
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        if(await db.Database.CanConnectAsync())
        {
            if(File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,"review-ids.json"))) return;
            Assert.False(await db.Users.AnyAsync(),"Do not replace existing review accounts.");
        }
        await db.Database.MigrateAsync();
        foreach(var role in new[]{"User","Inspector","Admin"})
            db.Roles.Add(new IdentityRole(role){Id=role,NormalizedName=role.ToUpperInvariant()});
        foreach(var id in new[]{"admin","seller","buyer"})
        {
            var email=id=="admin"?"admin@cocoretrade.vn":id+"@example.test";
            var user=new ApplicationUser{Id=id,UserName=email,NormalizedUserName=email.ToUpperInvariant(),Email=email,
                NormalizedEmail=email.ToUpperInvariant(),EmailConfirmed=true,PhoneNumber=id=="admin"?"0905551230":id=="seller"?"0905551231":"0905551232",PhoneNumberConfirmed=true,
                FullName="Kiểm thử "+id,SecurityStamp=Guid.NewGuid().ToString(),ConcurrencyStamp=Guid.NewGuid().ToString()};
            user.PasswordHash=new PasswordHasher<ApplicationUser>().HashPassword(user,password);db.Users.Add(user);
            db.UserRoles.Add(new IdentityUserRole<string>{UserId=id,RoleId="User"});
            if(id=="admin")foreach(var role in new[]{"Admin","Inspector"})db.UserRoles.Add(new IdentityUserRole<string>{UserId=id,RoleId=role});
        }
        var now=DateTime.UtcNow;
        Product Product(int index,string category="Áo")
        {
            var amount=index==2?250000:200000;
            var name=index%2==0?"Áo kiểm thử có tên dài để xem bố cục đầy đủ":"Món đồ kiểm thử "+index;
            var p=CoCoReTrade.Domain.Product.Submit("seller","CD-"+index.ToString("D6")+"-2211",name,"CoCo",category,category=="Phụ kiện"?"20 x 15 x 8 cm":"M",
                amount*2,amount,95,true,[new ProductImage(photo,photoType,0),new ProductImage(photo,photoType,1),new ProductImage(photo,photoType,2)]);
            p.ReviewPreliminarily("admin",PreliminaryReviewStatus.Approved,null,now);
            p.RecordInspection("admin",true,p.Name,p.Brand,p.Category,p.Size,p.OriginalPrice,p.AskingPrice,95,"Đã kiểm tra",[],now);
            p.Publish("CC-2026-"+index.ToString("D6"),now);return p;
        }
        var items=Enumerable.Range(1,15).Select(i=>Product(i,i>4&&i%2==0?"Phụ kiện":"Áo")).ToArray();db.Products.AddRange(items);
        db.CheckoutVouchers.Add(new CheckoutVoucher("QA10",10,0,now.AddDays(7)));
        await db.SaveChangesAsync();
        var batch=new CheckoutBatch("buyer",Guid.NewGuid(),new string('b',64),now);db.CheckoutBatches.Add(batch);
        var pending=Order.Create(items[0],null,"buyer",Guid.NewGuid(),FulfillmentType.Shipping,"Người nhận kiểm thử","0905551234",
            "Đà Nẵng","Hải Châu","12 Đường kiểm thử",null,null,20000,35,now);
        pending.AttachCheckout(batch.Id,20000,20000,"QA10");items[0].Reserve();db.Orders.Add(pending);
        var notification=new MarketplaceNotification{Id=Guid.NewGuid(),UserId="admin",Category="Orders",OrderId=pending.Id,
            ProductId=pending.ProductId,Message="Có đơn hàng mới cần xử lý",CreatedAtUtc=now};
        db.MarketplaceNotifications.Add(notification);await db.SaveChangesAsync();
        var completed=new List<Guid>();
        foreach(var index in new[]{1,2})
        {
            var order=OrderWalletUpgradeTests.NewOrder(items[index],now.AddSeconds(index));
            if(index==2){var b=new CheckoutBatch("buyer",Guid.NewGuid(),new string('c',64),now);db.CheckoutBatches.Add(b);order.AttachCheckout(b.Id,0,20000,"QA10");}
            items[index].Reserve();order.MarkReady();order.ConfirmPayment("admin","QA-PAID-"+index,now);order.Complete(items[index],now);
            db.Orders.Add(order);await db.SaveChangesAsync();
            await new SellerPayoutService(new AdminWalletRepository(db),new PlatformCommissionRepository(db))
                .CreditCompletedOrderAsync(order,"admin",now.AddMinutes(index),default);
            await db.SaveChangesAsync();completed.Add(order.Id);
        }
        db.CartItems.Add(new CartItem{UserId="buyer",ProductId=items[3].Id});await db.SaveChangesAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,"review-ids.json"),
            JsonSerializer.Serialize(new{order=pending.Id,code=pending.OrderCode,notification=notification.Id,product=items[3].Id,completed}));
    }
}

