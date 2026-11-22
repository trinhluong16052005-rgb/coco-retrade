using System.Data;
using System.Text.RegularExpressions;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class WalletBankingService(AppDbContext db, IWalletService wallets, TimeProvider clock) : IWalletBankingService
{
    public async Task<IReadOnlyList<SavedBankAccountDto>> AccountsAsync(string userId, CancellationToken ct)
    {
        EnsureUser(userId);
        return await db.SavedBankAccounts.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => new SavedBankAccountDto(x.Id, x.BankName, x.AccountNumber, x.AccountHolder,
                x.IsDefault, Convert.ToBase64String(x.RowVersion))).ToListAsync(ct);
    }

    public Task<SavedBankAccountDto> SaveAccountAsync(string userId, BankAccountInput input, CancellationToken ct) =>
        InTransaction(userId, () => SaveCore(userId, input, ct), ct);

    public Task DeleteAccountAsync(string userId, Guid id, string rowVersion, CancellationToken ct) =>
        InTransaction(userId, async () =>
        {
            var account = await FindOwned(userId, id, ct);
            EnsureVersion(account, rowVersion);
            db.SavedBankAccounts.Remove(account);
            await db.SaveChangesAsync(ct);
            if (account.IsDefault)
            {
                var next = await db.SavedBankAccounts.Where(x => x.UserId == userId)
                    .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
                if (next is not null) { next.IsDefault = true; next.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; }
            }
            Audit(userId, id, "BankAccounts.Deleted");
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

    public Task SetDefaultAsync(string userId, Guid id, string rowVersion, CancellationToken ct) =>
        InTransaction(userId, async () =>
        {
            var account = await FindOwned(userId, id, ct);
            EnsureVersion(account, rowVersion);
            await ClearDefault(userId, id, ct);
            account.IsDefault = true; account.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            Audit(userId, id, "BankAccounts.DefaultSelected");
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

    public Task<WithdrawalDto> SubmitAsync(string userId, WalletWithdrawalInput input, CancellationToken ct) =>
        InTransaction(userId, async () =>
        {
            Validate(input.BankName, input.AccountNumber, input.AccountHolder);
            var bank = input.BankName.Trim(); var number = input.AccountNumber.Trim(); var holder = input.AccountHolder.Trim();
            // A network retry must not reserve funds twice or resave a deleted/edited account.
            var replay = await db.WithdrawalRequests.AsNoTracking().Where(x =>
                x.RequestKey == input.RequestKey && db.Wallets.Any(w => w.Id == x.WalletId && w.UserId == userId)).AnyAsync(ct);
            if (!replay)
            {
                if (input.SavedAccountId.HasValue)
                {
                    var account = await FindOwned(userId, input.SavedAccountId.Value, ct);
                    EnsureVersion(account, input.SavedAccountVersion);
                    if (account.BankName != bank || account.AccountNumber != number || account.AccountHolder != holder)
                        throw new InvalidOperationException("Tài khoản nhận tiền đã thay đổi. Hãy chọn lại tài khoản.");
                }
                else if (input.SaveAccount)
                    await SaveCore(userId, new(null, bank, number, holder), ct);
            }
            return await wallets.RequestWithdrawalAsync(userId, input.RequestKey, input.Amount, number, bank, ct, holder);
        }, ct);

    public async Task<WithdrawalPage> HistoryAsync(string userId, string? search, string? status, int page,
        int pageSize, CancellationToken ct)
    {
        EnsureUser(userId);
        if (!string.IsNullOrWhiteSpace(status) && status is not ("Pending" or "Approved" or "Rejected" or "Transferred"))
            throw new ArgumentException("Trạng thái yêu cầu không hợp lệ.");
        if (search?.Length > 100) throw new ArgumentException("Từ khóa tìm kiếm tối đa 100 ký tự.");
        pageSize = pageSize == 5 ? 5 : 10;
        var query = db.WithdrawalRequests.AsNoTracking().Where(x =>
            db.Wallets.Any(w => w.Id == x.WalletId && w.UserId == userId));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            decimal.TryParse(term, out var amount);
            var isRequestId = Guid.TryParse(term, out var requestId);
            query = query.Where(x => x.BankName!.Contains(term) || x.BankAccountNumber!.Contains(term) ||
                x.BankAccountHolder!.Contains(term) || x.TransferReference!.Contains(term) || x.Amount == amount ||
                isRequestId && x.Id == requestId);
        }
        var total = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new WithdrawalDto(x.Id, x.Amount, x.BankAccountNumber ?? x.BankAccountMasked,
                x.Status, x.CreatedAtUtc, x.DecidedAtUtc, x.DecisionReason, x.BankName,
                x.TransferredAtUtc, x.TransferReference, x.BankAccountHolder)).ToListAsync(ct);
        return new(items, page, pageSize, total);
    }

    private async Task<SavedBankAccountDto> SaveCore(string userId, BankAccountInput input, CancellationToken ct)
    {
        Validate(input.BankName, input.AccountNumber, input.AccountHolder);
        var bank = input.BankName.Trim(); var number = input.AccountNumber.Trim();
        var now = clock.GetUtcNow().UtcDateTime;
        SavedBankAccount account;
        if (input.Id.HasValue)
        {
            account = await FindOwned(userId, input.Id.Value, ct);
            EnsureVersion(account, input.RowVersion);
            if (await db.SavedBankAccounts.AnyAsync(x => x.UserId == userId && x.Id != account.Id &&
                x.BankName == bank && x.AccountNumber == number, ct))
                throw new ArgumentException("Tài khoản ngân hàng này đã có trong danh sách.");
        }
        else
        {
            account = await db.SavedBankAccounts.SingleOrDefaultAsync(x => x.UserId == userId &&
                x.BankName == bank && x.AccountNumber == number, ct) ?? new SavedBankAccount { UserId = userId, CreatedAtUtc = now };
            if (db.Entry(account).State != EntityState.Detached && account.AccountHolder != input.AccountHolder.Trim())
                throw new ArgumentException("Tài khoản này đã được lưu. Chọn Chỉnh sửa để cập nhật tên chủ tài khoản.");
            account.BankName = bank; account.AccountNumber = number; account.AccountHolder = input.AccountHolder.Trim();
            if (db.Entry(account).State == EntityState.Detached) db.SavedBankAccounts.Add(account);
        }
        var makeDefault = input.IsDefault || account.IsDefault || !await db.SavedBankAccounts.AnyAsync(x => x.UserId == userId, ct);
        if (makeDefault) await ClearDefault(userId, account.Id, ct);
        account.BankName = bank; account.AccountNumber = number; account.AccountHolder = input.AccountHolder.Trim();
        account.IsDefault = makeDefault; account.UpdatedAtUtc = now;
        Audit(userId, account.Id, input.Id.HasValue ? "BankAccounts.Updated" : "BankAccounts.Saved");
        await db.SaveChangesAsync(ct);
        return new(account.Id, account.BankName, account.AccountNumber, account.AccountHolder, account.IsDefault,
            Convert.ToBase64String(account.RowVersion));
    }

    private async Task ClearDefault(string userId, Guid keep, CancellationToken ct)
    {
        var defaults = await db.SavedBankAccounts.Where(x => x.UserId == userId && x.Id != keep && x.IsDefault).ToListAsync(ct);
        foreach (var account in defaults) { account.IsDefault = false; account.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; }
        // Clear the filtered unique index before setting another default.
        if (defaults.Count > 0) await db.SaveChangesAsync(ct);
    }

    private async Task<T> InTransaction<T>(string userId, Func<Task<T>> work, CancellationToken ct)
    {
        EnsureUser(userId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // Serialize account/default changes and withdrawal submissions for this owner only.
            var owner = await db.Database.SqlQuery<string>($"SELECT [Id] AS [Value] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}")
                .SingleOrDefaultAsync(ct);
            if (owner is null) throw new UnauthorizedAccessException("Hãy đăng nhập lại.");
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException || ex.GetBaseException() is SqlException { Number: 1205 or 2601 or 2627 })
                throw new InvalidOperationException("Thông tin ví vừa thay đổi. Hãy tải lại trang và thử lại.", ex);
            throw;
        }
    }

    private async Task<SavedBankAccount> FindOwned(string userId, Guid id, CancellationToken ct) =>
        await db.SavedBankAccounts.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
        ?? throw new KeyNotFoundException("Không tìm thấy tài khoản ngân hàng.");
    private static void EnsureVersion(SavedBankAccount account, string? expected)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(expected ?? ""); }
        catch (FormatException) { throw new ArgumentException("Phiên bản tài khoản không hợp lệ."); }
        if (bytes.Length == 0 || !bytes.SequenceEqual(account.RowVersion))
            throw new InvalidOperationException("Tài khoản ngân hàng đã thay đổi. Hãy tải lại danh sách.");
    }
    private static void EnsureUser(string userId)
    { if (string.IsNullOrWhiteSpace(userId)) throw new UnauthorizedAccessException("Hãy đăng nhập."); }
    private static void Validate(string bank, string number, string holder)
    {
        if (string.IsNullOrWhiteSpace(bank) || bank.Trim().Length is < 2 or > 100 ||
            !Regex.IsMatch(number?.Trim() ?? "", @"^[0-9]{6,30}$") ||
            string.IsNullOrWhiteSpace(holder) || holder.Trim().Length is < 2 or > 150 ||
            bank.Any(char.IsControl) || holder.Any(char.IsControl))
            throw new ArgumentException("Nhập tên ngân hàng, số tài khoản từ 6 đến 30 chữ số và tên chủ tài khoản.");
    }
    private void Audit(string userId, Guid id, string action) => db.AuditLogs.Add(new AuditLog
    {
        Id = Guid.NewGuid(), ActorId = userId, Action = action, EntityType = "SavedBankAccount",
        EntityId = id.ToString(), Details = "Owner updated a saved bank account; withdrawal snapshots retained",
        CreatedAtUtc = clock.GetUtcNow().UtcDateTime
    });
}
