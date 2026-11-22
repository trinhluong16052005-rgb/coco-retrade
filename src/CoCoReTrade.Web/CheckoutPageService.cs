using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Identity;
namespace CoCoReTrade.Web;
public sealed class CheckoutPageService(ICartService cart, IProductService products, IBasketCheckoutService checkout,
    IAddressBookService addresses, ShippingPolicy shipping, IConfiguration configuration,
    IProductRecommendationService recommendations, UserManager<ApplicationUser> users)
{
    public async Task<CheckoutPageData> PrepareAsync(CheckoutForm form, string userId, bool initial, CancellationToken ct)
    {
        var cartItems = await cart.ItemsAsync(userId, ct);
        var items = form.FromCart ? cartItems : [await GetItemAsync(form.ProductId, userId, ct)];
        var saved = await addresses.ListAsync(userId, ct);
        if (initial)
        {
            form.ProductIds = items.Select(x => x.Id).ToList();
            var user = await users.FindByIdAsync(userId);
            form.RecipientName = saved.FirstOrDefault()?.RecipientName ?? user?.FullName ?? "";
            form.RecipientPhone = saved.FirstOrDefault()?.RecipientPhone ?? user?.PhoneNumber ?? "";
            form.Province = VietnamProvinces.Normalize(saved.FirstOrDefault()?.Province);
            form.Ward = saved.FirstOrDefault()?.Ward;
            form.StreetAddress = saved.FirstOrDefault()?.StreetAddress;
        }
        BasketQuote? quote = null;
        string? error = null;
        if (form.ToBasket().ProductIds.Count > 0)
        {
            try { quote = await checkout.QuoteAsync(form.ToBasket(), userId, ct); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                error = ex.Message;
                if (!string.IsNullOrWhiteSpace(form.VoucherCode))
                    try { quote = await checkout.QuoteAsync(form.ToBasket() with { VoucherCode = null }, userId, ct); }
                    catch (Exception retry) when (retry is ArgumentException or InvalidOperationException or KeyNotFoundException) { }
            }
        }
        var map = configuration["Shipping:StoreMapUrl"];
        var mapUrl = Uri.TryCreate(map, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.ToString() : null;
        IReadOnlyList<CheckoutVoucher> vouchers = items.Count == 0 ? [] : await checkout.ActiveVouchersAsync(ct);
        var suggestions = await RecommendAsync(items, cartItems, userId, ct);
        return new(items, saved, quote, shipping, mapUrl, vouchers, suggestions, error);
    }

    public async Task<CheckoutRecommendations> RecommendationsAsync(Guid? productId, string userId, CancellationToken ct)
    {
        var cartItems = await cart.ItemsAsync(userId, ct);
        var items = productId is { } id ? new[] { await GetItemAsync(id, userId, ct) } : cartItems;
        return await RecommendAsync(items, cartItems, userId, ct);
    }

    private async Task<ProductCard> GetItemAsync(Guid id, string userId, CancellationToken ct)
    {
        var product = await products.VisibleDetailsAsync(id, userId, false, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
        if (product.Status != ProductStatus.Active || product.SellerId == userId)
            throw new InvalidOperationException("Sản phẩm không thể đặt mua lúc này.");
        return new ProductCard(product.Id, product.Sku, product.Name, product.Brand, product.Category,
            product.Size, product.AskingPrice, product.VerifiedCondition ?? product.DeclaredCondition,
            product.Status, product.ImageIds.Count > 0 ? product.ImageIds[0] : null, product.SellerId);
    }

    private async Task<CheckoutRecommendations> RecommendAsync(IReadOnlyList<ProductCard> items,
        IReadOnlyList<ProductCard> cartItems, string userId, CancellationToken ct)
    {
        if (items.Count == 0) return new([], []);
        var excluded = cartItems.Select(x => x.Id).ToArray();
        var similar = await recommendations.SimilarForBasketAsync(items, userId, excluded, ct);
        var crossSell = await recommendations.CrossSellAsync(items.Select(x => x.Id).ToArray(), userId, ct,
            excluded.Concat(similar.Select(x => x.Id)).ToArray(), limit: 6);
        return new(crossSell, similar);
    }
}
