using System.Data;
using CoCoReTrade.Application;
using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.CatalogAccounts;

public sealed class AccountRunner(AppDbContext db, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles)
{
    private static readonly string[] RequiredPermissions =
    [
        StaffPermissions.ReadPending, StaffPermissions.Inspect, StaffPermissions.Publish,
        StaffPermissions.Reject, StaffPermissions.ManageProducts, StaffPermissions.ManageOrders,
        StaffPermissions.ManageUsers, StaffPermissions.ManageWallets, StaffPermissions.ReadAudit
    ];

    public async Task RunAsync(AccountOptions options)
    {
        var plan = AccountPlan.Build();
        PrivateAccountOutput.Validate();
        await ValidateDatabaseAsync(plan);
        if (!options.Apply)
        {
            Console.WriteLine("DRY-RUN: sellers=8; buyers=1; administrators=1; total=10; created=0; files=0.");
            return;
        }

        using var output = new PrivateAccountOutput();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'CoCoReTrade.CatalogAccounts.20261117',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
            IF @result < 0 THROW 51000, 'Account provisioning is already running.', 1;
            """);
        await ValidateDatabaseAsync(plan);
        var credentials = new List<PrivateAccount>();
        var uniquePasswords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in plan)
        {
            var password = AccountPlan.NewPassword();
            while (!uniquePasswords.Add(password)) password = AccountPlan.NewPassword();
            var user = new ApplicationUser
            {
                UserName = item.Email, Email = item.Email, EmailConfirmed = true,
                FullName = item.FullName, MustChangePassword = true, LockoutEnabled = true
            };
            Ensure(await users.CreateAsync(user, password));
            Ensure(await users.AddToRolesAsync(user, item.Roles));
            if (!await users.CheckPasswordAsync(user, password) || string.IsNullOrWhiteSpace(user.PasswordHash) ||
                user.PasswordHash == password || !user.MustChangePassword)
                throw new AccountToolException("Kiểm tra mật khẩu băm hoặc đổi mật khẩu bắt buộc không đạt; hủy giao dịch.");
            var assigned = (await users.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToArray();
            if (!assigned.SequenceEqual(item.Roles.Order(StringComparer.Ordinal)))
                throw new AccountToolException("Kiểm tra phân quyền không đạt; hủy giao dịch.");
            credentials.Add(new PrivateAccount(item.Email, password, user.Id, item.Roles));
        }
        var ids = credentials.Select(account => account.UserId).ToArray();
        if (await db.Products.AnyAsync(product => ids.Contains(product.SellerId)) ||
            await db.Orders.AnyAsync(order => ids.Contains(order.BuyerId) || ids.Contains(order.SellerId)))
            throw new AccountToolException("Tài khoản mới phải chưa có sản phẩm hoặc đơn hàng; hủy giao dịch.");

        output.Write(credentials);
        output.Retain();
        await transaction.CommitAsync();
        Console.WriteLine("APPLIED: sellers=8; buyers=1; administrators=1; created=10; privateFiles=2; changedExistingUsers=0.");
    }

    private async Task ValidateDatabaseAsync(IReadOnlyList<PlannedAccount> plan)
    {
        var database = await db.Database.SqlQuery<string>($"SELECT DB_NAME() AS Value").SingleAsync();
        if (!string.Equals(database, "CoCoReTrade", StringComparison.Ordinal))
            throw new AccountToolException("Database thực tế không phải CoCoReTrade.");
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
            throw new AccountToolException("Database còn migration chưa áp dụng; công cụ không tự sửa schema.");
        _ = await db.Users.AsNoTracking().Select(user => user.MustChangePassword).Take(1).ToListAsync();
        var emailKeys = plan.Select(account => account.Email.ToUpperInvariant()).ToArray();
        var emails = plan.Select(account => account.Email).ToArray();
        if (await db.Users.AnyAsync(user => emailKeys.Contains(user.NormalizedEmail!) ||
            emailKeys.Contains(user.NormalizedUserName!) || emails.Contains(user.Email!) || emails.Contains(user.UserName!)))
            throw new AccountToolException("Có email hoặc tên đăng nhập mục tiêu đã tồn tại; từ chối tạo và không đặt lại mật khẩu.");
        foreach (var roleName in new[] { "User", "Inspector", "Admin" })
            if (!await roles.RoleExistsAsync(roleName))
                throw new AccountToolException("Thiếu role có sẵn; công cụ không tự sửa quyền hệ thống.");
        var admin = (await roles.FindByNameAsync("Admin"))!;
        var permissions = (await roles.GetClaimsAsync(admin)).Where(claim => claim.Type == StaffPermissions.ClaimType)
            .Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);
        if (RequiredPermissions.Any(permission => !permissions.Contains(permission)))
            throw new AccountToolException("Role Admin thiếu permission hiện có; không tự thêm hoặc nâng quyền role.");
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new AccountToolException("Identity từ chối tạo tài khoản hoặc phân quyền; hủy toàn bộ giao dịch.");
    }
}
