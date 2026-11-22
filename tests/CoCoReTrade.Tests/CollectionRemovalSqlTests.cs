using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Tests;

// Opt-in; never loads production configuration. Creates and drops only its uniquely named test database.
public sealed class CollectionRemovalSqlTests
{
    [Fact]
    public async Task RealRepositoriesEnforceOwnershipInspectionLocksAtomicRollbackAndUserScopedFavorites()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        var database = "CoCoCollectionTests_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            $"Server=.\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True").Options;
        await using var setup = new AppDbContext(options);
        Assert.StartsWith("CoCoCollectionTests_", setup.Database.GetDbConnection().Database);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            Assert.Null(setup.Model.FindEntityType(typeof(Product))!.FindProperty(nameof(Product.CanSellerRemove)));
            setup.Users.AddRange(new[] { "seller", "other", "buyer", "inspector" }.Select(id => new ApplicationUser
            { Id = id, FullName = id, UserName = id + "@example.test", NormalizedUserName = id.ToUpperInvariant() + "@EXAMPLE.TEST" }));
            var first = NewProduct(); var second = NewProduct(); var foreign = NewProduct("other"); var inspected = NewProduct();
            second.ReviewPreliminarily("inspector", PreliminaryReviewStatus.Approved, null, DateTime.UtcNow);
            inspected.ReviewPreliminarily("inspector", PreliminaryReviewStatus.Approved, null, DateTime.UtcNow);
            inspected.RecordInspection("inspector", true, inspected.Name, inspected.Brand, inspected.Category, inspected.Size,
                inspected.OriginalPrice, inspected.AskingPrice, 95, "Kiểm định", [], DateTime.UtcNow);
            setup.Products.AddRange(first, second, foreign, inspected);
            setup.Favorites.AddRange(new Favorite { UserId = "buyer", ProductId = first.Id },
                new Favorite { UserId = "buyer", ProductId = inspected.Id }, new Favorite { UserId = "other", ProductId = first.Id });
            await setup.SaveChangesAsync();
            setup.ChangeTracker.Clear();

            await using (var db = new AppDbContext(options))
            {
                var service = Service(db);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RemoveFromClosetAsync("seller", [first.Id, foreign.Id], default));
            }
            await using (var db = new AppDbContext(options))
                await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).RemoveFromClosetAsync("seller", [first.Id, inspected.Id], default));
            Assert.Equal(4, await setup.Products.CountAsync(x => x.Status != ProductStatus.Removed));

            // A previously loaded row changes before deletion; SQL rowversion must roll back the whole batch.
            await using (var stale = new AppDbContext(options))
            {
                var repository = new ProductRepository(stale, new ShippingPolicy());
                await repository.FindAsync(first.Id, default);
                await using (var changed = new AppDbContext(options))
                {
                    var product = (await new ProductRepository(changed, new ShippingPolicy()).FindAsync(first.Id, default))!;
                    product.ReviewPreliminarily("inspector", PreliminaryReviewStatus.ChangesRequested, "Cần thêm ảnh rõ nét.", DateTime.UtcNow);
                    await changed.SaveChangesAsync();
                }
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new ProductService(repository, null!, null!)
                    .RemoveFromClosetAsync("seller", [first.Id, second.Id], default));
            }
            Assert.Equal(4, await setup.Products.CountAsync(x => x.Status != ProductStatus.Removed));
            await using (var db = new AppDbContext(options))
                Assert.Equal(2, (await Service(db).RemoveFromClosetAsync("seller", [first.Id, second.Id], default)).Count);
            Assert.Equal(2, await setup.Products.CountAsync(x => x.Status == ProductStatus.Removed));
            Assert.Equal(12, await setup.ProductImages.CountAsync());
            Assert.Single(await setup.Inspections.ToListAsync());
            await using (var db = new AppDbContext(options))
            {
                var cards = await Service(db).ClosetAsync("seller", default);
                Assert.False(Assert.Single(cards).CanRemoveFromCloset);
                var social = new SocialService(new SocialRepository(db), TimeProvider.System);
                await social.RemoveFavoritesAsync("buyer", [first.Id, inspected.Id, Guid.NewGuid()], default);
            }
            await using (var db = new AppDbContext(options))
            {
                var social = new SocialService(new SocialRepository(db), TimeProvider.System);
                await social.RemoveFavoritesAsync("buyer", [first.Id, inspected.Id], default);
                Assert.False(await social.SetFavoriteAsync("buyer", first.Id, false, default));
            }
            var remaining = Assert.Single(await setup.Favorites.AsNoTracking().ToListAsync());
            Assert.Equal("other", remaining.UserId);
            Assert.Equal(first.Id, remaining.ProductId);
            Assert.Equal(0, await setup.MarketplaceNotifications.CountAsync());
        }
        finally
        {
            Assert.StartsWith("CoCoCollectionTests_", setup.Database.GetDbConnection().Database);
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static ProductService Service(AppDbContext db) => new(new ProductRepository(db, new ShippingPolicy()), null!, null!);
    private static Product NewProduct(string seller = "seller") => Product.Submit(seller, "CD-" + Guid.NewGuid().ToString("N")[..10] + "-2026",
        "Áo ký gửi", "CoCo", "Áo", "M", 500000, 250000, 95, true,
        [new ProductImage("a.jpg", "image/jpeg", 0), new ProductImage("b.jpg", "image/jpeg", 1), new ProductImage("c.jpg", "image/jpeg", 2)]);
}
