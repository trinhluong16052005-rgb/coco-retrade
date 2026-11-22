using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Web.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoCoReTrade.Tests;

public sealed class CollectionRemovalTests
{
    [Fact]
    public async Task ClosetAllowsUninspectedItemsIncludingPreliminaryApprovalAndHidesRemovedItems()
    {
        var pending = NewProduct();
        var prelim = NewProduct();
        prelim.ReviewPreliminarily("inspector", PreliminaryReviewStatus.Approved, null, DateTime.UtcNow);
        var repository = new Products([pending, prelim]);
        var service = ProductService(repository);
        Assert.All(await service.ClosetAsync("seller", default), card => Assert.True(card.CanRemoveFromCloset));
        var removed = await service.RemoveFromClosetAsync("seller", [pending.Id, prelim.Id, pending.Id], default);
        Assert.Equal(2, removed.Count);
        Assert.All(repository.Rows, product => Assert.Equal(ProductStatus.Removed, product.Status));
        Assert.Empty(await service.ClosetAsync("seller", default));
        Assert.All(repository.Rows, product => Assert.Equal(3, product.Images.Count));
        Assert.Empty(repository.Notifications);
        Assert.Equal(1, repository.TransactionCount);
        Assert.Equal(2, (await service.RemoveFromClosetAsync("seller", removed, default)).Count);
    }

