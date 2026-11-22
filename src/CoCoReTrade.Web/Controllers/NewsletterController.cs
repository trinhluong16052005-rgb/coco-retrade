using CoCoReTrade.Application;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CoCoReTrade.Web.Controllers;

public sealed class NewsletterController(INewsletterSubscriptions subscriptions,
    ILogger<NewsletterController> logger) : Controller
{
    private const string SuccessMessage = "CoCo đã nhận đăng ký nhận ưu đãi của bạn.";

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("contact"), RequestSizeLimit(8_192)]
    public async Task<IActionResult> Subscribe(NewsletterForm form, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(form.Website))
            return Respond(form, SuccessMessage, true);
        if (!ModelState.IsValid)
            return Respond(form, "Vui lòng nhập email hợp lệ để đăng ký nhận ưu đãi.", false, 400);

        try
        {
            await subscriptions.SubscribeAsync(form.Email, ct);
            return Respond(form, SuccessMessage, true);
        }
        catch (ArgumentException)
        {
            return Respond(form, "Vui lòng nhập email hợp lệ để đăng ký nhận ưu đãi.", false, 400);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Newsletter signup could not be saved ({ExceptionType}).", ex.GetType().Name);
            return Respond(form, "Chưa thể đăng ký. Vui lòng thử lại sau ít phút.", false, 503);
        }
    }

    private IActionResult Respond(NewsletterForm form, string message, bool success, int statusCode = 200)
    {
        if (Request.GetTypedHeaders().Accept?.Any(item => item.MediaType == "application/json") == true)
            return StatusCode(statusCode, new { success, message });

        TempData["NewsletterMessage"] = message;
        TempData["NewsletterSuccess"] = success;
        if (!success) TempData["NewsletterEmail"] = form.Email;
        var returnUrl = Url.IsLocalUrl(form.ReturnUrl) ? form.ReturnUrl! : "/";
        return LocalRedirect(returnUrl.Split('#')[0] + "#footer-newsletter");
    }
}
