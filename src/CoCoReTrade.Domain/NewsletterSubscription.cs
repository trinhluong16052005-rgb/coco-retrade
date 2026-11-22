using System.Net.Mail;

namespace CoCoReTrade.Domain;

public sealed class NewsletterSubscription
{
    public string Email { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }

    private NewsletterSubscription() { }

    public NewsletterSubscription(string email, DateTime createdAtUtc)
    {
        var normalized = email?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length is 0 or > 254 ||
            !MailAddress.TryCreate(normalized, out var address) || address.Address != normalized)
            throw new ArgumentException("Email không hợp lệ.");

        Email = normalized;
        CreatedAtUtc = createdAtUtc;
    }
}
