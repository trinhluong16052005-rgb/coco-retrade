using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CoCoReTrade.Tests;

public sealed class DashboardUiReviewSetup
{
    [Fact]
    public async Task IsolatedDashboardBrowserReview()
    {
        var qa = Environment.GetEnvironmentVariable("COCO_DASHBOARD_UI_REVIEW");
        if (string.IsNullOrWhiteSpace(qa)) return;
        var root = Path.GetFullPath(qa); Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "review-config.json"); var resultPath = Path.Combine(root, "review-done.json");
        Assert.False(File.Exists(resultPath), "Use a fresh review folder.");
        await using var factory = new AdminSqlAuditFactory(new DashboardSqlTests.DashboardClock());
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var bootstrap = factory.CreateClient();
        try
        {
            await factory.InitializeUsersAsync(); await DashboardSqlTests.Seed(factory);
            if (Environment.GetEnvironmentVariable("COCO_STATUS_UI_REVIEW") == "1")
            {
                await AdminOrderStatusSqlTests.SeedStatusCases(factory);
                await AdminOrderStatusSqlTests.WriteBrowserExpectations(factory, root);
            }
            var url = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { url, password = factory.Password, qa = root }));
            var stopAt = DateTime.UtcNow.AddMinutes(5);
            while (!File.Exists(resultPath) && DateTime.UtcNow < stopAt) await Task.Delay(500);
            Assert.True(File.Exists(resultPath), "Dashboard browser review timed out.");
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
            Assert.True(result.RootElement.GetProperty("success").GetBoolean(), "Dashboard review found an issue; inspect review-done.json.");
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
            await factory.DeleteDatabaseAsync();
        }
    }
}
