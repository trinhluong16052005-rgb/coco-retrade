using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class NewsletterSubscriptions(AppDbContext db, TimeProvider time) : INewsletterSubscriptions
{
    public async Task SubscribeAsync(string email, CancellationToken ct)
    {
        var subscription = new NewsletterSubscription(email, time.GetUtcNow().UtcDateTime);
        db.NewsletterSubscriptions.Add(subscription);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Repeated and concurrent signups share the same email primary key.
        }
        finally
        {
            db.Entry(subscription).State = EntityState.Detached;
        }
    }
}
