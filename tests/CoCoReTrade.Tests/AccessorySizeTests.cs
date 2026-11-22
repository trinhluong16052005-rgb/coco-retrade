using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Web.Controllers;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace CoCoReTrade.Tests;

public sealed class AccessorySizeTests
{
    [Theory]
    [InlineData("Phụ kiện", "  20 x 30 cm  ", "20 x 30 cm")]
    [InlineData("Túi", "Dây dài 90 cm", "Dây dài 90 cm")]
    [InlineData("Túi xách", "Size 56", "Size 56")]
    [InlineData("Phụ kiện", "Free size", "Free size")]
    public void CustomAccessorySizesKeepSellerMeaningAndCase(string category, string entered, string expected)
        => Assert.Equal(expected, ProductSizing.Normalize(category, entered));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Dài 20\nRộng 30")]
    [InlineData("1234567890123456789012345678901")]
    public void AccessorySizeMustBeReadableAndFitSqlColumn(string? entered)
        => Assert.Throws<ArgumentException>(() => ProductSizing.Normalize("Phụ kiện", entered));

    [Fact]
    public void AccessorySizeAtSqlLimitIsAccepted()
        => Assert.Equal(new string('a', 30), ProductSizing.Normalize("Phụ kiện", new string('a', 30)));

    [Fact]
    public void SellerCanChangeAccessoryMeasurementsWithoutLosingOtherProductDetails()
    {
        var product = Product.Submit("seller", "CG-SIZE", "Túi đeo vai", "CoCo", "Phụ kiện", "20 x 30 cm",
            500000, 250000, 95, true, Images(), "Túi còn đẹp", "Đen");
        var imageIds = product.Images.Select(x => x.Id).ToArray();

        product.EditBySeller("seller", product.Name, product.Brand, product.Category, "Dài 32, rộng 24 cm",
            product.OriginalPrice, product.AskingPrice, product.DeclaredCondition, product.AllowOffers,
            imageIds, [], product.Description, product.Color);

        Assert.Equal("Dài 32, rộng 24 cm", product.Size);
        Assert.Equal("Túi còn đẹp", product.Description);
        Assert.Equal("Đen", product.Color);
        Assert.Equal(imageIds, product.Images.Select(x => x.Id));
    }

    [Fact]
    public void InspectionCanKeepActualAccessoryMeasurements()
    {
        var product = Product.Submit("seller", "CG-SIZE-INSPECT", "Túi đeo vai", "CoCo", "Phụ kiện", "20 x 30 cm",
            500000, 250000, 95, true, Images());
        product.ReviewPreliminarily("inspector", PreliminaryReviewStatus.Approved, null, DateTime.UtcNow);

        var inspection = product.RecordInspection("inspector", true, product.Name, product.Brand, product.Category,
            "Dài 31 cm", product.OriginalPrice, product.AskingPrice, 95, null, [], DateTime.UtcNow);

        Assert.Equal("Dài 31 cm", product.Size);
        Assert.Equal("Dài 31 cm", inspection.Size);
    }

    [Theory]
    [InlineData("Dài 32, rộng 24 cm", true)]
    [InlineData("123456789012345678901234567890", true)]
    [InlineData("1234567890123456789012345678901", false)]
    [InlineData("", false)]
    public void ListingAndInspectionValidateCustomSizeLength(string entered, bool valid)
    {
        foreach (var form in new object[] { new ListingForm { Size = entered }, new InspectionForm { Size = entered } })
        {
            var context = new ValidationContext(form) { MemberName = "Size" };
            Assert.Equal(valid, Validator.TryValidateProperty(entered, context, []));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsignmentControllerDoesNotReplaceCustomAccessorySizeWithFreeSize(bool editing)
    {
        var service = new CapturingProducts();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "seller")], "Test"));
        var controller = new ListingController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new MemoryTempData())
        };
        var form = new ListingForm { Category = "Phụ kiện", Size = "Dài 32, rộng 24 cm" };

        var result = editing
            ? await controller.Edit(Guid.NewGuid(), form, default)
            : await controller.Create(form, default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(form.Size, service.CapturedSize);
        Assert.NotEqual(ProductSizing.FreeSize, service.CapturedSize);
    }

    private static ProductImage[] Images() =>
        [new("one.jpg", "image/jpeg", 0), new("two.jpg", "image/jpeg", 1), new("three.jpg", "image/jpeg", 2)];

    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class CapturingProducts : IProductService
    {
        public Task<IReadOnlyList<Guid>> RemoveFromClosetAsync(string sellerId, IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public string? CapturedSize { get; private set; }
        public Task<ProductDetails> SubmitAsync(string sellerId, SubmitListing request, CancellationToken ct)
        {
            CapturedSize = request.Size;
            return Task.FromResult(Detail(request.Size));
        }
        public Task<ProductDetails> EditAsync(string sellerId, Guid id, EditListing request, CancellationToken ct)
        {
            CapturedSize = request.Size;
            return Task.FromResult(Detail(request.Size));
        }
        private static ProductDetails Detail(string size) => new(Guid.NewGuid(), "", "Túi đeo vai", "CoCo", "Phụ kiện", size,
            500000, 250000, 95, null, true, ProductStatus.PendingApproval, "seller", null, [], DateTime.UtcNow);
        public Task<IReadOnlyList<ProductCard>> ShopAsync(ProductFilter filter, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProductPage<ProductCard>> ShopPageAsync(ProductFilter filter, int pageNumber, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductCard>> ClosetAsync(string sellerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductCard>> PendingAsync(string? search, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProductDetails?> VisibleDetailsAsync(Guid id, string? viewerId, bool isAdmin, CancellationToken ct) => throw new NotSupportedException();
        public Task<(Stream Stream, string ContentType)?> VisibleImageAsync(Guid imageId, string? viewerId, bool canInspectPending,
            bool canManageAll, CancellationToken ct) => throw new NotSupportedException();
    }
}
