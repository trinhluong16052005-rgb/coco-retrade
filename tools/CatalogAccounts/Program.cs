using CoCoReTrade.CatalogAccounts;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("CatalogAccounts --database CoCoReTrade [--server .\\SQLEXPRESS] [--apply]");
    Console.WriteLine("Default: read-only dry-run. --apply creates 10 new accounts and 2 private files; never resets existing users.");
    return 0;
}
try
{
    if (!OperatingSystem.IsWindows()) throw new AccountToolException("Chỉ chạy trên Windows với Windows authentication.");
    var options = AccountOptions.Parse(args);
    var services = new ServiceCollection();
    services.AddLogging(logging => logging.ClearProviders());
    services.AddDbContext<AppDbContext>(builder => builder.UseSqlServer(options.ConnectionString()));
    services.AddIdentityCore<ApplicationUser>(identity =>
    {
        identity.Password.RequiredLength = 8;
        identity.Password.RequireDigit = true;
        identity.Password.RequireLowercase = true;
        identity.Password.RequireUppercase = false;
        identity.Password.RequireNonAlphanumeric = false;
        identity.User.RequireUniqueEmail = true;
    }).AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>();
    services.AddScoped<AccountRunner>();
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AccountRunner>().RunAsync(options);
    return 0;
}
catch (AccountToolException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch
{
    // Provider exceptions may include user data or connection values. Do not print them.
    Console.Error.WriteLine("Không hoàn tất. Kiểm tra SQL local, schema và quyền ghi tệp riêng. Nếu COMMIT đã bắt đầu, giữ tệp riêng để đối chiếu; không chạy lại hoặc xóa tệp trước khi kiểm tra.");
    return 1;
}
