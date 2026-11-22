namespace CoCoReTrade.Application;

public sealed record GoogleAccount(string ProviderKey, string Email, string FullName, bool EmailVerified);
public sealed record AccountDeliveryStatus(bool EmailReady, bool GoogleReady);
public interface IExternalAccountService
{
    Task<AccountActionResult> GoogleSignInAsync(GoogleAccount account, CancellationToken ct);
    Task<AccountActionResult> LinkGoogleAsync(string userId, GoogleAccount account, CancellationToken ct);
}
