using System.Security.Claims;
using CoCoReTrade.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Controllers.Api.V1;

[ApiController, Authorize(Policy = StaffPermissions.ReadPending), Route("api/v1/admin/dashboard")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDashboardController(IAdminDashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminDashboardOverview>> Index(CancellationToken ct,
        string period = "30d", DateOnly? from = null, DateOnly? through = null)
    {
        try { return Ok(await dashboard.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, new(period, from, through), ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
