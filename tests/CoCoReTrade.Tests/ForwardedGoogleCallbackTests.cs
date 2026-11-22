using System.Net;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CoCoReTrade.Infrastructure;

namespace CoCoReTrade.Tests;

public sealed class ForwardedGoogleCallbackTests(ForwardedGoogleCallbackTests.PreviewFactory factory)
    : IClassFixture<ForwardedGoogleCallbackTests.PreviewFactory>
{
    [Fact]
    public void PreviewHostCannotUseLiveSqlOrBackgroundWriters()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains("CoCoForwardedHeadersUnused", db.Database.GetConnectionString());
        Assert.DoesNotContain(factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            service => service.GetType().Name is "OfferExpirationWorker" or "OrderExpirationWorker" or "OutboxDispatcherWorker" or "ListingReminderWorker");
    }
    [Fact]
    public void ProgramTrustsOnlyExplicitLoopbackProxiesAndOneProtoHop()
    {
        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Empty(options.KnownIPNetworks);
        Assert.NotEmpty(options.KnownProxies);
        Assert.Contains(IPAddress.Loopback, options.KnownProxies);
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);
        Assert.All(options.KnownProxies, address => Assert.True(IPAddress.IsLoopback(address)));

        var google = factory.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get("Google");
        Assert.Equal("/signin-google", google.CallbackPath.Value);
        Assert.Contains("openid", google.Scope);
        Assert.Contains("profile", google.Scope);
        Assert.Contains("email", google.Scope);
    }

    [Theory]
    [InlineData("127.0.0.1", "https")]
    [InlineData("::1", "https")]
    [InlineData("203.0.113.15", "http")]
    public async Task ProgramOptionsAcceptProtoOnlyFromLoopback(string peer, string expectedScheme)
    {
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask,
            factory.Services.GetRequiredService<ILoggerFactory>(),
            factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>());
        var context = Context(peer, "https");
        // A forwarded Host must never be accepted by this proto-only configuration.
        context.Request.Headers["X-Forwarded-Host"] = "attacker.example.test";

        await middleware.Invoke(context);

        Assert.Equal(expectedScheme, context.Request.Scheme);
        Assert.Equal("coco.example.test", context.Request.Host.Value);
    }

    [Fact]
    public async Task ForwardLimitPreventsEarlierInjectedProtoFromOverridingNearestHop()
    {
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask,
            factory.Services.GetRequiredService<ILoggerFactory>(),
            factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>());
        var context = Context("127.0.0.1", "https, http");

        await middleware.Invoke(context);

        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal("https", context.Request.Headers["X-Forwarded-Proto"].ToString());
    }

    [Theory]
    [InlineData("127.0.0.1", "https")]
    [InlineData("::1", "https")]
    [InlineData("203.0.113.15", "http")]
    public async Task RealPipelineAppliesTrustedProtoBeforeResponse(string peer, string expectedScheme)
    {
        var result = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("coco.example.test");
            context.Request.Path = "/__forwarded_scheme_regression_probe";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        Assert.Equal(expectedScheme, result.Response.Headers[SchemeProbe.Header].ToString());
    }

    private static DefaultHttpContext Context(string peer, string proto)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("coco.example.test");
        context.Request.Path = "/signin-google";
        context.Request.Headers["X-Forwarded-Proto"] = proto;
        return context;
    }

    public sealed class PreviewFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                // Replace providers so this host consumes only test values, never real credentials.
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:CoCo"] = "Server=127.0.0.1;Database=CoCoForwardedHeadersUnused;Trusted_Connection=True;TrustServerCertificate=True",
                    ["Preview:Enabled"] = "true",
                    ["Demo:Enabled"] = "false",
                    ["Smtp:Host"] = "",
                    ["Smtp:From"] = "",
                    ["Smtp:User"] = "",
                    ["Smtp:Password"] = "",
                    ["Notifications:MarketplaceEmailEnabled"] = "false",
                    ["Authentication:Google:ClientId"] = "forwarded-regression-only.apps.googleusercontent.com",
                    ["Authentication:Google:ClientSecret"] = "fake-value-for-local-test-only"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(
                    "Server=127.0.0.1;Database=CoCoForwardedHeadersUnused;Trusted_Connection=True;TrustServerCertificate=True"));
                var writers = services.Where(service => service.ImplementationType?.Name is
                    "OfferExpirationWorker" or "OrderExpirationWorker" or "OutboxDispatcherWorker" or "ListingReminderWorker").ToArray();
                foreach (var writer in writers) services.Remove(writer);
                services.AddSingleton<IStartupFilter>(new SchemeProbe());
            });
        }
    }

    private sealed class SchemeProbe : IStartupFilter
    {
        public const string Header = "X-CoCo-Test-Effective-Scheme";
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers[Header] = context.Request.Scheme;
                    return Task.CompletedTask;
                });
                await following(context);
            });
            next(app);
        };
    }
}
