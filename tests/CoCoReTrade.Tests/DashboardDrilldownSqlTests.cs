using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.Extensions.DependencyInjection;
using static CoCoReTrade.Tests.AdminSqlAuditFactory;

namespace CoCoReTrade.Tests;

public sealed class DashboardDrilldownSqlTests
{
    [Fact]
    public async Task ChartDestinationsKeepExactDatesStatusesPermissionsAndOriginalReports()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new AdminSqlAuditFactory(new DashboardSqlTests.DashboardClock()); using var bootstrap = factory.CreateClient();
        try
        {
            await factory.InitializeUsersAsync(); await DashboardSqlTests.Seed(factory);
            using var admin = await factory.LoginAsync("admin");
            var data = await Success<AdminDashboardOverview>(await admin.GetAsync("/api/v1/admin/dashboard?period=7d"));
            const string dates = "from=2026-11-16&through=2026-11-22";
            foreach (var state in data.OrderStatuses)
            {
                var html = await admin.GetStringAsync("/Admin/Orders?" + dates + "&status=" + state.Key);
                Assert.Contains("data-dashboard-selection", html);
                Assert.Equal(state.Count, Regex.Matches(html, "href=\"/Admin/Orders/Details/").Count);
                Assert.Contains("name=\"from\" value=\"2026-11-16\"", html);
                Assert.Contains("name=\"status\" value=\"" + state.Key + "\"", html);
            }
            var sold = await admin.GetStringAsync("/Admin/Orders?" + dates + "&status=Completed&paymentStatus=Paid&basis=completed");
            Assert.Equal(3, Regex.Matches(sold, "href=\"/Admin/Orders/Details/").Count);
            Assert.Contains("Theo ng", sold); Assert.Contains("name=\"basis\" value=\"completed\"", sold);
            var active = await admin.GetStringAsync("/Admin/Products?" + dates + "&status=Active");
            Assert.Equal(2, Regex.Matches(active, "class=\"admin-product-identity\"").Count);
            var pending = await admin.GetStringAsync("/Admin/Inspection?" + dates);
            Assert.Equal(2, Regex.Matches(pending, "href=\"/Admin/Inspection/Review/").Count);
            foreach (var bucket in data.Series)
            {
                using var scope = factory.Services.CreateScope();
                var reports = scope.ServiceProvider.GetRequiredService<IAdminFinancialReportService>();
                var adminId = (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<CoCoReTrade.Infrastructure.ApplicationUser>>()
                    .FindByEmailAsync("admin@cocoretrade.vn"))!.Id;
                var report = await reports.GetAsync(adminId, "custom", null, default, bucket.From, bucket.Through);
                Assert.Equal(bucket.Commission, report.CommissionAmount);
                Assert.Equal(bucket.Revenue, report.CommissionAmount + report.SellerNetAmount);
            }
            var revenue = await admin.GetStringAsync("/Admin/Reports/Revenue?period=custom&" + dates);
            Assert.Contains("id=\"report-revenue\"", revenue); Assert.Contains("id=\"report-commission\"", revenue);
            Assert.Contains("102,500", revenue); Assert.Contains("227,500", revenue);
            Assert.Contains("value=\"2026-11-16\"", revenue); Assert.Contains("value=\"2026-11-22\"", revenue);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin/Reports/Revenue?period=month&date=2026-11-22")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/Admin/Orders?from=2026-11-22")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/Admin/Orders?" + dates + "&basis=invalid")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/Admin/Products?from=2026-11-23&through=2026-11-24")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/Admin/Reports/Revenue?period=custom&from=bad")).StatusCode);
            using var inspector = await factory.LoginAsync("member");
            Assert.Equal(HttpStatusCode.OK, (await inspector.GetAsync("/Admin/Inspection?" + dates)).StatusCode);
            foreach (var route in new[] { "/Admin/Orders?" + dates, "/Admin/Products?status=Active&" + dates, "/Admin/Reports/Revenue?period=custom&" + dates })
                Assert.Equal(HttpStatusCode.Redirect, (await inspector.GetAsync(route)).StatusCode);
            var dashboard = await admin.GetStringAsync("/Admin/Dashboard");
            Assert.DoesNotContain("<p>Đơn phát sinh theo ngày đặt", dashboard);
            Assert.DoesNotContain("data-measure-previous", dashboard);
            Assert.DoesNotContain("<p class=\"dashboard-scope\">Theo ngày tạo", dashboard);
        }
        finally { await factory.DeleteDatabaseAsync(); }
    }
}
