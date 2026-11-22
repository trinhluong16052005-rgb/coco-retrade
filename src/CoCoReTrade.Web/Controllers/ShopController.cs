using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Controllers;

public sealed class ShopController(IProductService products, ISocialService social, IUserProfileService profiles,
    ICartService cart) : Controller
{
    public async Task<IActionResult> Index([FromQuery] ProductFilter filter, [FromQuery] string? category,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (ProductCategories.IsAccessory(category)) category = ProductCategories.Accessories;
        filter = filter with { Category = category };
        ViewBag.Filter = filter;
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        ViewBag.FavoriteIds = userId is null ? Array.Empty<Guid>() : await social.FavoriteIdsAsync(userId, ct);
        return View(await products.ShopPageAsync(filter, page, ct));
    }
    [HttpGet("/Shop/Details/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await products.VisibleDetailsAsync(id, User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null,
            User.IsInRole("Admin") && User.HasClaim(StaffPermissions.ClaimType, StaffPermissions.ManageProducts), ct);
        if (result is null) return NotFound();
        ViewBag.SellerName = await social.SellerNameAsync(result.SellerId, ct);
        ViewBag.SellerProfile = result.Status == CoCoReTrade.Domain.ProductStatus.Active
            ? await profiles.GetAsync(result.SellerId, ct) : null;
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var favoriteIds = userId is not null ? await social.FavoriteIdsAsync(userId, ct) : Array.Empty<Guid>();
        ViewBag.IsFavorite = favoriteIds.Contains(result.Id);
        ViewBag.FavoriteIds = favoriteIds;
        var recommendations = HttpContext.RequestServices.GetRequiredService<IProductRecommendationService>();
        var cartIds = userId is null ? Array.Empty<Guid>() : (await cart.ItemsAsync(userId, ct)).Select(x => x.Id).ToArray();
        var complementary = result.Status == CoCoReTrade.Domain.ProductStatus.Active
            ? await recommendations.CrossSellAsync([result.Id], userId, ct, cartIds, limit: 5) : [];
        ViewBag.ComplementaryProducts = complementary;
        var shown = complementary.Select(x => x.Id).Concat(cartIds).ToHashSet();
        ViewBag.SimilarProducts = await recommendations.SimilarAsync(result, userId, ct, shown, limit: 5);
        return View(result);
    }
}
