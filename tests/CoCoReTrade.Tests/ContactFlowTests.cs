using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Web.Controllers;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CoCoReTrade.Tests;

public sealed class ContactFlowTests
{
    [Theory]
    [InlineData(null, "system")]
    [InlineData("signed-in-buyer", "signed-in-buyer")]
    public async Task ValidContactQueuesTicketAndAdminNotificationsWithoutExposingTicket(string? actor, string expectedOwner)
    {
        var repository = new SupportRepository();
        var support = Service(repository);
        var controller = Controller(support, actor);
        var form = Form();
        Assert.True(Validate(controller, form));

        var result = Assert.IsType<RedirectToActionResult>(await controller.Contact(form, default));

        Assert.Equal("Contact", result.ActionName);
        Assert.Null(result.RouteValues);
        Assert.Equal(true, controller.TempData["ContactSuccess"]);
        var ticket = Assert.Single(repository.Tickets);
        Assert.Equal(expectedOwner, ticket.UserId);
        Assert.Contains(form.Name, ticket.Subject);
        var message = Assert.Single(ticket.Messages);
        Assert.Equal(expectedOwner, message.SenderId);
        Assert.Contains("Họ và tên: Nguyễn Minh", message.Body);
        Assert.Contains("Email: other-account@example.test", message.Body);
        Assert.Contains("Số điện thoại: +84901234567", message.Body);
        Assert.Contains(form.Message, message.Body);
        Assert.Equal(new[] { "admin", "admin-2" }, repository.Notifications.Select(x => x.UserId));
        Assert.All(repository.Notifications, notice => Assert.Equal("Support", notice.Category));
        Assert.Equal(ticket.Id, Assert.Single(await support.AdminQueueAsync("admin", default)).Id);
        Assert.False(await support.CanAccessAsync(ticket.Id, "", default));
        Assert.False(await support.CanAccessAsync(ticket.Id, "other-account", default));
        Assert.False(await support.CanAccessAsync(ticket.Id, "unrelated-visitor", default));
    }

    [Theory]
    [InlineData("", "member@example.test", "0901234567", "Cần hỗ trợ")]
    [InlineData("Nguyễn Minh", "not-an-email", "0901234567", "Cần hỗ trợ")]
    [InlineData("Nguyễn Minh", "member@example.test", "phone", "Cần hỗ trợ")]
    [InlineData("Nguyễn Minh", "member@example.test", "0901234567", "ab")]
    public async Task InvalidContactRetainsFieldsAndCreatesNoTicket(string name, string email, string phone, string message)
    {
        var repository = new SupportRepository();
        var controller = Controller(Service(repository));
        var form = new ContactForm { Name = name, Email = email, Phone = phone, Message = message };
        Assert.False(Validate(controller, form));

        var result = Assert.IsType<ViewResult>(await controller.Contact(form, default));

        Assert.Same(form, result.Model);
        Assert.False(result.ViewData.ModelState.IsValid);
        Assert.Empty(repository.Tickets);
        Assert.Empty(repository.Notifications);
        Assert.Null(controller.TempData["ContactSuccess"]);
    }

    [Theory]
    [InlineData("0901234567")]
    [InlineData("+84 90 123 4567")]
    [InlineData("84 (90) 123-4567")]
    [InlineData("+1 (415) 555-2671")]
    public void CommonPhoneFormatsValidate(string phone)
    {
        var form = Form();
        form.Phone = phone;
        Assert.True(Validator.TryValidateObject(form, new ValidationContext(form), [], true));
    }

    [Fact]
    public async Task MaximumFieldLengthsFitExistingSupportMessageLimit()
    {
        var repository = new SupportRepository();
        var controller = Controller(Service(repository));
        var form = new ContactForm
        {
            Name = new string('N', 100), Email = new string('a', 64) + "@" + new string('b', 185) + ".com",
            Phone = "+123456789012345", Message = new string('M', 1500)
        };
        Assert.True(Validate(controller, form));

        Assert.IsType<RedirectToActionResult>(await controller.Contact(form, default));

        Assert.InRange(Assert.Single(Assert.Single(repository.Tickets).Messages).Body.Length, 1, 2000);
    }

