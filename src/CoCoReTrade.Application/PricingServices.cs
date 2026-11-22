using CoCoReTrade.Domain;

namespace CoCoReTrade.Application;

public interface IShippingZoneRepository
{
    Task<bool> IsInnerCityAsync(string province, string ward, CancellationToken ct);
}

public interface IShippingFeeService
{
    Task<decimal> FeeAsync(FulfillmentType fulfillment, string? province, string? ward, CancellationToken ct);
}

public sealed class ShippingFeeService : IShippingFeeService
{
    public Task<decimal> FeeAsync(FulfillmentType fulfillment, string? province, string? ward, CancellationToken ct)
    {
        if (fulfillment == FulfillmentType.Pickup) return Task.FromResult(0m);
        if (fulfillment != FulfillmentType.Shipping) throw new ArgumentOutOfRangeException(nameof(fulfillment));
        var city = VietnamProvinces.Normalize(province)
            ?? throw new ArgumentException("Hãy chọn tỉnh hoặc thành phố giao hàng.");
        return Task.FromResult(ShippingFeeRules.Calculate(fulfillment, ShippingFeeRules.IsDaNang(city)));
    }
}

public interface ICommissionService
{
    decimal Calculate(decimal merchandisePrice);
}

public sealed class CommissionService(MarketplacePolicy policy) : ICommissionService
{
    public decimal Calculate(decimal merchandisePrice) =>
        CommissionRules.Calculate(merchandisePrice, policy.CommissionPercent);
}
