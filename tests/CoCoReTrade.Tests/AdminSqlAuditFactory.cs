using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CoCoReTrade.Application;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoCoReTrade.Tests;

internal sealed class AdminSqlAuditFactory(TimeProvider? clock = null) : WebApplicationFactory<Program>
{
    private readonly string database = "CoCoAdminSyncTests_" + Guid.NewGuid().ToString("N");
    private readonly string uploads = Path.Combine(Path.GetTempPath(), "CoCoAdminSyncTests_" + Guid.NewGuid().ToString("N"));
    public string Password { get; } = "CocoSync7" + Guid.NewGuid().ToString("N");
    public FailSaveInterceptor Failures { get; } = new();
    public Uri? ReviewAddress { get; set; }
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = $"Server=.\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.Sources.Clear();
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CoCo"] = connection, ["Preview:Enabled"] = "false", ["Demo:Enabled"] = "false",
                ["Admin:InitialPassword"] = Password, ["Storage:Root"] = uploads,
                ["Smtp:Host"] = "", ["Smtp:From"] = "", ["Smtp:User"] = "", ["Smtp:Password"] = "",
                ["Notifications:MarketplaceEmailEnabled"] = "false", ["Authentication:Google:ClientId"] = "", ["Authentication:Google:ClientSecret"] = ""
            });
        });
        builder.ConfigureServices(services =>
        {
            if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
            services.RemoveAll<AccountDeliveryStatus>(); services.AddSingleton(new AccountDeliveryStatus(false, false));
            services.PostConfigure<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>("Google", options =>
            { options.ClientId = "isolated-sync-test.apps.googleusercontent.com"; options.ClientSecret = "local-test-only"; });
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connection).AddInterceptors(Failures));
            services.RemoveAll<IProductService>(); services.AddScoped<IProductService, ProductService>();
            foreach (var worker in services.Where(x => new[] { "OfferExpirationWorker", "OrderExpirationWorker", "OutboxDispatcherWorker", "ListingReminderWorker" }.Contains(x.ImplementationType?.Name)).ToList())
                services.Remove(worker);
        });
    }
    public async Task InitializeUsersAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        Assert.StartsWith("CoCoAdminSyncTests_", db.Database.GetDbConnection().Database);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync("admin@cocoretrade.vn"))!;
        admin.MustChangePassword = false; Assert.True((await users.UpdateAsync(admin)).Succeeded);
        foreach (var id in new[] { "seller", "buyer", "member" })
        {
            var user = new ApplicationUser { Id = id, UserName = id + "@example.test", Email = id + "@example.test",
                EmailConfirmed = true, FullName = "Người kiểm thử " + id };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "User")).Succeeded);
        }
        db.Wallets.Add(new CoCoReTrade.Domain.Wallet { Id = Guid.NewGuid(), UserId = "seller", Balance = 500_000 });
        await db.SaveChangesAsync();
    }
    public async Task<HttpClient> LoginAsync(string name)
    {
        var client = ReviewAddress is null
            ? CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            : new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true }) { BaseAddress = ReviewAddress };
        var login = await client.GetStringAsync("/Account/Login");
        var email = name == "admin" ? "admin@cocoretrade.vn" : name + "@example.test";
        var response = await client.PostAsync("/Account/Login", Form(login, ("Identifier", email), ("Password", Password)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var csrf = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/v1/session/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!["token"]);
        return client;
    }
    public async Task<T> ReadAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.StartsWith("CoCoAdminSyncTests_", db.Database.GetDbConnection().Database);
        return await read(db);
    }
    public async Task DeleteDatabaseAsync()
    {
        Failures.AfterSaves = -1;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.StartsWith("CoCoAdminSyncTests_", db.Database.GetDbConnection().Database);
        await db.Database.EnsureDeletedAsync();
        var resolved = Path.GetFullPath(uploads);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("CoCoAdminSyncTests_"))
            throw new InvalidOperationException("Refusing unsafe test-upload cleanup.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
    public static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    public static FormUrlEncodedContent Form(string html, params (string Key, string Value)[] values) =>
        new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Append(new("__RequestVerificationToken", Token(html))));
    public static async Task<T> Success<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }
    internal sealed class FailSaveInterceptor : SaveChangesInterceptor
    {
        public int AfterSaves = -1;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (AfterSaves == 0) { AfterSaves = -1; throw new DbUpdateException("Sensitive SQL test failure must not be exposed"); }
            if (AfterSaves > 0) AfterSaves--;
            return ValueTask.FromResult(result);
        }
    }
}
