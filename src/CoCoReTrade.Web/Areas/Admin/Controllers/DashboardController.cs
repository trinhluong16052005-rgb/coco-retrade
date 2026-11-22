using System.Security.Claims;
using CoCoReTrade.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = StaffPermissions.ReadPending)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController(IAdminDashboardService dashboard) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct, string period = "30d",
        DateOnly? from = null, DateOnly? through = null)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!ModelState.IsValid)
        {
            ModelState.AddModelError(string.Empty, "Ngày không hợp lệ. Hãy chọn lại khoảng thời gian.");
            return View(await dashboard.GetAsync(actorId, new(), ct));
        }
        try { return View(await dashboard.GetAsync(actorId, new(period, from, through), ct)); }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await dashboard.GetAsync(actorId, new(), ct));
        }
    }
}
