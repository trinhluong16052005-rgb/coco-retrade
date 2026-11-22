using System.Security.Claims;
using CoCoReTrade.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = StaffPermissions.ManageWallets)]
public sealed class ReportsController(IAdminFinancialReportService reports) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Revenue(string period = "month", DateOnly? date = null,
        CancellationToken ct = default, DateOnly? from = null, DateOnly? through = null)
    {
        if (!ModelState.IsValid) return BadRequest("Khoảng ngày báo cáo không hợp lệ.");
        try
        {
            var report = await reports.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, period, date, ct, from, through);
            if (period == "custom") { ViewBag.DashboardFrom = from; ViewBag.DashboardThrough = through; }
            return View(report);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
