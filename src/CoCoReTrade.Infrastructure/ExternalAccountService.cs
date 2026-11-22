using CoCoReTrade.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class ExternalAccountService(AppDbContext db, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn) : IExternalAccountService
{
    public async Task<AccountActionResult> GoogleSignInAsync(GoogleAccount account, CancellationToken ct)
    {
        if (!Valid(account)) return new(false, "Google chưa xác minh email. Hãy chọn tài khoản có email đã xác minh.");
        var user = await users.FindByLoginAsync("Google", account.ProviderKey);
        if (user is null)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            if (await users.FindByEmailAsync(account.Email.Trim()) is not null)
                return new(false, "Email đã có tài khoản CoCo. Hãy đăng nhập bằng mật khẩu, rồi liên kết Google trong hồ sơ.");
            var email = account.Email.Trim().ToLowerInvariant();
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true,
                FullName = string.IsNullOrWhiteSpace(account.FullName) ? email.Split('@')[0] : account.FullName.Trim()[..Math.Min(account.FullName.Trim().Length, 150)] };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded) return new(false, "Không thể tạo tài khoản Google lúc này. Hãy thử lại.");
            var role = await users.AddToRoleAsync(user, "User");
            var linked = role.Succeeded ? await users.AddLoginAsync(user, new UserLoginInfo("Google", account.ProviderKey, "Google")) : role;
            if (!role.Succeeded || !linked.Succeeded) return new(false, "Không thể hoàn tất tài khoản Google. Hãy thử lại.");
            await transaction.CommitAsync(ct);
        }
        if (user.TrustSuspended || await users.IsLockedOutAsync(user))
            return new(false, "Tài khoản đang tạm khóa. Vui lòng liên hệ CoCo.");
        var result = await signIn.ExternalLoginSignInAsync("Google", account.ProviderKey, isPersistent: false, bypassTwoFactor: false);
        return result.Succeeded ? new(true, UserId: user.Id) : new(false, "Không thể đăng nhập tài khoản này bằng Google.");
    }

    public async Task<AccountActionResult> LinkGoogleAsync(string userId, GoogleAccount account, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || !Valid(account) || user.TrustSuspended || !user.EmailConfirmed || await users.IsLockedOutAsync(user))
            return new(false, "Không thể liên kết tài khoản này.");
        if (!string.Equals(user.Email, account.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            return new(false, "Hãy chọn tài khoản Google có cùng email với tài khoản CoCo.");
        // Called only after the web callback validates Identity XSRF and recent password confirmation.
        var linked = await users.FindByLoginAsync("Google", account.ProviderKey);
        if (linked is not null) return linked.Id == userId ? new(true) : new(false, "Google đã được liên kết với tài khoản khác.");
        var result = await users.AddLoginAsync(user, new UserLoginInfo("Google", account.ProviderKey, "Google"));
        if (!result.Succeeded) return new(false, "Không thể liên kết Google lúc này.");
        await signIn.RefreshSignInAsync(user);
        return new(true);
    }
    private static bool Valid(GoogleAccount account) => account.EmailVerified &&
        !string.IsNullOrWhiteSpace(account.ProviderKey) && account.ProviderKey.Length <= 450 &&
        !string.IsNullOrWhiteSpace(account.Email) && account.Email.Length <= 256 &&
        System.Net.Mail.MailAddress.TryCreate(account.Email, out _);
}
