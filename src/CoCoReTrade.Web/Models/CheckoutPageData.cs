using CoCoReTrade.Application;
using CoCoReTrade.Domain;
namespace CoCoReTrade.Web.Models;
public sealed record CheckoutPageData(IReadOnlyList<ProductCard> Items, IReadOnlyList<AddressDto> Addresses,
    BasketQuote? Quote, ShippingPolicy Shipping, string? StoreMapUrl, IReadOnlyList<CheckoutVoucher> Vouchers,
    CheckoutRecommendations Recommendations, string? QuoteError = null);

public sealed record CheckoutRecommendations(IReadOnlyList<ProductCard> CrossSell, IReadOnlyList<ProductCard> Similar);
