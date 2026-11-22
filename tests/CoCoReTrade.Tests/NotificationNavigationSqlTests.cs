using System.Net;
using System.Text.Json;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoCoReTrade.Tests;

public sealed class NotificationNavigationSqlTests
{
    internal sealed record NavigationCase(string Actor, Guid NoticeId, string Path, string? Fragment = null,
        string? Search = null, Guid? WalletId = null, Guid? RequestId = null);

    [Fact]
    public async Task NotificationsFollowOwnedWorkflowCurrentStateAndPermissionsWithoutChangingBusinessData()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new AdminSqlAuditFactory(new DashboardSqlTests.DashboardClock()); using var bootstrap = factory.CreateClient();
        try
        {
            await factory.InitializeUsersAsync(); await DashboardSqlTests.Seed(factory);
            var cases = await Seed(factory);
            var before = await BusinessSnapshot(factory);
            var messages = await factory.ReadAsync(db => db.MarketplaceNotifications.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Message).ToListAsync());
            foreach (var actor in cases.Select(x => x.Actor).Distinct())
            {
                using var client = await factory.LoginAsync(actor);
                foreach (var item in cases.Where(x => x.Actor == actor))
                {
                    var response = await client.GetAsync("/Profile/OpenNotification/" + item.NoticeId);
                    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                    var target = new Uri(client.BaseAddress!, response.Headers.Location!);
                    Assert.Equal(item.Path, target.AbsolutePath);
                    Assert.Equal(item.Fragment is null ? "" : "#" + item.Fragment, target.Fragment);
                    var query = QueryHelpers.ParseQuery(target.Query);
                    if (item.Search is not null) Assert.Equal(item.Search, query["search"].ToString());
                    if (item.WalletId.HasValue) Assert.Equal(item.WalletId.ToString(), query["walletId"].ToString());
                    if (item.RequestId.HasValue) Assert.Equal(item.RequestId.ToString(), query["requestId"].ToString());
                    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(target.PathAndQuery)).StatusCode);
                    Assert.True(await factory.ReadAsync(db => db.MarketplaceNotifications.Where(x => x.Id == item.NoticeId).Select(x => x.IsRead).SingleAsync()));
                }
            }
            using var buyer = await factory.LoginAsync("buyer");
            var sellerNotice = cases.First(x => x.Actor == "seller");
            Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync("/Profile/OpenNotification/" + sellerNotice.NoticeId)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync("/Profile/OpenNotification/" + Guid.NewGuid())).StatusCode);
            Assert.Equal(HttpStatusCode.MovedPermanently, (await buyer.GetAsync("/Profile/Notiifications")).StatusCode);
            Assert.Equal(before, await BusinessSnapshot(factory));
            Assert.Equal(messages, await factory.ReadAsync(db => db.MarketplaceNotifications.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Message).ToListAsync()));
            var support = await factory.ReadAsync(db => db.MarketplaceNotifications.Where(x => x.Category == "Support" && x.SupportTicketId != null).CountAsync());
            var withdrawals = await factory.ReadAsync(db => db.MarketplaceNotifications.Where(x => x.Category == "Wallets" && x.WithdrawalRequestId != null).CountAsync());
            Assert.True(support > 0 && withdrawals > 0);
        }
        finally { await factory.DeleteDatabaseAsync(); }
    }

    internal static async Task<List<NavigationCase>> Seed(AdminSqlAuditFactory factory)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.Users.Where(x => x.Email == "admin@cocoretrade.vn").Select(x => x.Id).SingleAsync();
        var now = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc); var result = new List<NavigationCase>();
        string Owner(string actor) => actor == "admin" ? adminId : actor;
        void Add(string actor, string category, string path, Guid? productId = null, Guid? orderId = null,
            Guid? offerId = null, Guid? conversationId = null, Guid? ticketId = null, Guid? withdrawalId = null,
            string? fragment = null, string? search = null, Guid? walletId = null, string? message = null, DateTime? created = null)
        {
            var notice = new MarketplaceNotification { Id = Guid.NewGuid(), UserId = Owner(actor), Category = category,
                Message = message ?? "Thông báo kiểm thử điều hướng", ProductId = productId, OrderId = orderId,
                OfferId = offerId, ConversationId = conversationId, SupportTicketId = ticketId,
                WithdrawalRequestId = withdrawalId, CreatedAtUtc = created ?? now };
            db.MarketplaceNotifications.Add(notice);
            result.Add(new(actor, notice.Id, path, fragment, search, walletId, walletId.HasValue ? withdrawalId : null));
        }
        var order = await db.Orders.FirstAsync();
        Add("admin", "Orders", "/Admin/Orders/Details/" + order.Id, orderId: order.Id);
        Add("buyer", "orders", "/Orders/Details/" + order.Id, orderId: order.Id);
        Add("seller", "orders", "/Orders/Details/" + order.Id, orderId: order.Id);
        Add("member", "orders", "/Orders", orderId: order.Id);
        var pending = await db.Products.FirstAsync(x => x.Status == ProductStatus.PendingApproval);
        var approved = await db.Products.FirstAsync(x => x.Status == ProductStatus.PendingApproval && x.Id != pending.Id);
        pending.ReviewPreliminarily(adminId, PreliminaryReviewStatus.ChangesRequested, "Bổ sung ảnh rõ nét", now);
        approved.ReviewPreliminarily(adminId, PreliminaryReviewStatus.Approved, null, now);
        var active = await db.Products.FirstAsync(x => x.Status == ProductStatus.Active);
        Add("seller", "Listings", "/Listing/Edit/" + pending.Id, productId: pending.Id);
        Add("seller", "Listings", "/Listing/Closet", productId: approved.Id, fragment: "consignment-" + approved.Id);
        Add("seller", "Listings", "/Listing/Closet", productId: active.Id, fragment: "consignment-" + active.Id);
        Add("admin", "Listings", "/Admin/Inspection/Review/" + pending.Id, productId: pending.Id);
        Add("member", "Listings", "/Admin/Inspection/Review/" + approved.Id, productId: approved.Id);
        Add("admin", "Listings", "/Admin/Inspection/Details/" + active.Id, productId: active.Id);
        Add("member", "Listings", "/Shop/Details/" + active.Id, productId: active.Id);
        Add("buyer", "Listings", "/Listing/Closet", productId: pending.Id);
        Add("seller", "Social", "/Shop/Details/" + active.Id, productId: active.Id);
        Add("seller", "Social", "/Listing/Closet");
        Add("seller", "Listings", "/Listing/Closet");
        Add("member", "Listings", "/Admin"); // Area route defaults to Inspection/Index.
        Add("buyer", "Cart", "/Cart", productId: active.Id);
        Add("buyer", "Carts", "/Cart");
        Add("buyer", "General", "/Profile/Notifications");
        var support = scope.ServiceProvider.GetRequiredService<ISupportService>();
        var ticket = await support.CreateAsync("seller", "Kiểm thử hỗ trợ", "Nội dung hỗ trợ đủ dài để kiểm thử.", default);
        await support.ReplyAsync(ticket.Ticket.Id, adminId, "CoCo phản hồi yêu cầu kiểm thử.", default);
        Add("admin", "Support", "/Admin/HelpDesk/Details/" + ticket.Ticket.Id, ticketId: ticket.Ticket.Id);
        Add("seller", "Support", "/Support/Details/" + ticket.Ticket.Id, ticketId: ticket.Ticket.Id);
        Add("buyer", "Support", "/Support", ticketId: ticket.Ticket.Id);
        var legacy = SupportTicket.Create("seller", "Yêu cầu cũ", "Nội dung hỗ trợ cũ để đối chiếu thời gian.", now.AddDays(-1));
        db.SupportTickets.Add(legacy);
        Add("seller", "Support", "/Support/Details/" + legacy.Id, created: now.AddDays(-1));
        Add("buyer", "Support", "/Support");
        var walletService = scope.ServiceProvider.GetRequiredService<IWalletService>();
        var request = await walletService.RequestWithdrawalAsync("seller", Guid.NewGuid(), 1000, "0123456789", "Vietcombank", default, "NGUOI KIEM THU");
        var wallet = await db.Wallets.SingleAsync(x => x.UserId == "seller");
        Add("admin", "Wallets", "/Admin/Wallets", withdrawalId: request.Id, walletId: wallet.Id, fragment: "withdrawal-" + request.Id);
        Add("seller", "Wallets", "/Profile/Withdrawals", withdrawalId: request.Id, search: request.Id.ToString(), fragment: "withdrawal-" + request.Id);
        Add("admin", "Wallets", "/Admin/Wallets", walletId: wallet.Id, withdrawalId: request.Id, fragment: "withdrawal-" + request.Id);
        Add("admin", "Wallets", "/Admin/Wallets", message: "Có yêu cầu rút ví " + request.Id + " cần xét duyệt.", walletId: null, fragment: "withdrawal-" + request.Id);
        // The previous legacy case resolves its wallet/request from SQL, even without typed metadata.
        result[^1] = result[^1] with { WalletId = wallet.Id, RequestId = request.Id };
        Add("admin", "Wallets", "/Admin/Wallets", fragment: "withdrawals");
        Add("seller", "Wallets", "/Profile/Withdrawals");
        Add("buyer", "Wallets", "/Profile/Withdrawals", withdrawalId: request.Id);
        var offer = await scope.ServiceProvider.GetRequiredService<IOfferService>().MakeAsync(active.Id, "buyer", decimal.Floor(active.AskingPrice * .9m), default);
        Add("seller", "offers", "/Offers/Details/" + offer.Id, offerId: offer.Id);
        Add("buyer", "offers", "/Offers/Details/" + offer.Id, offerId: offer.Id);
        Add("member", "offers", "/Offers", offerId: offer.Id);
        Add("admin", "orders", "/Admin/Orders/Details/" + order.Id, orderId: order.Id, offerId: offer.Id);
        var conversations = scope.ServiceProvider.GetRequiredService<IConversationService>();
        var conversation = await conversations.OpenAsync(active.Id, "buyer", default);
        await conversations.SendAsync(conversation, "buyer", "Tin nhắn kiểm thử điều hướng.", default);
        Add("seller", "Messages", "/Messages/Details/" + conversation, conversationId: conversation, productId: active.Id);
        Add("buyer", "Messages", "/Messages/Details/" + conversation, conversationId: conversation, productId: active.Id);
        Add("member", "Messages", "/Messages", conversationId: conversation);
        await db.SaveChangesAsync();
        return result;
    }

    private static Task<string> BusinessSnapshot(AdminSqlAuditFactory factory) => factory.ReadAsync(async db =>
        JsonSerializer.Serialize(new { Orders = await AdminSnapshot(db),
            Wallets = await db.Wallets.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Balance }).ToListAsync(),
            Ledger = await db.WalletTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount }).ToListAsync(),
            Withdrawals = await db.WithdrawalRequests.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status }).ToListAsync() }));
    private static async Task<object> AdminSnapshot(AppDbContext db) => await db.Orders.AsNoTracking().OrderBy(x => x.Id)
        .Select(x => new { x.Id, x.Status, x.PaymentStatus, x.ItemAmount, x.TotalAmount }).ToListAsync();
}
