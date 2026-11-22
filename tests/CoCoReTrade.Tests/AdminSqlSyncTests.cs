using System.Net;
using System.Net.Http.Json;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static CoCoReTrade.Tests.AdminSqlAuditFactory;

namespace CoCoReTrade.Tests;

public sealed class AdminSqlSyncTests
{
    [Fact]
    public async Task AdminChangesAreDurableAndVisibleToBuyerAndSellerWithHistoricalLinksIntact()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new AdminSqlAuditFactory(); using var bootstrap = factory.CreateClient();
        try
        {
            await factory.InitializeUsersAsync();
            using var admin = await factory.LoginAsync("admin"); using var seller = await factory.LoginAsync("seller");
            using var buyer = await factory.LoginAsync("buyer");
            var listing = await Success<ProductDetails>(await seller.PostAsync("/api/v1/listings", Listing()));
            Assert.Equal(3, await factory.ReadAsync(db => db.ProductImages.CountAsync(x => x.ProductId == listing.Id)));
            Assert.Equal(ProductStatus.PendingApproval, await factory.ReadAsync(db => db.Products.Where(x => x.Id == listing.Id).Select(x => x.Status).SingleAsync()));
            // Owner edits are stored, not lost after a new request/DbContext.
            listing = (await seller.GetFromJsonAsync<ProductDetails>($"/api/v1/listings/{listing.Id}", Json))!;
            var edit = Listing(false); edit.Add(new StringContent(listing.RowVersion), "ExpectedRowVersion");
            foreach (var image in listing.ImageIds) edit.Add(new StringContent(image.ToString()), "RetainedImageIds");
            edit.Add(new StringContent("Mô tả được cập nhật và lưu SQL"), "Description");
            Assert.Equal(HttpStatusCode.Redirect, (await seller.PostAsync($"/Listing/Edit/{listing.Id}", edit)).StatusCode);
            Assert.Equal("Mô tả được cập nhật và lưu SQL", await factory.ReadAsync(db => db.Products.Where(x => x.Id == listing.Id).Select(x => x.Description).SingleAsync()));
            listing = (await seller.GetFromJsonAsync<ProductDetails>($"/api/v1/listings/{listing.Id}", Json))!;
            Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync($"/Admin/Inspection/Preliminary/{listing.Id}",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["decision"] = "Approved", ["ExpectedRowVersion"] = listing.RowVersion }))).StatusCode);
            Assert.Equal(PreliminaryReviewStatus.Approved, await factory.ReadAsync(db => db.Products.Where(x => x.Id == listing.Id).Select(x => x.PreliminaryStatus).SingleAsync()));
            var detail = (await admin.GetFromJsonAsync<AdminProductDetails>($"/api/v1/admin/inspections/{listing.Id}", Json))!;
            var cover = detail.Product.ImageIds.Last();
            detail = await Success<AdminProductDetails>(await admin.PostAsJsonAsync($"/api/v1/admin/inspections/{listing.Id}/cover", new { ImageId = cover, ExpectedRowVersion = detail.RowVersion }));
            Assert.Equal(cover, await factory.ReadAsync(db => db.ProductImages.Where(x => x.ProductId == listing.Id).OrderBy(x => x.SortOrder).Select(x => x.Id).FirstAsync()));
            var version = detail.RowVersion;
            var inspection = new MultipartFormDataContent();
            foreach (var pair in new Dictionary<string, string> { ["ProductId"] = listing.Id.ToString(), ["ExpectedRowVersion"] = version,
                ["Passed"] = "true", ["Name"] = "Áo sau kiểm định", ["Brand"] = "CoCo", ["Category"] = "Áo", ["Size"] = "M",
                ["OriginalPrice"] = "400000", ["AskingPrice"] = "200000", ["VerifiedCondition"] = "90", ["Notes"] = "Đã kiểm tra hiện vật" })
                inspection.Add(new StringContent(pair.Value), pair.Key);
            detail = await Success<AdminProductDetails>(await admin.PostAsync($"/api/v1/admin/inspections/{listing.Id}/inspect-and-publish", inspection));
            Assert.Equal(ProductStatus.Active, detail.Product.Status);
            var sql = await factory.ReadAsync(db => db.Products.AsNoTracking().SingleAsync(x => x.Id == listing.Id));
            Assert.Equal("Áo sau kiểm định", sql.Name); Assert.Equal(200000, sql.AskingPrice); Assert.Equal(90, sql.VerifiedCondition);
            Assert.Equal(ProductStatus.Active, sql.Status); Assert.StartsWith("CC-", sql.Sku);
            Assert.Equal(1, await factory.ReadAsync(db => db.Inspections.CountAsync(x => x.ProductId == listing.Id && x.Approved)));
            var publicItem = (await buyer.GetFromJsonAsync<ProductDetails>($"/api/v1/products/{listing.Id}", Json))!;
            Assert.Equal(sql.Name, publicItem.Name); Assert.Equal(sql.AskingPrice, publicItem.AskingPrice);
            var closet = (await seller.GetFromJsonAsync<List<ProductCard>>("/api/v1/listings/mine", Json))!;
            Assert.Equal(sql.Name, closet.Single(x => x.Id == listing.Id).Name);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/admin/inspections/{listing.Id}/publish",
                new { ExpectedRowVersion = version, PhysicalItemConfirmed = true })).StatusCode);
            var ownerHtml = await seller.GetStringAsync("/Listing/Closet");
            Assert.DoesNotContain("aria-label=\"Mua ngay", ownerHtml);
            Assert.DoesNotContain("Không thể tự mua", ownerHtml);
            Assert.Contains("product-sale-status Active", ownerHtml);

            var ownCheckout = new CheckoutRequest(listing.Id, null, Guid.NewGuid(), "Người kiểm thử", "0905551234",
                FulfillmentType.Shipping, null, "Đà Nẵng", "Hải Châu", "12 Đường kiểm thử");
            Assert.Equal(HttpStatusCode.Forbidden, (await seller.PostAsJsonAsync("/api/v1/orders", ownCheckout)).StatusCode);
            var order = await Success<OrderDto>(await buyer.PostAsJsonAsync("/api/v1/orders", ownCheckout with { IdempotencyKey = Guid.NewGuid() }));
            Assert.Equal(ProductStatus.Reserved, await factory.ReadAsync(db => db.Products.Where(x => x.Id == listing.Id).Select(x => x.Status).SingleAsync()));
            Assert.Matches(@"^ORD-\d{8}-\d{3,}$", order.OrderCode);
            await Success<OrderDto>(await admin.PostAsync($"/api/v1/admin/orders/{order.Id}/confirm", null));
            order = await Success<OrderDto>(await admin.PostAsJsonAsync($"/api/v1/admin/orders/{order.Id}/ready", new { TrackingCode = "SQL-SHIP-01" }));
            Assert.Equal(OrderStatus.Shipping, await factory.ReadAsync(db => db.Orders.Where(x => x.Id == order.Id).Select(x => x.Status).SingleAsync()));
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/v1/admin/orders/{order.Id}/complete", null)).StatusCode);
            Assert.Equal(500000, await factory.ReadAsync(db => db.Wallets.Where(x => x.UserId == "seller").Select(x => x.Balance).SingleAsync()));
            await Success<OrderDto>(await admin.PostAsJsonAsync($"/api/v1/admin/orders/{order.Id}/confirm-payment", new { Reference = "SQL-PAID-01" }));
            order = await Success<OrderDto>(await admin.PostAsync($"/api/v1/admin/orders/{order.Id}/complete", null));
            Assert.Equal(OrderStatus.Completed, order.Status);
            Assert.Equal(PaymentStatus.Paid, await factory.ReadAsync(db => db.Orders.Where(x => x.Id == order.Id).Select(x => x.PaymentStatus).SingleAsync()));
            Assert.Equal(ProductStatus.Sold, await factory.ReadAsync(db => db.Products.Where(x => x.Id == listing.Id).Select(x => x.Status).SingleAsync()));
            Assert.Equal(630000, await factory.ReadAsync(db => db.Wallets.Where(x => x.UserId == "seller").Select(x => x.Balance).SingleAsync()));
            Assert.Equal(70000, await factory.ReadAsync(db => db.PlatformCommissionEntries.Where(x => x.OrderId == order.Id).Select(x => x.Amount).SingleAsync()));
            Assert.Equal(1, await factory.ReadAsync(db => db.WalletTransactions.CountAsync(x => x.OrderId == order.Id && x.Type == "SellerPayout")));
            Assert.Contains("product-sale-status Sold", await seller.GetStringAsync("/Listing/Closet"));
            var buyerOrder = (await buyer.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", Json))!;
            Assert.Equal(OrderStatus.Completed, buyerOrder.Status); Assert.Equal(order.OrderCode, buyerOrder.OrderCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/admin/reports/revenue")).StatusCode);
            var completedHtml = await admin.GetStringAsync("/Admin/Products");
            var currentVersion = await factory.ReadAsync(db => db.Products.Where(x => x.Id == listing.Id).Select(x => x.RowVersion).SingleAsync());
            Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync($"/Admin/Products/Delete/{listing.Id}", Form(completedHtml,
                ("expectedRowVersion", Convert.ToBase64String(currentVersion))))).StatusCode);
            Assert.Equal(ProductStatus.Removed, await factory.ReadAsync(db => db.Products.Where(x => x.Id == listing.Id).Select(x => x.Status).SingleAsync()));
            Assert.Equal(1, await factory.ReadAsync(db => db.Orders.CountAsync(x => x.Id == order.Id)));
            Assert.Equal("Áo sau kiểm định", await factory.ReadAsync(db => db.Orders.Where(x => x.Id == order.Id).Select(x => x.ProductName).SingleAsync()));
            Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/api/v1/products/{listing.Id}")).StatusCode);
            Assert.DoesNotContain((await buyer.GetFromJsonAsync<List<ProductCard>>("/api/v1/products", Json))!, p => p.Id == listing.Id);

            // Identity writes, permissions and audit entries use the same SQL transaction.
            await Success<AdminUserRow>(await admin.PostAsJsonAsync("/api/v1/admin/users/member/inspector", new { Enabled = true }));
            Assert.True(await factory.ReadAsync(db => db.UserRoles.Join(db.Roles, a => a.RoleId, b => b.Id, (a, b) => new { a.UserId, b.Name }).AnyAsync(x => x.UserId == "member" && x.Name == "Inspector")));
            await Success<AdminUserRow>(await admin.PostAsJsonAsync("/api/v1/admin/users/member/publish-grant", new { Enabled = true }));
            Assert.True(await factory.ReadAsync(db => db.UserClaims.AnyAsync(x => x.UserId == "member" && x.ClaimValue == StaffPermissions.Publish)));
            await Success<AdminUserRow>(await admin.PostAsJsonAsync("/api/v1/admin/users/member/inspector", new { Enabled = false }));
            Assert.False(await factory.ReadAsync(db => db.UserClaims.AnyAsync(x => x.UserId == "member" && x.ClaimValue == StaffPermissions.Publish)));
            await Success<AdminUserRow>(await admin.PostAsJsonAsync("/api/v1/admin/users/member/lock", new { Enabled = true }));
            Assert.NotNull(await factory.ReadAsync(db => db.Users.Where(x => x.Id == "member").Select(x => x.LockoutEnd).SingleAsync()));
            await Success<AdminUserRow>(await admin.PostAsJsonAsync("/api/v1/admin/users/member/lock", new { Enabled = false }));
            Assert.Null(await factory.ReadAsync(db => db.Users.Where(x => x.Id == "member").Select(x => x.LockoutEnd).SingleAsync()));
            Assert.True(await factory.ReadAsync(db => db.AuditLogs.AnyAsync(x => x.Action == "Products.Published" && x.EntityId == listing.Id.ToString())));
            var support = await Success<SupportTicketDetails>(await buyer.PostAsJsonAsync("/api/v1/support", new { Subject = "Kiểm tra hỗ trợ SQL", Body = "Nội dung liên hệ cần lưu lại" }));
            await Success<SupportTicketDetails>(await admin.PostAsJsonAsync($"/api/v1/admin/support/{support.Ticket.Id}/replies", new { Body = "Phản hồi đã được ghi vào SQL" }));
            Assert.True(await factory.ReadAsync(db => db.SupportMessages.AnyAsync(x => x.TicketId == support.Ticket.Id && x.Body == "Phản hồi đã được ghi vào SQL")));
            await Success<SupportTicketDetails>(await admin.PostAsync($"/api/v1/admin/support/{support.Ticket.Id}/close", null));
            Assert.Equal(SupportTicketStatus.Closed, await factory.ReadAsync(db => db.SupportTickets.Where(x => x.Id == support.Ticket.Id).Select(x => x.Status).SingleAsync()));
            // SQL, not a UI/session cache, is the voucher source read by checkout.
            await factory.ReadAsync(async db => { db.CheckoutVouchers.Add(new CheckoutVoucher("SQLONLY", 7, 1000, DateTime.UtcNow.AddDays(1))); return await db.SaveChangesAsync(); });
            Assert.True(await factory.ReadAsync(async db => (await new BasketRepository(db).ActiveVouchersAsync(DateTime.UtcNow, default)).Any(x => x.Code == "SQLONLY" && x.Percent == 7)));
        }
        finally { await factory.DeleteDatabaseAsync(); }
    }
    private static MultipartFormDataContent Listing(bool images = true)
    {
        var content = new MultipartFormDataContent();
        foreach (var pair in new Dictionary<string, string> { ["Name"] = "Áo kiểm thử SQL", ["Brand"] = "CoCo", ["Category"] = "Áo", ["Size"] = "M",
            ["OriginalPrice"] = "400000", ["AskingPrice"] = "250000", ["ConditionPercent"] = "95", ["AllowOffers"] = "true", ["AcknowledgedDeliveryInstructions"] = "true" })
            content.Add(new StringContent(pair.Value), pair.Key);
        if (images)
        {
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9kAAAAASUVORK5CYII=");
            for (var i = 0; i < 3; i++)
            {
                var bytes = new byte[150]; png.CopyTo(bytes, 0); var file = new ByteArrayContent(bytes);
                file.Headers.ContentType = new("image/png"); content.Add(file, "Images", $"fixture-{i}.png");
            }
        }
        return content;
    }
}
