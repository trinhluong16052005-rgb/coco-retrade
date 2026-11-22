using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CoCoReTrade.Web.Controllers;

public sealed class AboutController(ISupportService support, ILogger<AboutController> logger) : Controller
{
    [HttpGet] public IActionResult Index() => View();
    [HttpGet] public IActionResult Inspection() => View();
    [HttpGet] public IActionResult Policies() => View();
    [HttpGet] public IActionResult Returns() => View();
    [HttpGet] public IActionResult Help() => RedirectToActionPermanent(nameof(Contact));

    [HttpGet, AllowAnonymous]
    public IActionResult Contact() => View(new ContactForm
    {
        Email = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.Email) ?? "" : ""
    });

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("contact"), RequestSizeLimit(32_000)]
    public async Task<IActionResult> Contact(ContactForm form, CancellationToken ct)
    {
        // Treat bot submissions like success without creating a ticket or revealing the trap.
        if (!string.IsNullOrWhiteSpace(form.Website)) return ContactSent();
        if (!ModelState.IsValid) return View(form);

        var ownerId = User.Identity?.IsAuthenticated == true
            ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        // The permanently locked, passwordless system account owns guest requests.
        // Sender email is contact data only; it must never resolve another user's account.
        if (string.IsNullOrWhiteSpace(ownerId)) ownerId = "system";
        var name = form.Name.Trim();
        var phone = new string(form.Phone.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        var body = $"Liên hệ từ website CoCo\nHọ và tên: {name}\nEmail: {form.Email.Trim()}\nSố điện thoại: {phone}\n\nNội dung:\n{form.Message.Trim()}";
        try
        {
            await support.CreateAsync(ownerId, $"Liên hệ từ {name}", body, ct);
            return ContactSent();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never log submitted fields, addresses, or message bodies.
            logger.LogError("Contact request could not be queued ({ExceptionType}).", ex.GetType().Name);
            ModelState.AddModelError(string.Empty, "Chưa thể gửi lời nhắn. Vui lòng thử lại sau ít phút.");
            return View(form);
        }
    }

    private IActionResult ContactSent()
    {
        TempData["ContactSuccess"] = true;
        return RedirectToAction(nameof(Contact));
    }
}
