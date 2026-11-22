using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

// Read-only order lists and chart selections. Date/status predicates execute in SQL.
public sealed class DashboardDrilldown(AppDbContext db, IStaffPermissionChecker permissions, TimeProvider clock)
    : IDashboardDrilldown
{
    public async Task<IReadOnlyList<OrderDto>> OrdersAsync(string actorId, DateOnly? from, DateOnly? through,
        OrderStatus? status, PaymentStatus? payment, string? basis, CancellationToken ct)
    {
        await permissions.EnsureAsync(actorId, StaffPermissions.ManageOrders, ct);
        if (from.HasValue != through.HasValue) throw new ArgumentException("Cần chọn đầy đủ khoảng ngày.");
        var range = from.HasValue ? DashboardRange.Create(new("custom", from.Value, through!.Value), clock.GetUtcNow()) : null;
        if (status.HasValue && !Enum.IsDefined(status.Value) || payment.HasValue && !Enum.IsDefined(payment.Value) ||
            basis is not (null or "" or "created" or "completed")) throw new ArgumentException("Bộ lọc đơn hàng không hợp lệ.");
        var query = db.Orders.AsNoTracking();
        if (basis == "completed") query = query.Where(x => x.Status == OrderStatus.Completed && x.PaymentStatus == PaymentStatus.Paid);
        if (range is not null)
        {
            var start = range.FromUtc; var end = range.ToUtc;
            query = basis == "completed"
                ? query.Where(x => x.CompletedAtUtc >= start && x.CompletedAtUtc < end)
                : query.Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end);
        }
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (payment.HasValue) query = query.Where(x => x.PaymentStatus == payment.Value);
        var items = await query.OrderByDescending(x => basis == "completed" ? x.CompletedAtUtc : x.CreatedAtUtc)
            .ThenByDescending(x => x.Id).ToListAsync(ct);
        return items.Select(x => new OrderDto(x.Id, x.ProductId, x.OfferId, x.ProductSku, x.ProductName,
            x.ProductSize, x.BuyerId, x.SellerId, x.IdempotencyKey, x.Fulfillment, x.RecipientName, x.RecipientPhone,
            x.Province, x.Ward, x.StreetAddress, x.PickupAtUtc, x.PickupAddress, x.ItemAmount, x.ShippingFee, x.TotalAmount,
            x.CommissionAmount, x.Status, x.PaymentStatus, x.PaymentReference, x.CreatedAtUtc, x.PaidAtUtc,
            x.CancelledAtUtc, x.CompletedAtUtc, x.PreferredDeliveryWindow, x.DeliveryNote, x.PickupDeadlineAtUtc,
            TrackingCode: x.TrackingCode, DiscountAmount: x.DiscountAmount, DiscountCode: x.DiscountCode,
            CheckoutBatchId: x.CheckoutBatchId, OrderCode: x.OrderCode)).ToList();
    }

    public async Task<IReadOnlyList<AdminProductItem>> ProductsAsync(string actorId, DateOnly from,
        DateOnly through, ProductStatus? status, string? search, CancellationToken ct)
    {
        await permissions.EnsureAsync(actorId, status == ProductStatus.PendingApproval
            ? StaffPermissions.ReadPending : StaffPermissions.ManageProducts, ct);
        var range = DashboardRange.Create(new("custom", from, through), clock.GetUtcNow());
        if (status.HasValue && !Enum.IsDefined(status.Value))
            throw new ArgumentException("Bộ lọc sản phẩm không hợp lệ.");
        var start = range.FromUtc; var end = range.ToUtc;
        var query = from p in db.Products.AsNoTracking()
                    join u in db.Users.AsNoTracking() on p.SellerId equals u.Id
                    where p.Status != ProductStatus.Removed && (!status.HasValue || p.Status == status.Value) && p.CreatedAtUtc >= start && p.CreatedAtUtc < end
                    select new { Product = p, SellerName = u.FullName };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Product.Name.Contains(term) || x.Product.Sku.Contains(term) ||
                x.Product.ConsignmentCode.Contains(term) || x.SellerName.Contains(term));
        }
        return await query.OrderByDescending(x => x.Product.CreatedAtUtc).ThenByDescending(x => x.Product.Id)
            .Select(x => new AdminProductItem(x.Product.Id, x.Product.ConsignmentCode, x.Product.Sku,
                x.Product.Name, x.Product.Brand, x.SellerName, x.Product.Status, x.Product.VerifiedCondition,
                x.Product.CreatedAtUtc, x.Product.PreliminaryStatus, Convert.ToBase64String(x.Product.RowVersion),
                x.Product.Images.OrderBy(i => i.SortOrder).Select(i => (Guid?)i.Id).FirstOrDefault())).ToListAsync(ct);
    }
}