    [Fact]
    public async Task HoneypotSubmissionCreatesNoTicket()
    {
        var repository = new SupportRepository();
        var controller = Controller(Service(repository));
        var form = Form();
        form.Website = "https://spam.example.test";

        Assert.IsType<RedirectToActionResult>(await controller.Contact(form, default));

        Assert.Equal(true, controller.TempData["ContactSuccess"]);
        Assert.Empty(repository.Tickets);
        Assert.Empty(repository.Notifications);
    }

    [Fact]
    public async Task StorageFailureRetainsFieldsAndShowsOnlySafeError()
    {
        var repository = new SupportRepository { SaveFailure = new InvalidOperationException("Sensitive database details") };
        var controller = Controller(Service(repository));
        var form = Form();

        var result = Assert.IsType<ViewResult>(await controller.Contact(form, default));

        Assert.Same(form, result.Model);
        var error = Assert.Single(controller.ModelState.Values.SelectMany(x => x.Errors));
        Assert.Equal("Chưa thể gửi lời nhắn. Vui lòng thử lại sau ít phút.", error.ErrorMessage);
        Assert.Null(controller.TempData["ContactSuccess"]);
    }

    [Fact]
    public async Task RequestCancellationIsNotReportedAsStorageFailure()
    {
        var repository = new SupportRepository { SaveFailure = new OperationCanceledException() };
        var controller = Controller(Service(repository));

        await Assert.ThrowsAsync<OperationCanceledException>(() => controller.Contact(Form(), default));

        Assert.True(controller.ModelState.IsValid);
        Assert.Null(controller.TempData["ContactSuccess"]);
    }

