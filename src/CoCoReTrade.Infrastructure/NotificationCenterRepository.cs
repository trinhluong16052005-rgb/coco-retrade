using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class NotificationCenterRepository(AppDbContext db) : INotificationCenterRepository
{
    public async Task<IReadOnlyList<MarketplaceNotification>> ForUserAsync(string userId, CancellationToken ct)
    {
        var notifications = await db.MarketplaceNotifications.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(300).ToListAsync(ct);
        await NotificationProductNames.ApplyAsync(db, notifications, ct);
        await NotificationOrderCodes.ApplyAsync(db, notifications, ct);
        return notifications;
    }
    public async Task<MarketplaceNotification?> OwnedAsync(string userId, Guid id, CancellationToken ct)
    {
        var notification = await db.MarketplaceNotifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        // Read-state writes must not persist display-only message enrichment.
        return notification;
    }

    private IQueryable<MarketplaceNotification> UnreadForUser(string userId) =>
        db.MarketplaceNotifications.Where(x => x.UserId == userId && !x.IsRead);

    public Task<bool> HasUnreadAsync(string userId, CancellationToken ct) =>
        UnreadForUser(userId).AnyAsync(ct);

    public Task MarkAllReadAsync(string userId, CancellationToken ct) =>
        UnreadForUser(userId).ExecuteUpdateAsync(update => update.SetProperty(x => x.IsRead, true), ct);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
