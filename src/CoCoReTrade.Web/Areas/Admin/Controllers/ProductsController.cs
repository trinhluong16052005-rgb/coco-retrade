using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = StaffPermissions.ManageProducts)]
public sealed class ProductsController(IAdminProductService products, IDashboardDrilldown drilldown) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(ProductStatus? status, string? search, CancellationToken ct,
        DateOnly? from = null, DateOnly? through = null)
    {
        ViewBag.Status = status;
        ViewBag.Search = search;
        if (from.HasValue || through.HasValue)
        {
            if (!ModelState.IsValid || !from.HasValue || !through.HasValue) return BadRequest("Cần chọn đầy đủ khoảng ngày hợp lệ.");
            ViewBag.DashboardFrom = from; ViewBag.DashboardThrough = through;
            try { return View(await drilldown.ProductsAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, from.Value, through.Value, status, search, ct)); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
        }
        return View(await products.ManageAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, status, search, ct));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, string expectedRowVersion, ProductStatus? status, string? search, CancellationToken ct)
    {
        try
        {
            await products.DeleteAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, expectedRowVersion, ct);
            TempData["Message"] = "Đã xóa sản phẩm khỏi danh sách. Lịch sử đơn hàng được giữ nguyên.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { TempData["Error"] = ex.Message; }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        { TempData["Error"] = "Sản phẩm vừa thay đổi. Hãy kiểm tra lại trước khi xóa."; }
        return RedirectToAction(nameof(Index), new { status, search });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteSelected(List<Guid> selectedIds, Dictionary<Guid, string> versions,
        ProductStatus? status, string? search, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid) throw new ArgumentException("Danh sách sản phẩm không hợp lệ. Hãy tải lại trang.");
            var selections = selectedIds.Select(id => new AdminProductDeleteSelection(id,
                versions.GetValueOrDefault(id) ?? "")).ToArray();
            var count = await products.DeleteManyAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, selections, ct);
            TempData["Message"] = $"Đã xóa {count} sản phẩm khỏi danh sách. Lịch sử đơn hàng được giữ nguyên.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { TempData["Error"] = ex.Message; }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        { TempData["Error"] = "Danh sách vừa thay đổi. Chưa xóa sản phẩm nào. Hãy tải lại trang."; }
        return RedirectToAction(nameof(Index), new { status, search });
    }
}
