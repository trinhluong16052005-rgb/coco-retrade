using System.Text.RegularExpressions;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class NotificationNavigation(AppDbContext db, IStaffPermissionChecker permissions) : INotificationNavigation
{
    public async Task<NotificationDestination> ResolveAsync(string userId, NotificationDto notice,
        IReadOnlySet<string> grantedPermissions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new UnauthorizedAccessException("Hãy đăng nhập.");
        var allowed = new Dictionary<string, bool>();
        async Task<bool> Can(string permission)
        {
            if (!grantedPermissions.Contains(permission)) return false;
            if (allowed.TryGetValue(permission, out var result)) return result;
            try { await permissions.EnsureAsync(userId, permission, ct); return allowed[permission] = true; }
            catch (UnauthorizedAccessException) { return allowed[permission] = false; }
        }
        var category = notice.Category.Trim().ToLowerInvariant();
        if (category == "support" || notice.SupportTicketId.HasValue)
        {
            var staff = await Can(StaffPermissions.ManageUsers);
            var tickets = db.SupportTickets.AsNoTracking().Where(x => x.UserId == userId || staff);
            var id = notice.SupportTicketId;
            if (!id.HasValue)
            {
                // Old notices had no ticket ID. Exact stored event time is safe only for a unique match.
                var matches = await tickets.Where(x => x.CreatedAtUtc == notice.CreatedAtUtc || x.LastActivityAtUtc == notice.CreatedAtUtc ||
                    x.Messages.Any(m => m.CreatedAtUtc == notice.CreatedAtUtc))
                    .Select(x => x.Id).Distinct().Take(2).ToListAsync(ct);
                if (matches.Count == 1) id = matches[0];
            }
            var ticket = id.HasValue ? await tickets.Where(x => x.Id == id.Value).Select(x => new { x.Id, x.UserId }).SingleOrDefaultAsync(ct) : null;
            return ticket is not null
                ? new(ticket.UserId == userId ? "Support" : "HelpDesk", "Details", ticket.UserId == userId ? "" : "Admin", ticket.Id)
                : new(staff ? "HelpDesk" : "Support", Area: staff ? "Admin" : "");
        }
        if (category == "wallets" || notice.WithdrawalRequestId.HasValue)
        {
            var staff = await Can(StaffPermissions.ManageWallets);
            var id = notice.WithdrawalRequestId;
            if (!id.HasValue)
            {
                // Legacy admin notices explicitly contained the request GUID; verify it against SQL.
                var match = Regex.Match(notice.Message, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");
                if (Guid.TryParse(match.Value, out var legacyId)) id = legacyId;
            }
            var request = id.HasValue ? await (from r in db.WithdrawalRequests.AsNoTracking()
                join w in db.Wallets.AsNoTracking() on r.WalletId equals w.Id
                where r.Id == id.Value && (w.UserId == userId || staff)
                select new { r.Id, r.WalletId, w.UserId }).SingleOrDefaultAsync(ct) : null;
            if (request is not null)
                return request.UserId == userId
                    ? new("Profile", "Withdrawals", Fragment: "withdrawal-" + request.Id, Search: request.Id.ToString())
                    : new("Wallets", Area: "Admin", Fragment: "withdrawal-" + request.Id, WalletId: request.WalletId, RequestId: request.Id);
            return staff ? new("Wallets", Area: "Admin", Fragment: "withdrawals") : new("Profile", "Withdrawals");
        }
        if (category is "cart" or "carts") return new("Cart");
        if (category == "messages" || category is not ("orders" or "offers" or "listings" or "social") && notice.ConversationId.HasValue)
        {
            if (notice.ConversationId is Guid id && await db.Conversations.AnyAsync(x => x.Id == id && (x.BuyerId == userId || x.SellerId == userId), ct))
                return new("Messages", "Details", Id: id);
            return new("Messages");
        }
        if (category == "orders" || notice.OrderId.HasValue)
        {
            var staff = await Can(StaffPermissions.ManageOrders);
            if (notice.OrderId is Guid id && await db.Orders.AnyAsync(x => x.Id == id && (x.BuyerId == userId || x.SellerId == userId || staff), ct))
                return new("Orders", "Details", staff ? "Admin" : "", id);
            return new("Orders", Area: staff ? "Admin" : "");
        }
        if (category == "offers" || notice.OfferId.HasValue)
        {
            if (notice.OfferId is Guid id && await db.Offers.AnyAsync(x => x.Id == id && (x.BuyerId == userId || x.SellerId == userId), ct))
                return new("Offers", "Details", Id: id);
            return new("Offers");
        }
        if (category is "listings" or "social" || notice.ProductId.HasValue)
        {
            var product = notice.ProductId is Guid id ? await db.Products.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.SellerId, x.Status, x.PreliminaryStatus }).SingleOrDefaultAsync(ct) : null;
            if (product?.SellerId == userId)
            {
                if (category == "listings")
                {
                    if (product.Status == ProductStatus.PendingApproval && product.PreliminaryStatus == PreliminaryReviewStatus.ChangesRequested)
                        return new("Listing", "Edit", Id: product.Id);
                    return new("Listing", "Closet", Fragment: product.Status == ProductStatus.Removed ? null : "consignment-" + product.Id);
                }
                return product.Status == ProductStatus.Removed ? new("Listing", "Closet") : new("Shop", "Details", Id: product.Id);
            }
            var inspect = await Can(StaffPermissions.ReadPending);
            if (category == "listings" && product is not null && inspect)
            {
                if (product.Status == ProductStatus.PendingApproval) return new("Inspection", "Review", "Admin", product.Id);
                if (await Can(StaffPermissions.ManageProducts)) return new("Inspection", "Details", "Admin", product.Id);
            }
            if (product?.Status == ProductStatus.Active) return new("Shop", "Details", Id: product.Id);
            return category == "listings" && inspect ? new("Inspection", Area: "Admin") : new("Listing", "Closet");
        }
        return new("Profile", "Notifications");
    }
}
