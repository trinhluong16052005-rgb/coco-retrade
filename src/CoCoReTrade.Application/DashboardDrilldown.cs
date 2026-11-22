using CoCoReTrade.Domain;

namespace CoCoReTrade.Application;

public interface IDashboardDrilldown
{
    Task<IReadOnlyList<OrderDto>> OrdersAsync(string actorId, DateOnly? from, DateOnly? through,
        OrderStatus? status, PaymentStatus? payment, string? basis, CancellationToken ct);
    Task<IReadOnlyList<AdminProductItem>> ProductsAsync(string actorId, DateOnly from,
        DateOnly through, ProductStatus? status, string? search, CancellationToken ct);
}
