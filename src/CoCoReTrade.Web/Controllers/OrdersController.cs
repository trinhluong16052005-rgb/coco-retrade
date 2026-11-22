using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Web.Demo;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Controllers;

[Authorize]
public sealed class OrdersController(IOrderService orders, IProductRepository products, DemoCatalog demo) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool CanServeProductImage => !demo.Enabled || DemoCatalog.IsLocalHost(Request.Host.Host);

    [HttpGet] public async Task<IActionResult> Index(string? search, CancellationToken ct)
    {
        var rows = await orders.MineAsync(UserId, ct);
        ViewBag.Search = search;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(o => o.OrderCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                o.Id.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                o.ProductSku.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                o.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return View(rows);
    }
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var order = await orders.DetailsAsync(id, UserId, User.IsInRole("Admin"), ct);
        if (order is null) return NotFound();

        var product = await products.FindAsync(order.ProductId, ct);
        return View(new OrderDetailsPage(order, product?.VerifiedCondition ?? product?.DeclaredCondition,
            CanServeProductImage && product?.Images.Count > 0));
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ProductImage(Guid id, [FromServices] IPrivateImageStore images, CancellationToken ct)
    {
        if (!CanServeProductImage) return NotFound();
        var order = await orders.DetailsAsync(id, UserId, User.IsInRole("Admin"), ct);
        if (order is null) return NotFound();

        var product = await products.FindAsync(order.ProductId, ct);
        var cover = product?.Images.MinBy(image => image.SortOrder);
        if (cover is null) return NotFound();

        try { return File(await images.OpenAsync(cover.StorageKey, ct), cover.ContentType); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or ArgumentException)
        { return NotFound(); }
    }
    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try { await orders.CancelAsync(id, UserId, User.IsInRole("Admin"), ct); TempData["Message"] = "Đã hủy đơn và mở bán lại sản phẩm."; }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }
}
