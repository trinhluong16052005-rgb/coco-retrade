using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = StaffPermissions.ManageOrders)]
public sealed class OrdersController(IAdminOrderService orders, TimeProvider clock,
    OrderExpirationPolicy expiration, IProductRepository? products = null, IDashboardDrilldown? drilldown = null) : Controller
{
    private string AdminId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> Index(string? group, string? search, CancellationToken ct,
        DateOnly? from = null, DateOnly? through = null, OrderStatus? status = null,
        PaymentStatus? paymentStatus = null, string? basis = null)
    {
        if (!ModelState.IsValid) return BadRequest("Bộ lọc đơn hàng không hợp lệ.");
        OrderStatus? groupStatus = group switch
        {
            "active" => OrderStatus.PendingProcessing,
            "shipping" => OrderStatus.Shipping,
            "ready" => OrderStatus.ReadyForPickup,
            "completed" => OrderStatus.Completed,
            "cancelled" => OrderStatus.Cancelled,
            _ => null
        };
        IReadOnlyList<OrderDto> queue;
        if (drilldown is not null)
        {
            try { queue = await drilldown.OrdersAsync(AdminId, from, through, status ?? groupStatus, paymentStatus, basis, ct); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
        }
        else
        {
            if (from.HasValue || through.HasValue) return BadRequest("Cần chọn đầy đủ khoảng ngày.");
            queue = await orders.QueueAsync(AdminId, ct);
        }
        if (from.HasValue && through.HasValue)
        {
            ViewBag.DashboardFrom = from; ViewBag.DashboardThrough = through;
            ViewBag.DashboardBasis = basis; ViewBag.DashboardStatus = status; ViewBag.DashboardPayment = paymentStatus;
        }
        var names = await orders.SellerNamesAsync(AdminId, queue.Select(order => order.SellerId).ToArray(), ct);
        ViewBag.Group = group;
        ViewBag.Search = search;
        ViewBag.SellerNames = names;
        IReadOnlyList<OrderDto> selected = groupStatus.HasValue
            ? queue.Where(x => x.Status == groupStatus.Value).ToList() : queue;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            selected = selected.Where(order => order.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                order.OrderCode.Contains(term, StringComparison.OrdinalIgnoreCase) || order.Id.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                order.ProductSku.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                names.GetValueOrDefault(order.SellerId, "").Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return View(selected);
    }
    [HttpGet]
    public async Task<IActionResult> HeldPickups(string? search, string? remaining, CancellationToken ct)
    {
        var held = await orders.HeldPickupsAsync(AdminId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        ViewBag.Search = search;
        ViewBag.Remaining = remaining;
        ViewBag.NowUtc = now;
        ViewBag.ReservationMinutes = expiration.UnpaidReservationMinutes;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            held = held.Where(order => order.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                order.OrderCode.Contains(term, StringComparison.OrdinalIgnoreCase) || order.OrderId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                order.ProductSku.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                order.SellerName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        var maxHours = remaining switch { "1h" => 1, "6h" => 6, "24h" => 24, _ => 0 };
        if (maxHours > 0)
            held = held.Where(order => (order.PickupDeadlineAtUtc ??
                order.CreatedAtUtc?.AddMinutes(expiration.UnpaidReservationMinutes) ?? now) <= now.AddHours(maxHours)).ToList();
        return View(held);
    }
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var order = await orders.DetailsAsync(id, AdminId, ct);
        if (order is not null && products is not null)
        {
            var product = await products.FindAsync(order.ProductId, ct);
            ViewBag.HasProductImage = product?.Images.Count > 0;
            ViewBag.ProductCondition = product?.VerifiedCondition ?? product?.DeclaredCondition;
        }
        return order is null ? NotFound() : View(order);
    }
    [HttpPost] public Task<IActionResult> Ready(Guid id, string? trackingCode, CancellationToken ct) =>
        Change(id, () => orders.MarkReadyAsync(id, AdminId, ct, trackingCode));
    [HttpPost] public Task<IActionResult> Confirm(Guid id, CancellationToken ct) =>
        Change(id, () => orders.ConfirmAsync(id, AdminId, ct));
    [HttpPost] public Task<IActionResult> ConfirmPayment(Guid id, CancellationToken ct) =>
        Change(id, () => orders.ConfirmPaymentAsync(id, AdminId, null, ct));
    [HttpPost] public Task<IActionResult> Complete(Guid id, CancellationToken ct) =>
        Change(id, () => orders.CompleteAsync(id, AdminId, ct));
    [HttpPost] public Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        Change(id, () => orders.CancelAsync(id, AdminId, ct));
    [HttpPost] public Task<IActionResult> ReturnUndelivered(Guid id, bool itemReturnedToStore, CancellationToken ct) =>
        Change(id, () => orders.ReturnUndeliveredAsync(id, AdminId, itemReturnedToStore, ct));
    private async Task<IActionResult> Change(Guid id, Func<Task<OrderDto>> action)
    {
        try { await action(); TempData["Message"] = "Đã cập nhật đơn hàng."; }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or InvalidOperationException)
        { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }
}