    [Fact]
    public void ContactGetReturnsTypedFormAndHelpPermanentlyRedirects()
    {
        var controller = Controller(Service(new SupportRepository()));
        Assert.IsType<ContactForm>(Assert.IsType<ViewResult>(controller.Contact()).Model);
        var redirect = Assert.IsType<RedirectToActionResult>(controller.Help());
        Assert.True(redirect.Permanent);
        Assert.Equal("Contact", redirect.ActionName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task AnonymousHttpPostRequiresValidAntiforgeryToken(string? token)
    {
        var repository = new SupportRepository();
        await using var app = await HttpApp(repository);
        using var client = app.GetTestServer().CreateClient();
        var values = new Dictionary<string, string>
        {
            ["Name"] = "Nguyễn Minh", ["Email"] = "guest@example.test",
            ["Phone"] = "0901234567", ["Message"] = "Tôi cần CoCo tư vấn."
        };
        if (token is not null) values["__RequestVerificationToken"] = token;

        using var response = await client.PostAsync("/About/Contact", new FormUrlEncodedContent(values));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(repository.Tickets);
        Assert.Empty(repository.Notifications);
    }

    [Fact]
    public async Task AnonymousHttpPostWithAntiforgeryQueuesSenderMetadataAndRedirectsToContact()
    {
        var repository = new SupportRepository();
        await using var app = await HttpApp(repository);
        using var client = app.GetTestServer().CreateClient();
        using var tokenResponse = await client.GetAsync("/__antiforgery");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var requestToken = await tokenResponse.Content.ReadAsStringAsync();
        var cookie = string.Join("; ", tokenResponse.Headers.GetValues("Set-Cookie").Select(x => x.Split(';', 2)[0]));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/About/Contact")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = requestToken,
                ["Name"] = "Nguyễn Minh", ["Email"] = "guest@example.test",
                ["Phone"] = "+84 90 123 4567", ["Message"] = "Tôi cần CoCo tư vấn về ký gửi."
            })
        };
        Assert.True(request.Headers.TryAddWithoutValidation("Cookie", cookie));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/About/Contact", response.Headers.Location?.OriginalString);
        var support = Service(repository);
        var ticket = Assert.Single(await support.AdminQueueAsync("admin", default));
        Assert.Equal("system", ticket.UserId);
        var details = await support.DetailsAsync(ticket.Id, "admin", default);
        var message = Assert.Single(details.Messages);
        Assert.Contains("Họ và tên: Nguyễn Minh", message.Body);
        Assert.Contains("Email: guest@example.test", message.Body);
        Assert.Contains("Số điện thoại: +84901234567", message.Body);
        Assert.Contains("Tôi cần CoCo tư vấn về ký gửi.", message.Body);
        Assert.Equal(new[] { "admin", "admin-2" }, repository.Notifications.Select(x => x.UserId));
        Assert.All(repository.Notifications, notice => Assert.Equal("Support", notice.Category));
    }

    private static async Task<WebApplication> HttpApp(SupportRepository repository)
    {
        // Isolated MVC host: no Program startup, configuration files, SQL, accounts, or hosted workers.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AboutController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AboutController).Assembly);
        builder.Services.AddSingleton<ISupportService>(Service(repository));
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("contact", limiter =>
        { limiter.PermitLimit = 5; limiter.Window = TimeSpan.FromMinutes(10); }));
        var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapGet("/__antiforgery", async context =>
        {
            var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
            await context.Response.WriteAsync(tokens.RequestToken!);
        });
        app.MapControllerRoute("default", "{controller}/{action}");
        await app.StartAsync();
        return app;
    }

    private static ContactForm Form() => new()
    {
        Name = "Nguyễn Minh", Email = "other-account@example.test", Phone = "+84 90 123 4567",
        Message = "Tôi cần tư vấn về dịch vụ ký gửi."
    };

    private static bool Validate(AboutController controller, ContactForm form)
    {
        var errors = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(form, new ValidationContext(form), errors, true);
        foreach (var error in errors)
            foreach (var member in error.MemberNames.DefaultIfEmpty(string.Empty))
                controller.ModelState.AddModelError(member, error.ErrorMessage!);
        return valid;
    }

    private static SupportService Service(SupportRepository repository) =>
        new(repository, new Permissions(), TimeProvider.System, new Realtime());

    private static AboutController Controller(ISupportService support, string? actor = null)
    {
        var context = new DefaultHttpContext();
        if (actor is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor)], "test"));
        return new AboutController(support, NullLogger<AboutController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new EmptyTempDataProvider())
        };
    }

    private sealed class SupportRepository : ISupportRepository
    {
        public List<SupportTicket> Tickets { get; } = [];
        public List<MarketplaceNotification> Notifications { get; } = [];
        public Exception? SaveFailure { get; init; }
        public Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) => work();
        public Task<SupportTicket?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Tickets.SingleOrDefault(x => x.Id == id));
        public Task<string?> OwnerIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Tickets.SingleOrDefault(x => x.Id == id)?.UserId);
        public Task<IReadOnlyList<SupportTicketRow>> ListAsync(string? owner, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SupportTicketRow>>(Tickets.Where(x => owner is null || x.UserId == owner).Select(Row).ToList());
        public Task<SupportTicketRow?> RowAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Tickets.Where(x => x.Id == id).Select(Row).SingleOrDefault());
        public Task<IReadOnlyList<string>> AdminIdsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["admin", "admin-2"]);
        public void Add(SupportTicket ticket) => Tickets.Add(ticket);
        public void AddMessage(SupportMessage message) { }
        public void AddNotification(MarketplaceNotification notification) => Notifications.Add(notification);
        public void AddAudit(AuditLog audit) { }
        public Task SaveAsync(CancellationToken ct) => SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
        private static SupportTicketRow Row(SupportTicket ticket) =>
            new(ticket.Id, ticket.Subject, ticket.Status, ticket.UserId, ticket.UserId, ticket.LastActivityAtUtc);
    }

    private sealed class Permissions : IStaffPermissionChecker
    {
        public Task EnsureAsync(string actorId, string permission, CancellationToken ct) => actorId is "admin" or "admin-2"
            ? Task.CompletedTask : Task.FromException(new UnauthorizedAccessException());
    }

    private sealed class Realtime : ISupportRealtimePublisher
    {
        public Task PublishChangedAsync(Guid ticketId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
