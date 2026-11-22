using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoCoReTrade.Tests;

// Opt-in. Creates and drops only its own new CoCoCategoryTests_<GUID> database.
public sealed class CategoryMergeIntegrationTests
{
    [Fact]
    public async Task BagCategoryMigrationPreservesProductsAndImages()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        var database = "CoCoCategoryTests_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=.\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        await using var db = new AppDbContext(options);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261119103158_CheckoutBasketVouchers");
            db.Users.Add(new ApplicationUser
            {
                Id = "category-seller", UserName = "category-seller@example.test",
                NormalizedUserName = "CATEGORY-SELLER@EXAMPLE.TEST",
                Email = "category-seller@example.test", NormalizedEmail = "CATEGORY-SELLER@EXAMPLE.TEST",
                FullName = "Người bán kiểm thử", EmailConfirmed = true
            });
            var bag = CreateProduct("CG-CATEGORY-1", "Túi đeo vai", ProductCategories.Accessories, 250000);
            var archivedBag = CreateProduct("CG-CATEGORY-2", "Túi đeo chéo", ProductCategories.Accessories, 320000);
            archivedBag.RemoveFromCatalog();
            var shirt = CreateProduct("CG-CATEGORY-3", "Áo sơ mi", ProductCategories.Tops, 180000);
            db.Products.AddRange(bag, archivedBag, shirt);
            await db.SaveChangesAsync();

            // Represent records written before bag aliases were normalized to accessories.
            var oldBagCategory = "Túi";
            var oldLongBagCategory = " Túi xách ";
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [dbo].[Products] SET [Category] = {oldBagCategory} WHERE [Id] = {bag.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [dbo].[Products] SET [Category] = {oldLongBagCategory} WHERE [Id] = {archivedBag.Id}");
            db.ChangeTracker.Clear();
            var before = await db.Products.AsNoTracking().Include(x => x.Images).ToDictionaryAsync(x => x.Id);
            Assert.Equal(oldBagCategory, before[bag.Id].Category);
            Assert.Equal(oldLongBagCategory, before[archivedBag.Id].Category);

            await migrator.MigrateAsync();
            Assert.Contains("20261119160000_MergeBagCategoryIntoAccessories", await db.Database.GetAppliedMigrationsAsync());
            var after = await db.Products.AsNoTracking().Include(x => x.Images).ToDictionaryAsync(x => x.Id);
            Assert.Equal(before.Keys.OrderBy(x => x), after.Keys.OrderBy(x => x));
            Assert.Equal(ProductCategories.Accessories, after[bag.Id].Category);
            Assert.Equal(ProductCategories.Accessories, after[archivedBag.Id].Category);
            Assert.Equal(ProductCategories.Tops, after[shirt.Id].Category);
            Assert.Equal(9, await db.ProductImages.CountAsync());
            foreach (var original in before.Values)
            {
                var current = after[original.Id];
                Assert.Equal(original.Name, current.Name);
                Assert.Equal(original.SellerId, current.SellerId);
                Assert.Equal(original.Brand, current.Brand);
                Assert.Equal(original.ConsignmentCode, current.ConsignmentCode);
                Assert.Equal(original.Sku, current.Sku);
                Assert.Equal(original.Size, current.Size);
                Assert.Equal(original.OriginalPrice, current.OriginalPrice);
                Assert.Equal(original.AskingPrice, current.AskingPrice);
                Assert.Equal(original.Status, current.Status);
                Assert.Equal(original.DeclaredCondition, current.DeclaredCondition);
                Assert.Equal(original.Description, current.Description);
                Assert.Equal(original.Color, current.Color);
                Assert.Equal(original.CreatedAtUtc, current.CreatedAtUtc);
                Assert.Equal(
                    original.Images.OrderBy(x => x.Id).Select(x => (x.Id, x.ProductId, x.StorageKey, x.ContentType, x.SortOrder, x.InspectionId)),
                    current.Images.OrderBy(x => x.Id).Select(x => (x.Id, x.ProductId, x.StorageKey, x.ContentType, x.SortOrder, x.InspectionId)));
            }
            Assert.Single(await db.Users.ToListAsync());
        }
        finally
        {
            db.ChangeTracker.Clear();
            var connectedDatabase = db.Database.GetDbConnection().Database;
            Assert.StartsWith("CoCoCategoryTests_", connectedDatabase);
            Assert.Equal(database, connectedDatabase);
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static Product CreateProduct(string code, string name, string category, decimal price) =>
        Product.Submit("category-seller", code, name, "CoCo", category,
            ProductSizing.IsOneSize(category) ? ProductSizing.FreeSize : "M", price * 2, price, 90, true,
            [new ProductImage(code + "-1.jpg", "image/jpeg", 0), new ProductImage(code + "-2.jpg", "image/jpeg", 1),
             new ProductImage(code + "-3.jpg", "image/jpeg", 2)], "Mô tả kiểm thử giữ nguyên sau chuyển danh mục", "Đen");
}
