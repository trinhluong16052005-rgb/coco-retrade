using CoCoReTrade.Domain;

namespace CoCoReTrade.Tests;
public sealed class CheckoutRulesTests
{
    private static readonly DateTime Expiry = new(2026, 12, 30, 17, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData("GIAM5", 5, 300000, 300000, 15000)]
    [InlineData("GIAM10", 10, 500000, 500000, 50000)]
    [InlineData("GIAM5", 5, 300000, 399999, 19999)]
    public void VoucherMinimumAndWholeVndDiscount(string code, int percent, decimal minimum, decimal total, decimal expected)
    { Assert.Equal(expected, new CheckoutVoucher(code, percent, minimum, Expiry).Discount(total, Expiry.AddTicks(-1))); }
    [Fact]
    public void VoucherExpiresAtMidnightVietnamAndRejectsUnderMinimum()
    {
        var voucher = new CheckoutVoucher("GIAM10", 10, 500000, Expiry);
        Assert.Throws<InvalidOperationException>(() => voucher.Discount(499999, Expiry.AddHours(-1)));
        Assert.Equal(50000, voucher.Discount(500000, Expiry.AddTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => voucher.Discount(500000, Expiry));
        Assert.Throws<InvalidOperationException>(() => new CheckoutVoucher("GIAM5", 5, 300000, Expiry, false).Discount(400000, Expiry.AddDays(-1)));
    }
    [Fact]
    public void AllocatedShippingAndDiscountAlwaysSumExactly()
    {
        foreach (var amount in new decimal[] { 0, 1, 20000, 35000, 19999, 50000 })
        {
            var split = CheckoutAllocation.Proportional(amount, [149999, 199999, 299999]);
            Assert.Equal(amount, split.Sum()); Assert.All(split, x => Assert.Equal(decimal.Truncate(x), x));
        }
    }
    [Theory]
    [InlineData("Áo", "XS", "XS")]
    [InlineData("Quần", "XXL", "2XL")]
    [InlineData("Đầm/Váy", "3XL", "3XL")]
    [InlineData("Phụ kiện", "  20 x 30 cm  ", "20 x 30 cm")]
    [InlineData("Túi", "M", "M")]
    public void NormalizedSizes(string category, string? entered, string expected) => Assert.Equal(expected, ProductSizing.Normalize(category, entered));
    [Fact] public void ClothingRejectsSizesOutsideStandardRange() => Assert.Throws<ArgumentException>(() => ProductSizing.Normalize("Áo", "4XL"));
}