    [Theory]
    [InlineData("inspected")]
    [InlineData("failed-inspection")]
    [InlineData("active")]
    [InlineData("reserved")]
    [InlineData("sold")]
    [InlineData("previously-published")]
    public async Task ClosetLocksInspectedAndPublishedItemsAndRejectsWholeMixedBatch(string state)
    {
        var pending = NewProduct();
        var locked = Inspected(state != "failed-inspection");
        if (state is "active" or "reserved" or "sold" or "previously-published")
            locked.Publish("CC-2026-900001", DateTime.UtcNow);
        if (state is "reserved" or "sold") locked.Reserve();
        if (state == "sold") locked.MarkSold();
        if (state == "failed-inspection") locked.Reject("Không đạt kiểm định hiện vật.");
        if (state == "previously-published")
            typeof(Product).GetProperty(nameof(Product.Status))!.SetValue(locked, ProductStatus.PendingApproval);
        var repository = new Products([pending, locked]);
        var service = ProductService(repository);
        Assert.False(locked.CanSellerRemove);
        Assert.False((await service.ClosetAsync("seller", default)).Single(x => x.Id == locked.Id).CanRemoveFromCloset);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveFromClosetAsync("seller", [pending.Id, locked.Id], default));
        Assert.Equal(ProductStatus.PendingApproval, pending.Status);
        Assert.Equal(0, repository.SaveCount);
        Assert.Throws<InvalidOperationException>(() => locked.RemoveBySeller("seller"));
    }

    [Fact]
    public async Task ClosetRejectsAnotherOwnerMissingIdsAndUnauthenticatedUserWithoutChangingAnything()
    {
        var own = NewProduct();
        var other = NewProduct("other");
        var repository = new Products([own, other]);
        var service = ProductService(repository);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RemoveFromClosetAsync("seller", [own.Id, other.Id], default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RemoveFromClosetAsync("seller", [own.Id, Guid.NewGuid()], default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RemoveFromClosetAsync("", [own.Id], default));
        Assert.Throws<UnauthorizedAccessException>(() => own.RemoveBySeller("other"));
        Assert.All(repository.Rows, product => Assert.Equal(ProductStatus.PendingApproval, product.Status));
        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrInvalidSelectionIsRejectedBeforeOpeningTransaction(bool emptyGuid)
    {
        var repository = new Products([]);
        await Assert.ThrowsAsync<ArgumentException>(() => ProductService(repository).RemoveFromClosetAsync("seller", emptyGuid ? [Guid.Empty] : [], default));
        Assert.Equal(0, repository.TransactionCount);
        var social = new Social();
        await Assert.ThrowsAsync<ArgumentException>(() => new SocialService(social, TimeProvider.System).RemoveFavoritesAsync("buyer", emptyGuid ? [Guid.Empty] : [], default));
        Assert.Equal(0, social.TransactionCount);
    }

    [Fact]
    public async Task FavoriteBulkRemovalIsIdempotentUserScopedAndDoesNotNeedAvailableProduct()
    {
        var first = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        var social = new Social();
        social.Favorites.UnionWith([("buyer", first), ("buyer", unavailable), ("other", first), ("other", unavailable)]);
        var service = new SocialService(social, TimeProvider.System);
        var requested = new[] { first, unavailable, first, Guid.NewGuid() };
        Assert.Equal(3, (await service.RemoveFavoritesAsync("buyer", requested, default)).Count);
        Assert.Equal(3, (await service.RemoveFavoritesAsync("buyer", requested, default)).Count);
        Assert.Equal(2, social.Favorites.Count);
        Assert.All(social.Favorites, favorite => Assert.Equal("other", favorite.User));
        Assert.Equal(0, social.ProductReads);
        Assert.Empty(social.Notifications);
        Assert.Equal(2, social.TransactionCount);
    }

    [Fact]
    public async Task IndividualFavoriteRemovalAlsoWorksAfterProductBecomesUnavailable()
    {
        var product = Guid.NewGuid();
        var social = new Social();
        social.Favorites.Add(("buyer", product));
        var service = new SocialService(social, TimeProvider.System);
        Assert.False(await service.SetFavoriteAsync("buyer", product, false, default));
        Assert.False(await service.SetFavoriteAsync("buyer", product, false, default));
        Assert.Empty(social.Favorites);
        Assert.Empty(social.Notifications);
        Assert.Equal(0, social.ProductReads);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetFavoriteAsync("buyer", product, true, default));
    }

    [Theory]
    [InlineData("/Social/RemoveFavorites")]
    [InlineData("/Listing/RemoveFromCloset")]
    public async Task HttpDeletionRequiresAuthenticationAndCsrfAndUpdatesOnlyAuthenticatedOwner(string path)
    {
        var product = NewProduct();
        var products = new Products([product]);
        var social = new Social();
        social.Favorites.UnionWith([("seller", product.Id), ("other", product.Id)]);
        await using var app = await HttpApp(products, social);
        using var client = app.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        using var anonymous = await client.PostAsync(path, new FormUrlEncodedContent([new("ids", product.Id.ToString())]));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "seller");
        using var noCsrf = await client.PostAsync(path, new FormUrlEncodedContent([new("ids", product.Id.ToString())]));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var tokenResponse = await client.GetAsync("/__antiforgery");
        var token = await tokenResponse.Content.ReadAsStringAsync();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", tokenResponse.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0])));
        using var invalid = await client.PostAsync(path, new FormUrlEncodedContent([new("ids", "bad-id"), new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var valid = await client.PostAsync(path, new FormUrlEncodedContent([new("ids", product.Id.ToString()), new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Contains(product.Id.ToString(), await valid.Content.ReadAsStringAsync());
        if (path.Contains("Closet")) Assert.Equal(ProductStatus.Removed, product.Status);
        else { Assert.DoesNotContain(("seller", product.Id), social.Favorites); Assert.Contains(("other", product.Id), social.Favorites); }
    }

    private static async Task<WebApplication> HttpApp(Products products, Social social)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ApplicationName = typeof(ListingController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(ListingController).Assembly);
        builder.Services.AddSingleton<IProductService>(ProductService(products));
        builder.Services.AddSingleton<ISocialService>(new SocialService(social, TimeProvider.System));
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/__antiforgery", async context => await context.Response.WriteAsync(context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context).RequestToken!));
        app.MapControllerRoute("default", "{controller}/{action}");
        await app.StartAsync();
        return app;
    }

    public sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].ToString();
            return Task.FromResult(string.IsNullOrWhiteSpace(id) ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test")), "test")));
        }
    }

    internal static Product NewProduct(string seller = "seller") => Product.Submit(seller, "CD-001-2026", "Áo ký gửi", "CoCo", "Áo", "M", 500000, 250000, 95, true,
        [new ProductImage("a.jpg", "image/jpeg", 0), new ProductImage("b.jpg", "image/jpeg", 1), new ProductImage("c.jpg", "image/jpeg", 2)]);
    internal static Product Inspected(bool passed = true)
    {
        var p = NewProduct();
        p.ReviewPreliminarily("inspector", PreliminaryReviewStatus.Approved, null, DateTime.UtcNow);
        p.RecordInspection("inspector", passed, p.Name, p.Brand, p.Category, p.Size, p.OriginalPrice, p.AskingPrice, 95, "Kiểm định hiện vật", [], DateTime.UtcNow);
        return p;
    }
    private static ProductService ProductService(Products repository) => new(repository, new UnusedImages(), null!);
    private sealed class Products(List<Product> rows) : IProductRepository
    {
        public List<Product> Rows => rows;
        public List<MarketplaceNotification> Notifications { get; } = [];
        public int SaveCount { get; private set; }
        public int TransactionCount { get; private set; }
        public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) { TransactionCount++; return await work(); }
        public Task<Product?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(rows.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<Product>> OwnedAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Product>>(rows.Where(x => x.SellerId == userId && x.Status != ProductStatus.Removed).ToList());
        public Task SaveAsync(CancellationToken ct) { SaveCount++; return Task.CompletedTask; }
        public void AddNotification(MarketplaceNotification value) => Notifications.Add(value);
        public Task AddAsync(Product product, CancellationToken ct) => throw new NotSupportedException();
        public Task<Product?> FindByImageAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Product>> ActiveAsync(ProductFilter filter, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProductPage<Product>> ActivePageAsync(ProductFilter filter, int pageNumber, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Product>> PendingAsync(string? search, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> InspectorUserIdsAsync(CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class UnusedImages : IPrivateImageStore
    {
        public Task<(string Key, string ContentType)> SaveAsync(ImageUpload image, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Social : ISocialRepository
    {
        public HashSet<(string User, Guid Product)> Favorites { get; } = [];
        public List<MarketplaceNotification> Notifications { get; } = [];
        public int ProductReads { get; private set; }
        public int TransactionCount { get; private set; }
        public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) { TransactionCount++; return await work(); }
        public Task<IReadOnlyList<Guid>> FavoriteIdsAsync(string user, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>(Favorites.Where(x => x.User == user).Select(x => x.Product).ToList());
        public Task<bool> IsFavoriteAsync(string user, Guid product, CancellationToken ct) => Task.FromResult(Favorites.Contains((user, product)));
        public void RemoveFavorite(string user, Guid product) => Favorites.Remove((user, product));
        public void AddFavorite(Favorite favorite) => Favorites.Add((favorite.UserId, favorite.ProductId));
        public Task<Product?> ProductAsync(Guid id, CancellationToken ct) { ProductReads++; return Task.FromResult<Product?>(null); }
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        public void AddNotification(MarketplaceNotification value) => Notifications.Add(value);
        public Task<bool> SellerExistsAsync(string sellerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> SellerNameAsync(string sellerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsFollowingAsync(string followerId, string sellerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductCard>> SavedProductsAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FollowingIdsAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public void AddFollow(SellerFollow follow) => throw new NotSupportedException();
        public void RemoveFollow(string followerId, string sellerId) => throw new NotSupportedException();
    }
}
