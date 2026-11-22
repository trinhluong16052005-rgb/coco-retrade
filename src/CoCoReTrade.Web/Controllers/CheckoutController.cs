using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(IBasketCheckoutService checkout, CheckoutPageService pages, ShippingPolicy shipping,
    CheckoutVoucherSelection vouchers) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> Index(Guid productId, Guid? offerId, FulfillmentType? fulfillment, CancellationToken ct)
    {
        var form = new CheckoutForm { ProductId = productId, OfferId = offerId, VoucherCode = vouchers.Read(HttpContext, UserId),
            Fulfillment = fulfillment == FulfillmentType.Shipping || !shipping.CanPickup ? FulfillmentType.Shipping : FulfillmentType.Pickup };
        try { ViewBag.CheckoutPage = await pages.PrepareAsync(form, UserId, true, ct); return View(form); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        { TempData["Error"] = ex.Message; return RedirectToAction("Index", "Shop"); }
    }
    [HttpGet]
    public async Task<IActionResult> Recommendations(Guid? productId, CancellationToken ct)
    {
        try { return PartialView("_CheckoutRecommendations", await pages.RecommendationsAsync(productId, UserId, ct)); }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        { return NotFound(); }
    }

    [HttpPost]
    public async Task<IActionResult> Quote(CheckoutForm form, bool applyVoucher, bool reprice, CancellationToken ct)
    {
        var request = form.ToBasket();
        try
        {
            var quote = await checkout.QuoteAsync(request, UserId, ct);
            if (applyVoucher) vouchers.Save(HttpContext, UserId, quote.VoucherCode);
            return Json(quote);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            if (reprice && !applyVoucher && !string.IsNullOrWhiteSpace(request.VoucherCode))
            {
                try
                {
                    var quote = await checkout.QuoteAsync(request with { VoucherCode = null }, UserId, ct);
                    return Json(quote with { VoucherError = ex.Message });
                }
                catch (Exception retry) when (retry is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException) { }
            }
            return BadRequest(new { error = ex.Message });
        }
    }
    [HttpPost]
    public async Task<IActionResult> Index(CheckoutForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var result = await checkout.PlaceAsync(form.ToBasket(), UserId, ct);
                vouchers.Save(HttpContext, UserId, null);
                TempData["Message"] = result.OrderIds.Count == 1 ? "Đơn hàng đã được ghi nhận, đang chờ CoCo xử lý."
                    : $"Đã ghi nhận {result.OrderIds.Count} sản phẩm trong lượt đặt hàng. Theo dõi từng món trong danh sách đơn.";
                return result.OrderIds.Count == 1 ? RedirectToAction("Details", "Orders", new { id = result.OrderIds[0] })
                    : RedirectToAction("Index", "Orders");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
            { ModelState.AddModelError("", ex.Message); }
        }
        try
        {
            ViewBag.CheckoutPage = await pages.PrepareAsync(form, UserId, false, ct);
            return View(form.FromCart ? "~/Views/Cart/Index.cshtml" : "Index", form);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        { TempData["Error"] = ex.Message; return RedirectToAction("Index", form.FromCart ? "Cart" : "Shop"); }
    }
}
