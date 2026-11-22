using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CoCoReTrade.Tests;

public sealed class NotificationUiReviewSetup
{
    [Fact]
    public async Task IsolatedNotificationBrowserReview()
    {
        var qa = Environment.GetEnvironmentVariable("COCO_NOTIFICATION_UI_REVIEW");
        if (string.IsNullOrWhiteSpace(qa)) return;
        var root = Path.GetFullPath(qa); Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "review-config.json"); var resultPath = Path.Combine(root, "review-done.json");
        Assert.False(File.Exists(resultPath), "Use a fresh review folder.");
        await using var factory = new AdminSqlAuditFactory(new DashboardSqlTests.DashboardClock());
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0)); using var bootstrap = factory.CreateClient();
        try
        {
            await factory.InitializeUsersAsync(); await DashboardSqlTests.Seed(factory);
            var cases = await NotificationNavigationSqlTests.Seed(factory);
            var url = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            await File.WriteAllTextAsync(Path.Combine(root, "navigation-cases.json"), JsonSerializer.Serialize(cases, AdminSqlAuditFactory.Json));
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { url, password = factory.Password }));
            var stopAt = DateTime.UtcNow.AddMinutes(5);
            while (!File.Exists(resultPath) && DateTime.UtcNow < stopAt) await Task.Delay(500);
            Assert.True(File.Exists(resultPath), "Notification browser review timed out.");
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
            Assert.True(result.RootElement.GetProperty("success").GetBoolean(), "Notification review failed; inspect review-done.json.");
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
            await factory.DeleteDatabaseAsync();
        }
    }
}
