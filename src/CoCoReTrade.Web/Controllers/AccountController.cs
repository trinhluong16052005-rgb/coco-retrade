using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;

namespace CoCoReTrade.Web.Controllers;

public sealed class AccountController(IAccountService accounts, IAccountMessageSender messages,
    IAccountOtpService otps, AccountDeliveryStatus delivery, IExternalAccountService externalAccounts,
    SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, TimeProvider clock) : Controller
{
    [HttpPost]
    public IActionResult Google(string? returnUrl = null)
    {
        if (!delivery.GoogleReady) { TempData["Error"] = "Đăng nhập Google đang chờ cấu hình. Vui lòng dùng email hoặc số điện thoại."; return RedirectToAction(nameof(Login)); }
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Profile");
        var callback = Url.Action(nameof(GoogleCallback), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" })!;
        return Challenge(signIn.ConfigureExternalAuthenticationProperties("Google", callback), "Google");
    }
    [HttpGet]
    public async Task<IActionResult> GoogleCallback(string? returnUrl, bool link = false, string? remoteError = null, CancellationToken ct = default)
    {
        if (!delivery.GoogleReady || remoteError is not null) { TempData["Error"] = "Chưa thể kết nối Google. Vui lòng thử lại."; return RedirectToAction(nameof(Login)); }
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var info = await signIn.GetExternalLoginInfoAsync(link ? userId ?? "invalid" : null);
        if (info is null || info.LoginProvider != "Google") { TempData["Error"] = "Phiên Google đã hết hạn. Vui lòng thử lại."; return RedirectToAction(nameof(Login)); }
        var account = new GoogleAccount(info.ProviderKey, info.Principal.FindFirstValue(ClaimTypes.Email) ?? "",
            info.Principal.FindFirstValue(ClaimTypes.Name) ?? "", string.Equals(info.Principal.FindFirstValue("coco:google-email-verified"), "true", StringComparison.OrdinalIgnoreCase));
        AccountActionResult result;
        try
        {
            if (link)
            {
                string? timestamp = null;
                info.AuthenticationProperties?.Items.TryGetValue("coco:password-at", out timestamp);
                if (userId is null || !long.TryParse(timestamp, out var seconds) ||
                    Math.Abs(clock.GetUtcNow().ToUnixTimeSeconds() - seconds) > 300)
                    result = new(false, "Hãy xác nhận lại mật khẩu trước khi liên kết Google.");
                else result = await externalAccounts.LinkGoogleAsync(userId, account, ct);
            }
            else if (userId is not null) result = new(false, "Bạn đang đăng nhập. Hãy dùng mục liên kết Google trong hồ sơ.");
            else result = await externalAccounts.GoogleSignInAsync(account, ct);
        }
        finally { await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme); }
        if (!result.Succeeded) { TempData["Error"] = result.Error; return RedirectToAction(link ? nameof(LinkGoogle) : nameof(Login)); }
        if (link) { TempData["Message"] = "Đã liên kết Google với tài khoản CoCo."; return RedirectToAction("Index", "Profile"); }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
    [Authorize, HttpGet] public IActionResult LinkGoogle() => View(new LinkGoogleForm());
    [Authorize, HttpPost]
    public async Task<IActionResult> LinkGoogle(LinkGoogleForm form)
    {
        if (!delivery.GoogleReady) ModelState.AddModelError("", "Đăng nhập Google đang chờ cấu hình.");
        if (!ModelState.IsValid) return View(form);
        var user = await users.GetUserAsync(User);
        if (user is null || user.TrustSuspended || !user.EmailConfirmed ||
            !(await signIn.CheckPasswordSignInAsync(user, form.CurrentPassword, lockoutOnFailure: true)).Succeeded)
        { ModelState.AddModelError("", "Không thể xác nhận mật khẩu. Vui lòng kiểm tra lại."); return View(form); }
        var properties = signIn.ConfigureExternalAuthenticationProperties("Google", Url.Action(nameof(GoogleCallback), new { link = true })!, user.Id);
        properties.Items["coco:password-at"] = clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Challenge(properties, "Google");
    }
    [HttpGet] public IActionResult Register(string? returnUrl = null) => AuthForm(nameof(Register), new RegisterForm(), returnUrl);
    [HttpPost]
    public async Task<IActionResult> Register(RegisterForm form, string? returnUrl = null)
    {
        var destination = AuthReturnUrl(returnUrl);
        if (!ModelState.IsValid) return AuthForm(nameof(Register), form, destination);
        if (!delivery.EmailReady) { ModelState.AddModelError("", "CoCo chưa bật gửi mã kích hoạt qua email. Vui lòng thử lại sau hoặc chọn đăng nhập Google khi khả dụng."); return AuthForm(nameof(Register), form, destination); }
        var result = await accounts.RegisterAsync(new RegisterAccount(form.FullName, form.Email, form.Phone, form.Password));
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return AuthForm(nameof(Register), form, destination); }
        IssuedAccountOtp issued;
        try
        {
            issued = await otps.IssueActivationAsync(result.UserId!, HttpContext.RequestAborted);
            await messages.SendOtpAsync(issued.Email, issued.Code, "activation");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Mail.SmtpException)
        {
            TempData["Error"] = "Tài khoản đã tạo nhưng chưa gửi được mã. Hãy dùng chức năng gửi lại mã kích hoạt.";
            return AuthRedirect(Url.Action(nameof(ResendActivation))!);
        }
        ViewBag.UserId = result.UserId;
        ViewBag.Flow = "activation";
        if (IsAuthModalRequest)
            return AuthRedirect(Url.Action(nameof(ConfirmOtp), new { userId = result.UserId, returnUrl = destination })!);
        return View("CheckEmail");
    }
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string userId, string token)
    {
        var result = await accounts.ConfirmEmailAsync(userId, token);
        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded ? "Tài khoản đã kích hoạt. Bạn có thể đăng nhập." : result.Error;
        return RedirectToAction(nameof(Login));
    }
    [HttpGet]
    public IActionResult ConfirmOtp(string userId, string? returnUrl = null)
    {
        AuthReturnUrl(returnUrl);
        return View(new AccountOtpForm { UserId = userId });
    }
    [HttpPost]
    public async Task<IActionResult> ConfirmOtp(AccountOtpForm form, CancellationToken ct, string? returnUrl = null)
    {
        var destination = AuthReturnUrl(returnUrl);
        if (!ModelState.IsValid) return View(form);
        var result = await otps.ConfirmActivationAsync(form.UserId, form.Code, ct);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return View(form); }
        TempData["Message"] = "Tài khoản đã kích hoạt. Bạn có thể đăng nhập.";
        return RedirectToAction(nameof(Login), new { returnUrl = destination });
    }
    [HttpGet] public IActionResult Login(string? externalError = null, string? returnUrl = null)
    {
        if (externalError is not null) ModelState.AddModelError("", "Không hoàn tất đăng nhập Google. Vui lòng thử lại.");
        return AuthForm(nameof(Login), new LoginForm(), returnUrl);
    }
    [HttpGet] public IActionResult ResendActivation() => View(new ResetRequestForm());
    [HttpPost]
    public async Task<IActionResult> ResendActivation(ResetRequestForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(form);
        if (!delivery.EmailReady) { ModelState.AddModelError("", "Dịch vụ gửi mã qua email đang chờ cấu hình. Vui lòng thử lại sau."); return View(form); }
        var account = await accounts.FindUnconfirmedAsync(form.Identifier);
        if (account.UserId is not null)
        {
            try
            {
                var issued = await otps.IssueActivationAsync(account.UserId, ct);
                await messages.SendOtpAsync(issued.Email, issued.Code, "activation");
                ViewBag.UserId = account.UserId;
                ViewBag.Flow = "activation";
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Net.Mail.SmtpException)
            {
                ModelState.AddModelError(string.Empty, ex is System.Net.Mail.SmtpException ?
                    "Không gửi được email lúc này. Vui lòng thử lại sau." : ex.Message);
                return View(form);
            }
        }
        else
        {
            ViewBag.UserId = Guid.NewGuid().ToString("N");
            ViewBag.Flow = "activation";
        }
        return View("CheckEmail");
    }
    [HttpPost]
    public async Task<IActionResult> Login(LoginForm form, string? returnUrl = null)
    {
        var destination = AuthReturnUrl(returnUrl);
        if (!ModelState.IsValid) return AuthForm(nameof(Login), form, destination);
        var result = await accounts.LoginAsync(form.Identifier, form.Password, form.RememberMe);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return AuthForm(nameof(Login), form, destination); }
        return AuthRedirect(destination);
    }
    [Authorize, HttpPost] public async Task<IActionResult> Logout()
    { await accounts.LogoutAsync(); return RedirectToAction("Index", "Home"); }

    [HttpGet] public IActionResult ForgotPassword() => View(new ResetRequestForm());
    [HttpPost]
    public async Task<IActionResult> ForgotPassword(ResetRequestForm form)
    {
        if (!ModelState.IsValid) return View(form);
        if (!delivery.EmailReady) { ModelState.AddModelError("", "Dịch vụ gửi mã qua email đang chờ cấu hình. Vui lòng thử lại sau."); return View(form); }
        var result = await accounts.CreateResetTokenAsync(form.Identifier);
        if (result.UserId is not null)
        {
            IssuedAccountOtp issued;
            try
            {
                issued = await otps.IssueResetAsync(result.UserId, HttpContext.RequestAborted);
                await messages.SendOtpAsync(issued.Email, issued.Code, "reset");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Net.Mail.SmtpException)
            {
                ModelState.AddModelError(string.Empty, ex is System.Net.Mail.SmtpException ?
                    "Không gửi được email lúc này. Vui lòng thử lại sau." : ex.Message);
                return View(form);
            }
            ViewBag.UserId = result.UserId;
            ViewBag.Flow = "reset";
        }
        else
        {
            ViewBag.UserId = Guid.NewGuid().ToString("N");
            ViewBag.Flow = "reset";
        }
        return View("CheckEmail");
    }
    [HttpGet]
    public IActionResult VerifyResetOtp(string userId) => View(new AccountOtpForm { UserId = userId });
    [HttpPost]
    public async Task<IActionResult> VerifyResetOtp(AccountOtpForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(form);
        var result = await otps.VerifyResetAsync(form.UserId, form.Code, ct);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return View(form); }
        return View("ResetPassword", new ResetPasswordForm { UserId = result.UserId!, Token = result.Token! });
    }
    [HttpPost]
    public async Task<IActionResult> ResendOtp(string userId, string purpose, CancellationToken ct, string? returnUrl = null)
    {
        var destination = AuthReturnUrl(returnUrl);
        if (purpose is not ("activation" or "reset")) return BadRequest();
        if (!delivery.EmailReady) { TempData["Error"] = "Dịch vụ gửi mã qua email đang chờ cấu hình. Vui lòng thử lại sau."; return RedirectToAction(purpose == "activation" ? nameof(ConfirmOtp) : nameof(VerifyResetOtp), new { userId, returnUrl = destination }); }
        try
        {
            var issued = purpose == "activation" ? await otps.IssueActivationAsync(userId, ct)
                : await otps.IssueResetAsync(userId, ct);
            await messages.SendOtpAsync(issued.Email, issued.Code, purpose);
            TempData["Message"] = "Đã gửi mã xác thực mới.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or System.Net.Mail.SmtpException)
        {
            if (ex is KeyNotFoundException) TempData["Message"] = "Nếu tài khoản hợp lệ, mã mới đã được gửi.";
            else TempData["Error"] = ex is System.Net.Mail.SmtpException ?
                "Không gửi được email lúc này. Vui lòng thử lại sau." : ex.Message;
        }
        return RedirectToAction(purpose == "activation" ? nameof(ConfirmOtp) : nameof(VerifyResetOtp), new { userId, returnUrl = destination });
    }
    [HttpGet] public IActionResult ResetPassword(string userId, string token) => View(new ResetPasswordForm { UserId = userId, Token = token });
    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordForm form)
    {
        if (!ModelState.IsValid) return View(form);
        var result = await accounts.ResetPasswordAsync(form.UserId, form.Token, form.Password);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return View(form); }
        TempData["Message"] = "Mật khẩu đã được cập nhật và bạn đã đăng nhập vào phiên mới.";
        return RedirectToAction("Index", "Home");
    }
    [Authorize, HttpGet] public IActionResult ChangePassword() => View(new ChangePasswordForm());
    [Authorize, HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordForm form)
    {
        if (!ModelState.IsValid) return View(form);
        var result = await accounts.ChangePasswordAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, form.CurrentPassword, form.NewPassword);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return View(form); }
        TempData["Message"] = "Mật khẩu đã đổi thành công.";
        return RedirectToAction("Index", "Home");
    }
    public IActionResult AccessDenied() => View();

    private bool IsAuthModalRequest => string.Equals(Request.Headers["X-CoCo-Auth-Modal"], "1", StringComparison.Ordinal);

    private string AuthReturnUrl(string? returnUrl)
    {
        var destination = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        ViewData["AuthReturnUrl"] = destination;
        return destination;
    }

    private IActionResult AuthForm(string view, object model, string? returnUrl)
    {
        AuthReturnUrl(returnUrl);
        Response.Headers.CacheControl = "no-store";
        if (!IsAuthModalRequest) return View(view, model);
        Response.Headers["X-CoCo-Auth-Fragment"] = view.ToLowerInvariant();
        return PartialView($"_{view}Form", model);
    }

    private IActionResult AuthRedirect(string? returnUrl)
    {
        var destination = AuthReturnUrl(returnUrl);
        return IsAuthModalRequest ? Json(new { redirectUrl = destination }) : LocalRedirect(destination);
    }
}
