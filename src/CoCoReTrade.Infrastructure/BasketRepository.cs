using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class BasketRepository(AppDbContext db) : IBasketRepository
{
    public Task<CheckoutBatch?> FindAsync(string buyerId, Guid key, CancellationToken ct) =>
        db.CheckoutBatches.AsNoTracking().SingleOrDefaultAsync(x => x.BuyerId == buyerId && x.IdempotencyKey == key, ct);
    public async Task<IReadOnlyList<Order>> OrdersAsync(Guid batchId, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Where(x => x.CheckoutBatchId == batchId).OrderBy(x => x.Id).ToListAsync(ct);
    public Task<CheckoutVoucher?> VoucherAsync(string code, CancellationToken ct) =>
        db.CheckoutVouchers.AsNoTracking().SingleOrDefaultAsync(x => x.Code == code, ct);
    public async Task<IReadOnlyList<CheckoutVoucher>> ActiveVouchersAsync(DateTime nowUtc, CancellationToken ct) =>
        await db.CheckoutVouchers.AsNoTracking().Where(x => x.Enabled && x.EndsAtUtc > nowUtc && x.Percent >= 1 && x.Percent <= 35)
            .OrderBy(x => x.MinimumAmount).ThenBy(x => x.Code).ToListAsync(ct);
    public async Task<IReadOnlyList<Guid>> CartIdsAsync(string buyerId, CancellationToken ct) =>
        await db.CartItems.Where(x => x.UserId == buyerId).Select(x => x.ProductId).ToListAsync(ct);
    public void Add(CheckoutBatch batch) => db.CheckoutBatches.Add(batch);
    public async Task RemovePurchasedAsync(string buyerId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        db.CartItems.RemoveRange(await db.CartItems.Where(x => x.UserId == buyerId && ids.Contains(x.ProductId)).ToListAsync(ct));
}
