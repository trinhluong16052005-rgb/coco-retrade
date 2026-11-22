namespace CoCoReTrade.Application;

public sealed record SavedBankAccountDto(Guid Id, string BankName, string AccountNumber,
    string AccountHolder, bool IsDefault, string RowVersion);
public sealed record BankAccountInput(Guid? Id, string BankName, string AccountNumber,
    string AccountHolder, bool IsDefault = false, string? RowVersion = null);
public sealed record WalletWithdrawalInput(Guid RequestKey, decimal Amount, string BankName,
    string AccountNumber, string AccountHolder, Guid? SavedAccountId = null,
    string? SavedAccountVersion = null, bool SaveAccount = false);
public sealed record WithdrawalPage(IReadOnlyList<WithdrawalDto> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public interface IWalletBankingService
{
    Task<IReadOnlyList<SavedBankAccountDto>> AccountsAsync(string userId, CancellationToken ct);
    Task<SavedBankAccountDto> SaveAccountAsync(string userId, BankAccountInput input, CancellationToken ct);
    Task DeleteAccountAsync(string userId, Guid id, string rowVersion, CancellationToken ct);
    Task SetDefaultAsync(string userId, Guid id, string rowVersion, CancellationToken ct);
    Task<WithdrawalDto> SubmitAsync(string userId, WalletWithdrawalInput input, CancellationToken ct);
    Task<WithdrawalPage> HistoryAsync(string userId, string? search, string? status, int page,
        int pageSize, CancellationToken ct);
}
