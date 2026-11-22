using System.Text.Json;
using CoCoReTrade.Application;
using CoCoReTrade.Web.Controllers;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace CoCoReTrade.Tests;

public sealed class AuthModalFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormGetPreservesFullPageFallbackAndSelectsOnlyRequestedFragment(bool modal)
    {
        var controller = Controller(new Accounts(), modal);
        var result = controller.Login(returnUrl: "/Shop?category=Ao");
        if (modal)
        {
            Assert.Equal("_LoginForm", Assert.IsType<PartialViewResult>(result).ViewName);
            Assert.Equal("login", controller.Response.Headers["X-CoCo-Auth-Fragment"]);
        }
        else Assert.Equal("Login", Assert.IsType<ViewResult>(result).ViewName);
        Assert.Equal("/Shop?category=Ao", controller.ViewData["AuthReturnUrl"]);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task InvalidModalFormReturnsErrorsWithoutCallingAccountService()
    {
        var accounts = new Accounts();
        var controller = Controller(accounts);
        controller.ModelState.AddModelError("Identifier", "Email không hợp lệ.");

        var result = await controller.Login(new LoginForm(), "/Cart");

        var view = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_LoginForm", view.ViewName);
        Assert.False(view.ViewData!.ModelState.IsValid);
        Assert.Equal(0, accounts.LoginCalls);
    }

    [Fact]
    public async Task RejectedPasswordRemainsInModalWithServerError()
    {
        var accounts = new Accounts { LoginResult = new(false, "Thông tin đăng nhập chưa đúng.") };
        var controller = Controller(accounts);

        var result = await controller.Login(new LoginForm { Identifier = "buyer@example.test", Password = "wrong-test-only" }, "/Cart");

        Assert.IsType<PartialViewResult>(result);
        Assert.Contains(controller.ModelState.Values.SelectMany(x => x.Errors), x => x.ErrorMessage == "Thông tin đăng nhập chưa đúng.");
        Assert.Equal(1, accounts.LoginCalls);
    }

    [Theory]
    [InlineData("/Cart", "/Cart")]
    [InlineData("https://external.example.test", "/")]
    [InlineData("//external.example.test", "/")]
    public async Task SuccessfulModalLoginUsesOnlyLocalReturnUrl(string attempted, string expected)
    {
        var controller = Controller(new Accounts());

        var result = await controller.Login(new LoginForm { Identifier = "buyer@example.test", Password = "test-value" }, attempted);

        Assert.Equal(expected, RedirectUrl(result));
    }

    [Fact]
    public async Task SuccessfulFullPageLoginKeepsNormalRedirect()
    {
        var controller = Controller(new Accounts(), modal: false);
        var result = await controller.Login(new LoginForm { Identifier = "buyer@example.test", Password = "test-value" }, "/Cart");
        Assert.Equal("/Cart", Assert.IsType<LocalRedirectResult>(result).Url);
    }

    [Fact]
    public async Task ModalRegistrationMustSendOtpBeforeAdvancingToActivation()
    {
        var sender = new Messages();
        var controller = Controller(new Accounts(), sender: sender);

        var result = await controller.Register(new RegisterForm { FullName = "Người kiểm thử", Email = "buyer@example.test", Phone = "0900000000", Password = "test-value", ConfirmPassword = "test-value" }, "/Checkout");

        var location = RedirectUrl(result);
        Assert.StartsWith("/Account/ConfirmOtp?", location);
        Assert.Contains("userId=test-user", location);
        Assert.Contains("returnUrl=%2FCheckout", location);
        Assert.Equal(1, sender.SentCount);
    }

    [Fact]
    public async Task FailedOtpDeliveryDoesNotPretendRegistrationIsActivated()
    {
        var sender = new Messages { Fail = true };
        var controller = Controller(new Accounts(), sender: sender);

        var result = await controller.Register(new RegisterForm { FullName = "Người kiểm thử", Email = "buyer@example.test", Phone = "0900000000", Password = "test-value", ConfirmPassword = "test-value" });

        Assert.Equal("/Account/ResendActivation", RedirectUrl(result));
        Assert.NotNull(controller.TempData["Error"]);
    }

    private static string RedirectUrl(IActionResult result)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<JsonResult>(result).Value));
        return json.RootElement.GetProperty("redirectUrl").GetString()!;
    }

    private static AccountController Controller(Accounts accounts, bool modal = true, Messages? sender = null)
    {
        var context = new DefaultHttpContext();
        if (modal) context.Request.Headers["X-CoCo-Auth-Modal"] = "1";
        var actionContext = new ActionContext(context, new RouteData(), new ControllerActionDescriptor());
        var controller = new AccountController(accounts, sender ?? new Messages(), new Otps(), new(true, true), null!, null!, null!, TimeProvider.System)
        {
            ControllerContext = new ControllerContext(actionContext),
            Url = new TestUrlHelper(actionContext),
            TempData = new TempDataDictionary(context, new EmptyTempDataProvider())
        };
        return controller;
    }

    private sealed class Accounts : IAccountService
    {
        public int LoginCalls { get; private set; }
        public AccountActionResult LoginResult { get; init; } = new(true, UserId: "test-user");
        public Task<AccountActionResult> LoginAsync(string identifier, string password, bool rememberMe) { LoginCalls++; return Task.FromResult(LoginResult); }
        public Task<AccountActionResult> RegisterAsync(RegisterAccount request) => Task.FromResult(new AccountActionResult(true, UserId: "test-user"));
        public Task<AccountActionResult> ConfirmEmailAsync(string userId, string token) => throw new NotSupportedException();
        public Task LogoutAsync() => throw new NotSupportedException();
        public Task<AccountActionResult> CreateResetTokenAsync(string identifier) => throw new NotSupportedException();
        public Task<AccountActionResult> FindUnconfirmedAsync(string identifier) => throw new NotSupportedException();
        public Task<AccountActionResult> ResetPasswordAsync(string userId, string token, string newPassword) => throw new NotSupportedException();
        public Task<AccountActionResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword) => throw new NotSupportedException();
    }

    private sealed class Otps : IAccountOtpService
    {
        public Task<IssuedAccountOtp> IssueActivationAsync(string userId, CancellationToken ct) => Task.FromResult(new IssuedAccountOtp("buyer@example.test", "123456"));
        public Task<IssuedAccountOtp> IssueResetAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<AccountActionResult> ConfirmActivationAsync(string userId, string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<AccountActionResult> VerifyResetAsync(string userId, string code, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Messages : IAccountMessageSender
    {
        public bool Fail { get; init; }
        public int SentCount { get; private set; }
        public Task SendOtpAsync(string email, string code, string purpose)
        {
            if (Fail) throw new InvalidOperationException("Test delivery failure");
            SentCount++; return Task.CompletedTask;
        }
        public Task SendActivationAsync(string email, string link) => throw new NotSupportedException();
        public Task SendPasswordResetAsync(string email, string link) => throw new NotSupportedException();
    }

    private sealed class TestUrlHelper(ActionContext context) : IUrlHelper
    {
        public ActionContext ActionContext { get; } = context;
        public string? Action(UrlActionContext actionContext)
        {
            var values = new RouteValueDictionary(actionContext.Values);
            return "/Account/" + actionContext.Action + QueryString.Create(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value?.ToString())));
        }
        public bool IsLocalUrl(string? url) => new UrlHelper(ActionContext).IsLocalUrl(url);
        public string? Content(string? contentPath) => contentPath;
        public string? Link(string? routeName, object? values) => throw new NotSupportedException();
        public string? RouteUrl(UrlRouteContext routeContext) => throw new NotSupportedException();
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
