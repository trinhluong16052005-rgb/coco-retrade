using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoCoReTrade.Domain;

namespace CoCoReTrade.Application;

public sealed record BasketRequest(IReadOnlyCollection<Guid> ProductIds, bool FromCart, string? VoucherCode,
    Guid? OfferId, Guid IdempotencyKey, string RecipientName, string RecipientPhone, FulfillmentType Fulfillment,
    string? PickupLocal, string? Province, string? Ward, string? StreetAddress, string? DeliveryNote);
public sealed record BasketLine(Guid ProductId, string Name, string Sku, string Size, decimal Price);
public sealed record BasketQuote(IReadOnlyList<BasketLine> Items, decimal MerchandiseAmount, decimal ShippingFee,
    decimal DiscountAmount, decimal TotalAmount, string? VoucherCode, bool ShippingFeePending,
    DateTime? OfferExpiresAtUtc = null, string? VoucherError = null);
public sealed record BasketResult(Guid BatchId, IReadOnlyList<Guid> OrderIds, decimal TotalAmount);

public interface IBasketRepository
{
    Task<CheckoutBatch?> FindAsync(string buyerId, Guid key, CancellationToken ct);
    Task<IReadOnlyList<Order>> OrdersAsync(Guid batchId, CancellationToken ct);
    Task<CheckoutVoucher?> VoucherAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<CheckoutVoucher>> ActiveVouchersAsync(DateTime nowUtc, CancellationToken ct);
    Task<IReadOnlyList<Guid>> CartIdsAsync(string buyerId, CancellationToken ct);
    void Add(CheckoutBatch batch);
    Task RemovePurchasedAsync(string buyerId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
public interface IBasketCheckoutService
{
    Task<BasketQuote> QuoteAsync(BasketRequest request, string buyerId, CancellationToken ct);
    Task<BasketResult> PlaceAsync(BasketRequest request, string buyerId, CancellationToken ct);
    Task<IReadOnlyList<CheckoutVoucher>> ActiveVouchersAsync(CancellationToken ct);
}

public sealed class BasketCheckoutService(IOrderRepository orders, IBasketRepository baskets,
    MarketplacePolicy marketplace, ShippingPolicy shipping, IShippingFeeService shippingFees,
    TimeProvider clock) : IBasketCheckoutService
{
    public Task<IReadOnlyList<CheckoutVoucher>> ActiveVouchersAsync(CancellationToken ct) =>
        baskets.ActiveVouchersAsync(clock.GetUtcNow().UtcDateTime, ct);

    public async Task<BasketQuote> QuoteAsync(BasketRequest request, string buyerId, CancellationToken ct)
    {
        var resolved = await Resolve(request, buyerId, ct);
        return resolved.Quote;
    }
    public async Task<BasketResult> PlaceAsync(BasketRequest request, string buyerId, CancellationToken ct)
    {
        if (request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(buyerId))
            throw new ArgumentException("Thiếu mã xác nhận đặt hàng.");
        if (request.ProductIds is null || request.ProductIds.Count is < 1 or > 20)
            throw new ArgumentException("Hãy chọn từ 1 đến 20 sản phẩm.");
        var fingerprint = Fingerprint(request);
        try
        {
            return await orders.InTransactionAsync(async () =>
            {
                var existing = await baskets.FindAsync(buyerId, request.IdempotencyKey, ct);
                if (existing is not null) return await Replay(existing, fingerprint, ct);
                var (quote, products, offer) = await Resolve(request, buyerId, ct);
                if (quote.ShippingFeePending) throw new ArgumentException("Hãy nhập đầy đủ địa chỉ giao hàng.");
                var now = clock.GetUtcNow().UtcDateTime;
                var pickup = request.Fulfillment == FulfillmentType.Pickup && !string.IsNullOrWhiteSpace(request.PickupLocal)
                    ? offer is null ? shipping.ParsePickupUtc(request.PickupLocal, now)
                        : shipping.ParseOfferPickupUtc(request.PickupLocal, now, offer.ExpiresAtUtc)
                    : (DateTime?)null;
                if (request.Fulfillment == FulfillmentType.Shipping && !string.IsNullOrWhiteSpace(request.PickupLocal))
                    throw new ArgumentException("Giao tận nơi không dùng giờ nhận tại cửa hàng.");
                var batch = new CheckoutBatch(buyerId, request.IdempotencyKey, fingerprint, now);
                var prices = quote.Items.Select(x => x.Price).ToArray();
                var fees = CheckoutAllocation.Proportional(quote.ShippingFee, prices);
                var capacities = prices.Select(price => CommissionRules.Calculate(price, marketplace.CommissionPercent)).ToArray();
                var discounts = CheckoutAllocation.WithinCommission(quote.DiscountAmount, prices, capacities);
                // Validate every order before reserving any item.
                var created = products.Select((p, i) =>
                {
                    var order = Order.Create(p, offer, buyerId, Guid.NewGuid(), request.Fulfillment,
                        request.RecipientName, request.RecipientPhone, request.Province, request.Ward,
                        request.StreetAddress, pickup, shipping.StoreAddress, quote.ShippingFee,
                        marketplace.CommissionPercent, now, null, request.DeliveryNote);
                    order.AttachCheckout(batch.Id, fees[i], discounts[i], quote.VoucherCode);
                    return order;
                }).ToArray();
                baskets.Add(batch);
                if (offer is not null)
                {
                    offer.UseForOrder(buyerId, products[0].Status, now);
                    orders.AddOfferEvent(offer.Events[^1]);
                }
                var admins = await orders.AdminUserIdsAsync(ct);
                for (var i = 0; i < created.Length; i++)
                {
                    var order = created[i]; products[i].Reserve();
                    await orders.AddAsync(order, ct);
                    orders.AddAudit(new AuditLog { Id = Guid.NewGuid(), ActorId = buyerId, Action = "OrderPlaced", EntityType = "Order",
                        EntityId = order.Id.ToString(), Details = $"Checkout={batch.Id}; voucher={quote.VoucherCode ?? "none"}", CreatedAtUtc = now });
                    foreach (var user in admins.Append(order.BuyerId).Append(order.SellerId).Distinct())
                        orders.AddNotification(new MarketplaceNotification { Id = Guid.NewGuid(), UserId = user,
                            Category = "orders", OrderId = order.Id, ProductId = order.ProductId,
                            Message = $"Đơn {order.ProductName} đã được ghi nhận, đang chờ CoCo xử lý.", CreatedAtUtc = now });
                }
                await baskets.RemovePurchasedAsync(buyerId, request.ProductIds, ct);
                await orders.SaveAsync(ct);
                return new BasketResult(batch.Id, created.Select(x => x.Id).ToArray(), quote.TotalAmount);
            }, ct);
        }
        catch (OrderConcurrencyException)
        {
            var existing = await baskets.FindAsync(buyerId, request.IdempotencyKey, ct);
            if (existing is not null) return await Replay(existing, fingerprint, ct);
            throw;
        }
    }
    private async Task<BasketResult> Replay(CheckoutBatch batch, string hash, CancellationToken ct)
    {
        if (batch.RequestHash != hash) throw new InvalidOperationException("Mã xác nhận đã được dùng cho thông tin khác. Hãy tải lại trang.");
        var existing = await baskets.OrdersAsync(batch.Id, ct);
        if (existing.Count == 0) throw new InvalidOperationException("Không tìm thấy đơn trong lượt thanh toán.");
        return new BasketResult(batch.Id, existing.Select(x => x.Id).ToArray(), existing.Sum(x => x.TotalAmount));
    }
    private async Task<(BasketQuote Quote, IReadOnlyList<Product> Products, Offer? Offer)> Resolve(BasketRequest request, string buyerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw new UnauthorizedAccessException("Hãy đăng nhập để đặt hàng.");
        if (request.ProductIds is null || request.ProductIds.Count is < 1 or > 20 ||
            request.ProductIds.Contains(Guid.Empty) || request.ProductIds.Distinct().Count() != request.ProductIds.Count)
            throw new ArgumentException("Hãy chọn từ 1 đến 20 sản phẩm khác nhau.");
        if (request.OfferId.HasValue && request.ProductIds.Count != 1)
            throw new ArgumentException("Giá thỏa thuận áp dụng riêng cho một sản phẩm.");
        if (request.FromCart)
        {
            var cart = (await baskets.CartIdsAsync(buyerId, ct)).ToHashSet();
            if (request.ProductIds.Any(id => !cart.Contains(id)))
                throw new InvalidOperationException("Giỏ hàng đã thay đổi. Hãy tải lại và chọn sản phẩm.");
        }
        if (request.Fulfillment == FulfillmentType.Pickup && !shipping.CanPickup)
            throw new InvalidOperationException("Chưa cấu hình địa chỉ nhận tại CoCo.");
        var products = new List<Product>();
        foreach (var id in request.ProductIds.Order())
        {
            var product = await orders.ProductAsync(id, ct) ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
            if (product.SellerId == buyerId) throw new UnauthorizedAccessException("Không thể mua sản phẩm của chính mình.");
            if (product.Status != ProductStatus.Active) throw new InvalidOperationException($"{product.Name} không còn mở bán. Hãy cập nhật giỏ hàng.");
            products.Add(product);
        }
        Offer? offer = null;
        if (request.OfferId is { } offerId)
        {
            offer = await orders.OfferAsync(offerId, ct) ?? throw new KeyNotFoundException("Không tìm thấy giá thỏa thuận.");
            if (offer.BuyerId != buyerId || offer.ProductId != products[0].Id)
                throw new UnauthorizedAccessException("Giá thỏa thuận không thuộc sản phẩm hoặc người mua này.");
            if (offer.Status != OfferStatus.Accepted || offer.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime || !offer.AcceptedPrice.HasValue)
                throw new InvalidOperationException("Giá thỏa thuận đã hết hạn hoặc không còn áp dụng.");
        }
        var lines = products.Select(p => new BasketLine(p.Id, p.Name, p.Sku, p.Size, offer?.AcceptedPrice ?? p.AskingPrice)).ToArray();
        var merchandise = lines.Sum(x => x.Price);
        var pending = request.Fulfillment == FulfillmentType.Shipping && string.IsNullOrWhiteSpace(request.Province);
        var fee = pending ? 0 : await shippingFees.FeeAsync(request.Fulfillment, request.Province, request.Ward, ct);
        var code = NormalizeCode(request.VoucherCode);
        if (code is { Length: > 30 }) throw new ArgumentException("Mã giảm giá tối đa 30 ký tự.");
        var discount = code is null ? 0 : (await baskets.VoucherAsync(code, ct)
            ?? throw new InvalidOperationException("Mã giảm giá không tồn tại.")).Discount(merchandise, clock.GetUtcNow().UtcDateTime);
        var capacity = lines.Sum(line => CommissionRules.Calculate(line.Price, marketplace.CommissionPercent));
        discount = Math.Min(discount, capacity);
        return (new BasketQuote(lines, merchandise, fee, discount, merchandise + fee - discount, code, pending, offer?.ExpiresAtUtc), products, offer);
    }
    public static string? NormalizeCode(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
    private static string Fingerprint(BasketRequest request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { Products = request.ProductIds.Order().ToArray(), request.FromCart,
            Voucher = NormalizeCode(request.VoucherCode), request.OfferId, Name = request.RecipientName.Trim(),
            request.RecipientPhone, request.Fulfillment, request.PickupLocal, Province = request.Province?.Trim(),
            Ward = request.Ward?.Trim(), Street = request.StreetAddress?.Trim(), Note = request.DeliveryNote?.Trim() }))));
}
