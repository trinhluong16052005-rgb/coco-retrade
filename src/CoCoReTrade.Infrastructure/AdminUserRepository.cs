using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class AdminUserRepository(AppDbContext db, UserManager<ApplicationUser> users) : IAdminUserRepository
{
    public async Task<IReadOnlyList<AdminUserRow>> SearchAsync(string? search, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Where(user =>
            !db.UserClaims.Any(claim => claim.UserId == user.Id && claim.ClaimType == AdminAccountDeletion.DeletedAtClaim));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.FullName.Contains(term) || x.Email!.Contains(term) || x.PhoneNumber!.Contains(term));
        }
        var found = await query.OrderBy(x => x.FullName).Take(100).ToListAsync(ct);
        var rows = new List<AdminUserRow>();
        foreach (var user in found) rows.Add(await ToRow(user));
        return rows;
    }
    public async Task<AdminUserRow?> FindAsync(string id, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id);
        return user is null || (await users.GetClaimsAsync(user)).Any(x => x.Type == AdminAccountDeletion.DeletedAtClaim)
            ? null : await ToRow(user);
    }
    public async Task DeleteManyAsync(IReadOnlyCollection<string> targetIds, string actorId, DateTime nowUtc, CancellationToken ct)
    {
        if (targetIds is null || targetIds.Count is < 1 or > 100 || targetIds.Any(string.IsNullOrWhiteSpace) ||
            targetIds.Distinct(StringComparer.Ordinal).Count() != targetIds.Count)
            throw new ArgumentException("Danh sách tài khoản không hợp lệ.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var targets = new List<ApplicationUser>();
        foreach (var targetId in targetIds.Order(StringComparer.Ordinal))
        {
            if (targetId == actorId || targetId == "system") throw new InvalidOperationException("Không thể xóa tài khoản đang đăng nhập hoặc tài khoản hệ thống.");
            var user = await users.FindByIdAsync(targetId) ?? throw new KeyNotFoundException("Một tài khoản không còn tồn tại.");
            if (await users.IsInRoleAsync(user, "Admin")) throw new InvalidOperationException("Không thể xóa tài khoản quản trị tại đây.");
            if ((await users.GetClaimsAsync(user)).Any(x => x.Type == AdminAccountDeletion.DeletedAtClaim))
                throw new InvalidOperationException("Một tài khoản đã được xóa. Hãy tải lại danh sách.");
            if (await db.Products.AnyAsync(x => x.SellerId == targetId &&
                    (x.Status == ProductStatus.PendingApproval || x.Status == ProductStatus.Active || x.Status == ProductStatus.Reserved), ct))
                throw new InvalidOperationException($"“{user.FullName}” còn sản phẩm đang ký gửi. Hãy xử lý sản phẩm trước khi xóa tài khoản.");
            if (await db.Orders.AnyAsync(x => (x.BuyerId == targetId || x.SellerId == targetId) &&
                    x.Status != OrderStatus.Completed && x.Status != OrderStatus.Cancelled, ct) ||
                await db.Offers.AnyAsync(x => (x.BuyerId == targetId || x.SellerId == targetId) &&
                    (x.Status == OfferStatus.PendingSeller || x.Status == OfferStatus.PendingBuyer || x.Status == OfferStatus.Accepted), ct))
                throw new InvalidOperationException($"“{user.FullName}” còn giao dịch chưa hoàn tất. Hãy xử lý giao dịch trước khi xóa tài khoản.");
            if (await db.Wallets.AnyAsync(x => x.UserId == targetId && x.Balance != 0, ct) ||
                await (from withdrawal in db.WithdrawalRequests join wallet in db.Wallets on withdrawal.WalletId equals wallet.Id
                       where wallet.UserId == targetId && (withdrawal.Status == "Pending" || withdrawal.Status == "Approved")
                       select withdrawal.Id).AnyAsync(ct))
                throw new InvalidOperationException($"“{user.FullName}” còn số dư hoặc yêu cầu rút tiền. Hãy quyết toán trước khi xóa tài khoản.");
            if (await HasHistoricalReferences(targetId, ct))
                throw new InvalidOperationException($"Không thể xóa vĩnh viễn “{user.FullName}” vì tài khoản còn lịch sử sản phẩm, giao dịch hoặc hoạt động cần lưu giữ. Bạn có thể khóa tài khoản để ngừng truy cập.");
            targets.Add(user);
        }
        try
        {
            // All targets pass preflight before any private data or account is removed.
            // These rows have no financial history; required historical links are never reassigned.
            db.SellerFollows.RemoveRange(await db.SellerFollows
                .Where(x => targetIds.Contains(x.FollowerId) || targetIds.Contains(x.SellerId)).ToListAsync(ct));
            db.Wallets.RemoveRange(await db.Wallets.Where(x => targetIds.Contains(x.UserId)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            foreach (var user in targets)
            {
                // Identity and the SQL cascades remove roles, claims, logins, tokens, OTPs,
                // saved addresses, cart, favorites and notification deliveries with the account.
                await Ensure(await users.DeleteAsync(user));
                db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorId = actorId, Action = "Users.Deleted",
                    EntityType = "User", EntityId = user.Id, Details = "Account permanently deleted from SQL; no historical business records removed",
                    CreatedAtUtc = nowUtc });
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqlException { Number: 547 })
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw new InvalidOperationException("Tài khoản vừa có dữ liệu liên quan nên chưa thể xóa vĩnh viễn. Chưa xóa tài khoản nào. Hãy tải lại danh sách.", ex);
        }
    }

    private async Task<bool> HasHistoricalReferences(string id, CancellationToken ct) =>
        await db.Products.AnyAsync(x => x.SellerId == id, ct) ||
        await db.Inspections.AnyAsync(x => x.InspectorId == id, ct) ||
        await db.Offers.AnyAsync(x => x.BuyerId == id || x.SellerId == id, ct) ||
        await db.OfferEvents.AnyAsync(x => x.ActorId == id, ct) ||
        await db.Orders.AnyAsync(x => x.BuyerId == id || x.SellerId == id || x.PaymentConfirmedById == id, ct) ||
        await db.CheckoutBatches.AnyAsync(x => x.BuyerId == id, ct) ||
        await db.Conversations.AnyAsync(x => x.BuyerId == id || x.SellerId == id, ct) ||
        await db.ConversationMessages.AnyAsync(x => x.SenderId == id, ct) ||
        await db.SupportTickets.AnyAsync(x => x.UserId == id, ct) ||
        await db.SupportMessages.AnyAsync(x => x.SenderId == id, ct) ||
        await db.AuditLogs.AnyAsync(x => x.ActorId == id, ct) ||
        await db.PlatformCommissionWithdrawals.AnyAsync(x => x.AdminId == id, ct) ||
        await db.AdminPayoutAccounts.AnyAsync(x => x.AdminId == id, ct) ||
        await db.WithdrawalRequests.AnyAsync(x => x.DecidedById == id || x.TransferredById == id, ct) ||
        await (from transaction in db.WalletTransactions join wallet in db.Wallets on transaction.WalletId equals wallet.Id
               where wallet.UserId == id select transaction.Id).AnyAsync(ct) ||
        await (from withdrawal in db.WithdrawalRequests join wallet in db.Wallets on withdrawal.WalletId equals wallet.Id
               where wallet.UserId == id select withdrawal.Id).AnyAsync(ct);
    public Task SetInspectorAsync(string targetId, bool enabled, string actorId, DateTime nowUtc, CancellationToken ct) =>
        Change(targetId, actorId, nowUtc, enabled ? "Users.InspectorGranted" : "Users.InspectorRevoked", async user =>
        {
            var has = await users.IsInRoleAsync(user, "Inspector");
            if (enabled && !has)
            {
                await Ensure(await users.AddToRoleAsync(user, "Inspector"));
                if (!await users.IsInRoleAsync(user, "User")) await Ensure(await users.AddToRoleAsync(user, "User"));
            }
            else if (!enabled && has)
            {
                await Ensure(await users.RemoveFromRoleAsync(user, "Inspector"));
                await RemovePublishClaim(user);
            }
        }, ct);
    public Task SetPublishGrantAsync(string targetId, bool enabled, string actorId, DateTime nowUtc, CancellationToken ct) =>
        Change(targetId, actorId, nowUtc, enabled ? "Users.PublishGranted" : "Users.PublishRevoked", async user =>
        {
            if (!await users.IsInRoleAsync(user, "Inspector")) throw new InvalidOperationException("Người dùng này không phải kiểm định viên.");
            var has = (await users.GetClaimsAsync(user)).Any(x => x.Type == StaffPermissions.ClaimType && x.Value == StaffPermissions.Publish);
            if (enabled && !has) await Ensure(await users.AddClaimAsync(user, new Claim(StaffPermissions.ClaimType, StaffPermissions.Publish)));
            if (!enabled && has) await RemovePublishClaim(user);
        }, ct);
    public Task SetLockedAsync(string targetId, bool locked, string actorId, DateTime nowUtc, CancellationToken ct) =>
        Change(targetId, actorId, nowUtc, locked ? "Users.Locked" : "Users.Unlocked", async user =>
        {
            await Ensure(await users.SetLockoutEnabledAsync(user, true));
            await Ensure(await users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null));
        }, ct);
    public Task SetTrustScoreAsync(string targetId, int score, int minimumScore, string reason,
        string actorId, DateTime nowUtc, CancellationToken ct) =>
        Change(targetId, actorId, nowUtc, "Users.TrustScoreChanged", async user =>
        {
            user.TrustScore = score;
            user.TrustSuspended = score < minimumScore;
            await Ensure(await users.UpdateAsync(user));
        }, ct, $"Điểm tín nhiệm={score}; tạm khóa={(score < minimumScore ? "có" : "không")}; lý do={reason}");

    private async Task Change(string targetId, string actorId, DateTime nowUtc, string action,
        Func<ApplicationUser, Task> work, CancellationToken ct, string? details = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await users.FindByIdAsync(targetId) ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");
        if ((await users.GetClaimsAsync(user)).Any(x => x.Type == AdminAccountDeletion.DeletedAtClaim))
            throw new InvalidOperationException("Tài khoản đã được xóa.");
        if (await users.IsInRoleAsync(user, "Admin")) throw new InvalidOperationException("Không thể thay đổi tài khoản quản trị tại đây.");
        await work(user);
        await Ensure(await users.UpdateSecurityStampAsync(user));
        db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorId = actorId, Action = action,
            EntityType = "User", EntityId = targetId, Details = details ?? action, CreatedAtUtc = nowUtc });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    private async Task RemovePublishClaim(ApplicationUser user)
    {
        foreach (var claim in (await users.GetClaimsAsync(user)).Where(x => x.Type == StaffPermissions.ClaimType && x.Value == StaffPermissions.Publish))
            await Ensure(await users.RemoveClaimAsync(user, claim));
    }
    private static Task Ensure(IdentityResult outcome)
    {
        if (!outcome.Succeeded) throw new InvalidOperationException(string.Join("; ", outcome.Errors.Select(x => x.Description)));
        return Task.CompletedTask;
    }
    private async Task<AdminUserRow> ToRow(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        var direct = (await users.GetClaimsAsync(user)).Any(x => x.Type == StaffPermissions.ClaimType && x.Value == StaffPermissions.Publish);
        return new AdminUserRow(user.Id, user.FullName, user.Email ?? "", user.PhoneNumber, user.EmailConfirmed,
            roles.Contains("Inspector"), roles.Contains("Admin"), direct, user.LockoutEnd > DateTimeOffset.UtcNow,
            user.TrustScore, user.TrustSuspended);
    }
}
