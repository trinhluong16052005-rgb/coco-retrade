using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class AdminDashboardRepository(AppDbContext db, OrderExpirationPolicy expiration) : IAdminDashboardRepository
{
    public async Task<DashboardSource> ReadAsync(DashboardRange range, DateTime nowUtc, bool includeOrders,
        bool includeFinancials, CancellationToken ct)
    {
        var fromUtc = range.PreviousFromUtc; var toUtc = range.ToUtc;
        // Aggregate in SQL by Vietnam day. Never join one-to-many ledger/image rows into sales totals.
        var products = await db.Products.AsNoTracking().Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc)
            .GroupBy(x => DateOnly.FromDateTime(x.CreatedAtUtc.AddHours(7)))
            .Select(g => new DashboardProductDay(g.Key, g.Count(x => x.Status == ProductStatus.PendingApproval),
                g.Count(x => x.Status == ProductStatus.Active))).ToListAsync(ct);
        IReadOnlyList<DashboardOrderDay> orders = [];
        if (includeOrders)
        {
            var cutoff = nowUtc.AddMinutes(-expiration.UnpaidReservationMinutes);
            orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc)
                .GroupBy(x => DateOnly.FromDateTime(x.CreatedAtUtc.AddHours(7)))
                .Select(g => new DashboardOrderDay(g.Key,
                    g.Count(x => x.Status == OrderStatus.PendingProcessing), g.Count(x => x.Status == OrderStatus.Shipping),
                    g.Count(x => x.Status == OrderStatus.ReadyForPickup), g.Count(x => x.Status == OrderStatus.Completed),
                    g.Count(x => x.Status == OrderStatus.Cancelled),
                    g.Count(x => x.Fulfillment == FulfillmentType.Pickup && x.PaymentStatus == PaymentStatus.Unpaid &&
                        (x.Status == OrderStatus.PendingProcessing || x.Status == OrderStatus.ReadyForPickup) &&
                        (x.PickupDeadlineAtUtc != null ? x.PickupDeadlineAtUtc > nowUtc : x.CreatedAtUtc > cutoff) &&
                        db.Products.Any(p => p.Id == x.ProductId && p.Status == ProductStatus.Reserved)),
                    g.Count(x => x.Fulfillment == FulfillmentType.Shipping &&
                        (x.Status == OrderStatus.PendingProcessing || x.Status == OrderStatus.Shipping))))
                .ToListAsync(ct);
        }
        var sales = await db.Orders.AsNoTracking().Where(x => x.Status == OrderStatus.Completed &&
                x.PaymentStatus == PaymentStatus.Paid && x.CompletedAtUtc >= fromUtc && x.CompletedAtUtc < toUtc)
            .GroupBy(x => DateOnly.FromDateTime(x.CompletedAtUtc!.Value.AddHours(7)))
            .Select(g => new DashboardSalesDay(g.Key, g.Select(x => x.ProductId).Distinct().Count(),
                includeFinancials ? g.Sum(x => x.ItemAmount - x.DiscountAmount) : 0,
                includeFinancials ? g.Sum(x => x.CommissionAmount - x.DiscountAmount) : 0))
            .ToListAsync(ct);
        IReadOnlyList<DashboardWithdrawalDay> withdrawals = [];
        if (includeFinancials)
            withdrawals = await db.WithdrawalRequests.AsNoTracking().Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc &&
                    (x.Status == "Pending" || x.Status == "Approved"))
                .GroupBy(x => DateOnly.FromDateTime(x.CreatedAtUtc.AddHours(7)))
                .Select(g => new DashboardWithdrawalDay(g.Key, g.Count())).ToListAsync(ct);
        return new(products, orders, sales, withdrawals);
    }
}
