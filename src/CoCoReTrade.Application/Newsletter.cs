namespace CoCoReTrade.Application;

public interface INewsletterSubscriptions
{
    Task SubscribeAsync(string email, CancellationToken ct);
}
