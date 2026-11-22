using CoCoReTrade.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace CoCoReTrade.Infrastructure;

internal static partial class NotificationOrderCodes
{
    [GeneratedRegex(@"^ORD-\d{8}-\d{3,}\s·\s")]
    private static partial Regex Prefix();

    public static async Task ApplyAsync(AppDbContext db, IReadOnlyCollection<MarketplaceNotification> notifications, CancellationToken ct)
    {
        var ids = notifications.Where(n => n.OrderId.HasValue).Select(n => n.OrderId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return;
        var codes = db.ChangeTracker.Entries<Order>().Where(e => ids.Contains(e.Entity.Id) && e.Entity.OrderCode.Length > 0)
            .ToDictionary(e => e.Entity.Id, e => e.Entity.OrderCode);
        var missing = ids.Where(id => !codes.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            foreach (var row in await db.Orders.AsNoTracking().Where(o => missing.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderCode }).ToListAsync(ct)) codes[row.Id] = row.OrderCode;
        foreach (var notification in notifications)
            if (notification.OrderId is Guid id && codes.TryGetValue(id, out var code) && code.Length > 0)
                notification.Message = code + " · " + Prefix().Replace(notification.Message, "");
    }
}
