using System.Security.Claims;
using CoCoReTrade.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Controllers;

[Authorize]
public sealed class SocialController(ISocialService social) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpPost]
    public async Task<IActionResult> RemoveFavorites(Guid[] ids, CancellationToken ct)
    {
        var wantsJson = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (!ModelState.IsValid) throw new ArgumentException("Danh sách món đồ không hợp lệ.");
            var removed = await social.RemoveFavoritesAsync(UserId, ids, ct);
            var message = $"Đã xóa {removed.Count} món khỏi yêu thích.";
            if (wantsJson) return Json(new { removedIds = removed, message });
            TempData["Message"] = message;
        }
        catch (ArgumentException ex)
        {
            if (wantsJson) return BadRequest(new { error = ex.Message });
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction("Saved", "Profile");
    }

    [HttpPost]
    public async Task<IActionResult> Favorite(Guid productId, bool selected, string? returnUrl, CancellationToken ct)
    {
        var wantsJson = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        try
        {
            var saved = await social.SetFavoriteAsync(UserId, productId, selected, ct);
            if (wantsJson) return Json(new { selected = saved });
        }
        catch (KeyNotFoundException)
        {
            return wantsJson ? NotFound(new { error = "Món đồ không còn mở bán." }) : NotFound();
        }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action("Index", "Shop")!);
    }
    [HttpPost]
    public async Task<IActionResult> Follow(string sellerId, bool selected, string? returnUrl, CancellationToken ct)
    {
        try { await social.SetFollowAsync(UserId, sellerId, selected, ct); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { TempData["Error"] = ex.Message; }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action("Index", "Home")!);
    }
}
