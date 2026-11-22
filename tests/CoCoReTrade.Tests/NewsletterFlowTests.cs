using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Web.Controllers;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace CoCoReTrade.Tests;

public sealed class NewsletterFlowTests
{
    [Fact]
    public void SubscriptionNormalizesEmailAndPreservesSignupTime()
    {
        var now = DateTime.UtcNow;
        var subscription = new NewsletterSubscription(" Member+CoCo@Example.test ", now);
        Assert.Equal("member+coco@example.test", subscription.Email);
        Assert.Equal(now, subscription.CreatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing-address")]
    [InlineData("Member <member@example.test>")]
    public void InvalidAddressesCannotBecomeSubscriptions(string email) =>
        Assert.Throws<ArgumentException>(() => new NewsletterSubscription(email, DateTime.UtcNow));

    [Fact]
    public async Task AjaxSignupSavesEmailAndReturnsOnlyConfirmation()
    {
        var subscriptions = new Subscriptions();
        var controller = Controller(subscriptions);
        var result = Assert.IsType<ObjectResult>(await controller.Subscribe(Form(), default));
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("member@example.test", Assert.Single(subscriptions.Emails));
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"success\":true", json);
        Assert.DoesNotContain("member@example.test", json);
    }

    [Fact]
    public async Task InvalidFormAndHoneypotDoNotSaveEmail()
    {
        var subscriptions = new Subscriptions();
        var invalid = Controller(subscriptions);
        var form = Form();
        form.Email = "invalid";
        Assert.False(Validator.TryValidateObject(form, new ValidationContext(form), [], true));
        invalid.ModelState.AddModelError(nameof(form.Email), "Email không hợp lệ.");
        Assert.Equal(400, Assert.IsType<ObjectResult>(await invalid.Subscribe(form, default)).StatusCode);

        var bot = Form();
        bot.Website = "bot";
        Assert.Equal(200, Assert.IsType<ObjectResult>(await Controller(subscriptions).Subscribe(bot, default)).StatusCode);
        Assert.Empty(subscriptions.Emails);
    }

    [Fact]
    public async Task StorageFailureReturnsErrorWithoutInternalDetails()
    {
        var controller = Controller(new Subscriptions { Fail = true });
        var result = Assert.IsType<ObjectResult>(await controller.Subscribe(Form(), default));
        Assert.Equal(503, result.StatusCode);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"success\":false", json);
        Assert.DoesNotContain("Internal database details", json);
    }

    [Theory]
    [InlineData("/Shop?page=2", "/Shop?page=2#footer-newsletter")]
    [InlineData("https://outside.example", "/#footer-newsletter")]
    [InlineData("//outside.example", "/#footer-newsletter")]
    public async Task SignupWithoutJavaScriptReturnsToLocalFooter(string target, string expected)
    {
        var controller = Controller(new Subscriptions(), json: false);
        var form = Form();
        form.ReturnUrl = target;
        var result = Assert.IsType<LocalRedirectResult>(await controller.Subscribe(form, default));
        Assert.Equal(expected, result.Url);
        Assert.Equal(true, controller.TempData["NewsletterSuccess"]);
    }

    private static NewsletterForm Form() => new() { Email = "member@example.test", ReturnUrl = "/" };

    private static NewsletterController Controller(Subscriptions subscriptions, bool json = true)
    {
        var http = new DefaultHttpContext();
        if (json) http.Request.Headers.Accept = "application/json";
        var controller = new NewsletterController(subscriptions, NullLogger<NewsletterController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataProvider())
        };
        controller.Url = new UrlHelper(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        return controller;
    }

    private sealed class Subscriptions : INewsletterSubscriptions
    {
        public List<string> Emails { get; } = [];
        public bool Fail { get; init; }
        public Task SubscribeAsync(string email, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("Internal database details");
            Emails.Add(email);
            return Task.CompletedTask;
        }
    }

    private sealed class TempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
