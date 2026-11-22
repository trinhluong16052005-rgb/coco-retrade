using CoCoReTrade.Domain;
using CoCoReTrade.Infrastructure;
using CoCoReTrade.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Tests;

public sealed class ProductDescriptionMappingTests
{
    [Fact]
    public void OptionalDescriptionAndColorMapToBoundedNullableSqlColumns()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelInspectionOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var product = db.Model.FindEntityType(typeof(Product))!;
        var description = product.FindProperty(nameof(Product.Description))!;
        var color = product.FindProperty(nameof(Product.Color))!;
        Assert.True(description.IsNullable);
        Assert.True(color.IsNullable);
        Assert.Equal(2000, description.GetMaxLength());
        Assert.Equal(80, color.GetMaxLength());
    }

    [Fact]
    public void ListingFormPassesDescriptionsAndExplicitEmptyValuesWithoutChangingOldClients()
    {
        var form = new ListingForm { Description = "Thông tin quan sát", Color = "Trắng" };
        Assert.Equal("Thông tin quan sát", form.ToRequest().Description);
        Assert.Equal("Trắng", form.ToEditRequest().Color);
        form.Description = "";
        form.Color = "";
        Assert.Equal("", form.ToEditRequest().Description);
        Assert.Equal("", form.ToEditRequest().Color);
        var old = new ListingForm();
        Assert.Null(old.ToRequest().Description);
        Assert.Null(old.ToEditRequest().Color);
    }
}
