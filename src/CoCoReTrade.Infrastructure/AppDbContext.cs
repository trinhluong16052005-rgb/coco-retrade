using CoCoReTrade.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Infrastructure;

public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public int TrustScore { get; set; } = 100;
    public bool TrustSuspended { get; set; }
    public string? AvatarStorageKey { get; set; }
    public string? AvatarContentType { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferEvent> OfferEvents => Set<OfferEvent>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderCodeCounter> OrderCodeCounters => Set<OrderCodeCounter>();
    public DbSet<CheckoutVoucher> CheckoutVouchers => Set<CheckoutVoucher>();
    public DbSet<CheckoutBatch> CheckoutBatches => Set<CheckoutBatch>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<SellerFollow> SellerFollows => Set<SellerFollow>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<PlatformCommissionEntry> PlatformCommissionEntries => Set<PlatformCommissionEntry>();
    public DbSet<PlatformCommissionWithdrawal> PlatformCommissionWithdrawals => Set<PlatformCommissionWithdrawal>();
    public DbSet<AdminPayoutAccount> AdminPayoutAccounts => Set<AdminPayoutAccount>();
    public DbSet<WithdrawalRequest> WithdrawalRequests => Set<WithdrawalRequest>();
    public DbSet<SavedBankAccount> SavedBankAccounts => Set<SavedBankAccount>();
    public DbSet<MarketplaceNotification> MarketplaceNotifications => Set<MarketplaceNotification>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ShippingZoneMap> ShippingZones => Set<ShippingZoneMap>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AccountOtp> AccountOtps => Set<AccountOtp>();
    public DbSet<NewsletterSubscription> NewsletterSubscriptions => Set<NewsletterSubscription>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<SavedBankAccount>(e =>
        {
            e.ToTable("SavedBankAccounts"); e.HasKey(x => x.Id);
            e.Property(x => x.BankName).HasMaxLength(100).IsRequired();
            e.Property(x => x.AccountNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.AccountHolder).HasMaxLength(150).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.UserId, x.BankName, x.AccountNumber }).IsUnique();
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("[IsDefault] = 1");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WithdrawalRequest>().Property(x => x.BankAccountHolder).HasMaxLength(150);
        b.Entity<WithdrawalRequest>().HasIndex(x => new { x.WalletId, x.CreatedAtUtc });
        b.Entity<OrderCodeCounter>(e => { e.ToTable("OrderCodeCounters"); e.HasKey(x => x.Day); });
        b.Entity<NewsletterSubscription>(e =>
        {
            e.ToTable("NewsletterSubscriptions");
            e.HasKey(x => x.Email);
            e.Property(x => x.Email).HasMaxLength(254);
        });
        b.HasSequence<long>("ProductSkuSequence", "dbo").StartsAt(1).IncrementsBy(1);
        b.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.TrustScore).HasDefaultValue(100);
            e.Property(x => x.AvatarStorageKey).HasMaxLength(100);
            e.Property(x => x.AvatarContentType).HasMaxLength(40);
            e.HasIndex(x => x.PhoneNumber).IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");
        });
        b.Entity<Product>(e =>
        {
            e.ToTable("Products"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.Sku).IsUnique().HasFilter("[Sku] <> ''");
            e.HasIndex(x => x.ConsignmentCode).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.HasIndex(x => new { x.Status, x.ReminderSentAtUtc, x.PublishedAtUtc });
            e.Property(x => x.Sku).HasMaxLength(24).IsRequired();
            e.Property(x => x.ConsignmentCode).HasMaxLength(24).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Color).HasMaxLength(80);
            e.Property(x => x.Brand).HasMaxLength(100).IsRequired();
            e.Property(x => x.Category).HasMaxLength(80).IsRequired();
            e.Property(x => x.Size).HasMaxLength(30).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.RejectionReason).HasMaxLength(1000);
            e.Property(x => x.PreliminaryStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.PreliminaryReason).HasMaxLength(1000);
            e.Property(x => x.OriginalPrice).HasColumnType("decimal(18,0)");
            e.Property(x => x.AskingPrice).HasColumnType("decimal(18,0)");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Images).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Inspections).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ProductImage>(e =>
        {
            e.ToTable("ProductImages"); e.HasKey(x => x.Id);
            e.Property(x => x.StorageKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(40).IsRequired();
            e.HasOne<Inspection>().WithMany(x => x.Images).HasForeignKey(x => x.InspectionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Inspection>(e =>
        {
            e.ToTable("Inspections"); e.HasKey(x => x.Id);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Brand).HasMaxLength(100).IsRequired();
            e.Property(x => x.Category).HasMaxLength(80).IsRequired();
            e.Property(x => x.Size).HasMaxLength(30).IsRequired();
            e.Property(x => x.OriginalPrice).HasColumnType("decimal(18,0)");
            e.Property(x => x.AskingPrice).HasColumnType("decimal(18,0)");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.InspectorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Offer>(e =>
        {
            e.ToTable("Offers"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ProductId, x.BuyerId });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ProductSku).HasMaxLength(24).IsRequired();
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.AskingPrice).HasColumnType("decimal(18,0)");
            e.Property(x => x.CurrentPrice).HasColumnType("decimal(18,0)");
            e.Property(x => x.LastBuyerPrice).HasColumnType("decimal(18,0)");
            e.Property(x => x.LastSellerPrice).HasColumnType("decimal(18,0)");
            e.Property(x => x.AcceptedPrice).HasColumnType("decimal(18,0)");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OfferEvent>(e =>
        {
            e.ToTable("OfferEvents"); e.HasKey(x => x.Id); e.Property(x => x.Price).HasColumnType("decimal(18,0)");
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => new { x.OfferId, x.Action });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Order>(e =>
        {
            e.ToTable("Orders"); e.HasKey(x => x.Id);
            e.Property(x => x.OrderCode).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.OrderCode).IsUnique();
            e.HasIndex(x => x.ProductId).IsUnique().HasFilter("[Status] <> 'Cancelled'");
            e.HasIndex(x => new { x.BuyerId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => x.OfferId).IsUnique().HasFilter("[OfferId] IS NOT NULL");
            e.HasIndex(x => x.PickupDeadlineAtUtc).HasFilter("[PickupDeadlineAtUtc] IS NOT NULL");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Fulfillment).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ProductSku).HasMaxLength(24).IsRequired();
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ProductSize).HasMaxLength(30).IsRequired();
            e.Property(x => x.RecipientName).HasMaxLength(150).IsRequired();
            e.Property(x => x.RecipientPhone).HasMaxLength(15).IsRequired();
            e.Property(x => x.Province).HasMaxLength(120);
            e.Property(x => x.Ward).HasMaxLength(120);
            e.Property(x => x.StreetAddress).HasMaxLength(300);
            e.Property(x => x.PreferredDeliveryWindow).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.DeliveryNote).HasMaxLength(500);
            e.Property(x => x.PickupAddress).HasMaxLength(300);
            e.Property(x => x.PaymentReference).HasMaxLength(100);
            e.Property(x => x.TrackingCode).HasMaxLength(100);
            e.Property(x => x.ItemAmount).HasColumnType("decimal(18,0)");
            e.Property(x => x.ShippingFee).HasColumnType("decimal(18,0)");
            e.Property(x => x.TotalAmount).HasColumnType("decimal(18,0)");
            e.Property(x => x.CommissionAmount).HasColumnType("decimal(18,0)");
            e.Property(x => x.DiscountAmount).HasColumnType("decimal(18,0)").HasDefaultValue(0m);
            e.Property(x => x.DiscountCode).HasMaxLength(30);
            e.HasOne<CheckoutBatch>().WithMany().HasForeignKey(x => x.CheckoutBatchId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Offer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PaymentConfirmedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CheckoutVoucher>(e =>
        {
            e.ToTable("CheckoutVouchers"); e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.MinimumAmount).HasColumnType("decimal(18,0)");
            e.HasData(new CheckoutVoucher("GIAM5", 5, 300_000, new DateTime(2026, 12, 30, 17, 0, 0, DateTimeKind.Utc)),
                new CheckoutVoucher("GIAM10", 10, 500_000, new DateTime(2026, 12, 30, 17, 0, 0, DateTimeKind.Utc)));
        });
        b.Entity<CheckoutBatch>(e =>
        {
            e.ToTable("CheckoutBatches"); e.HasKey(x => x.Id);
            e.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.BuyerId, x.IdempotencyKey }).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Favorite>(e =>
        {
            e.ToTable("Favorites"); e.HasKey(x => new { x.UserId, x.ProductId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CartItem>(e =>
        {
            e.ToTable("CartItems"); e.HasKey(x => new { x.UserId, x.ProductId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Conversation>(e =>
        {
            e.ToTable("Conversations"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProductId, x.BuyerId }).IsUnique();
            e.HasIndex(x => new { x.BuyerId, x.LastMessageAtUtc });
            e.HasIndex(x => new { x.SellerId, x.LastMessageAtUtc });
            e.Property(x => x.ProductSku).HasMaxLength(24).IsRequired();
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ConversationMessage>(e =>
        {
            e.ToTable("ConversationMessages"); e.HasKey(x => x.Id);
            e.Property(x => x.Body).HasMaxLength(1000).IsRequired();
            e.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SupportTicket>(e =>
        {
            e.ToTable("SupportTickets"); e.HasKey(x => x.Id);
            e.Property(x => x.Subject).HasMaxLength(150).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.UserId, x.LastActivityAtUtc });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SupportMessage>(e =>
        {
            e.ToTable("SupportMessages"); e.HasKey(x => x.Id);
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => new { x.TicketId, x.CreatedAtUtc });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SellerFollow>(e =>
        {
            e.ToTable("SellerFollows"); e.HasKey(x => new { x.FollowerId, x.SellerId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.FollowerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Wallet>(e =>
        {
            e.ToTable("Wallets"); e.HasKey(x => x.Id); e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Balance).HasColumnType("decimal(18,0)"); e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WalletTransaction>(e =>
        {
            e.ToTable("WalletTransactions"); e.HasKey(x => x.Id); e.Property(x => x.Amount).HasColumnType("decimal(18,0)");
            e.HasIndex(x => x.OrderId).IsUnique().HasFilter("[OrderId] IS NOT NULL AND [Type] = 'SellerPayout'");
            e.HasIndex(x => new { x.WithdrawalRequestId, x.Type }).IsUnique()
                .HasFilter("[WithdrawalRequestId] IS NOT NULL");
            e.HasOne<Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WithdrawalRequest>().WithMany().HasForeignKey(x => x.WithdrawalRequestId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PlatformCommissionEntry>(e =>
        {
            e.ToTable("PlatformCommissionEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(18,0)");
            e.HasIndex(x => x.OrderId).IsUnique();
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PlatformCommissionWithdrawal>(e =>
        {
            e.ToTable("PlatformCommissionWithdrawals"); e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(18,0)");
            e.Property(x => x.BankAccountNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.BankName).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.AdminId, x.RequestKey }).IsUnique();
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AdminPayoutAccount>(e =>
        {
            e.ToTable("AdminPayoutAccounts"); e.HasKey(x => x.AdminId);
            e.Property(x => x.BankAccountNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.BankName).HasMaxLength(100).IsRequired();
            e.HasOne<ApplicationUser>().WithOne().HasForeignKey<AdminPayoutAccount>(x => x.AdminId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WithdrawalRequest>(e =>
        {
            e.ToTable("WithdrawalRequests"); e.HasKey(x => x.Id); e.Property(x => x.Amount).HasColumnType("decimal(18,0)");
            e.HasIndex(x => new { x.WalletId, x.RequestKey }).IsUnique().HasFilter("[RequestKey] <> '00000000-0000-0000-0000-000000000000'");
            e.Property(x => x.BankAccountMasked).HasMaxLength(60).IsRequired();
            e.Property(x => x.BankAccountNumber).HasMaxLength(30);
            e.Property(x => x.BankName).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.DecisionReason).HasMaxLength(1000);
            e.Property(x => x.TransferReference).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DecidedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TransferredById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MarketplaceNotification>(e =>
        {
            e.ToTable("Notifications"); e.HasKey(x => x.Id);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Offer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.SupportTicketId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<WithdrawalRequest>().WithMany().HasForeignKey(x => x.WithdrawalRequestId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        });
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages"); e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasMaxLength(20).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(300);
            e.HasIndex(x => new { x.NotificationId, x.Channel }).IsUnique();
            e.HasIndex(x => new { x.SentAtUtc, x.NextAttemptAtUtc, x.LeaseUntilUtc });
            e.HasOne<MarketplaceNotification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ShippingZoneMap>(e =>
        {
            e.ToTable("ShippingZones"); e.HasKey(x => x.Id);
            e.Property(x => x.Province).HasMaxLength(120).IsRequired();
            e.Property(x => x.Ward).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.Province, x.Ward }).IsUnique();
        });
        b.Entity<Address>(e =>
        {
            e.ToTable("Addresses"); e.HasKey(x => x.Id);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs"); e.HasKey(x => x.Id);
            e.Property(x => x.Details).HasMaxLength(2000).IsRequired();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccountOtp>(e =>
        {
            e.ToTable("AccountOtps"); e.HasKey(x => x.Id);
            e.Property(x => x.Purpose).HasMaxLength(20).IsRequired();
            e.Property(x => x.CodeHash).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Purpose, x.CreatedAtUtc });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override int SaveChanges() => SaveChanges(true);
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var added = ChangeTracker.Entries<Order>().Where(e => e.State == EntityState.Added && e.Entity.OrderCode.Length == 0)
            .OrderBy(e => e.Entity.CreatedAtUtc).ThenBy(e => e.Entity.Id).ToArray();
        await using var transaction = added.Length > 0 && Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            foreach (var entry in added) await SqlOrderCodeSequence.AssignAsync(this, entry.Entity, cancellationToken);
            await NotificationOrderCodes.ApplyAsync(this, ChangeTracker.Entries<MarketplaceNotification>()
                .Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToArray(), cancellationToken);
            EnqueueNotificationDeliveries();
            var count = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return count;
        }
        catch
        {
            foreach (var entry in added) entry.Property(o => o.OrderCode).CurrentValue = "";
            throw;
        }
    }

    private void EnqueueNotificationDeliveries()
    {
        var tracked = ChangeTracker.Entries<OutboxMessage>()
            .Select(x => (x.Entity.NotificationId, x.Entity.Channel)).ToHashSet();
        var notifications = ChangeTracker.Entries<MarketplaceNotification>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .ToArray();
        var deliveries = new List<OutboxMessage>();
        foreach (var notification in notifications)
        {
            foreach (var channel in new[] { "email", "webpush" })
            {
                if (!tracked.Add((notification.Id, channel))) continue;
                deliveries.Add(new OutboxMessage
                {
                    NotificationId = notification.Id, Channel = channel,
                    CreatedAtUtc = notification.CreatedAtUtc,
                    NextAttemptAtUtc = notification.CreatedAtUtc
                });
            }
        }
        OutboxMessages.AddRange(deliveries);
    }
}
