namespace CoCoReTrade.Application;

public sealed record NotificationDestination(string Controller, string Action = "Index", string Area = "",
    Guid? Id = null, string? Fragment = null, Guid? WalletId = null, Guid? RequestId = null, string? Search = null);

public interface INotificationNavigation
{
    Task<NotificationDestination> ResolveAsync(string userId, NotificationDto notice,
        IReadOnlySet<string> grantedPermissions, CancellationToken ct);
}
