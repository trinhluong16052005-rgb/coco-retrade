using CoCoReTrade.Domain;

namespace CoCoReTrade.Tests;

public sealed class UnscheduledPickupTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PickupWithoutAppointmentKeepsStoreAndDoesNotInventAnArrivalTime()
    {
        var order = Create(Active());

        Assert.Null(order.PickupAtUtc);
        Assert.Null(order.PickupDeadlineAtUtc);
        Assert.Equal("536/538 Núi Thành", order.PickupAddress);
        Assert.Equal(0, order.ShippingFee);
        Assert.Equal(300000, order.TotalAmount);
        Assert.Equal(OrderStatus.PendingProcessing, order.Status);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData(" ", 0)]
    [InlineData("536/538 Núi Thành", 20000)]
    public void PickupStillRequiresStoreAndZeroShippingFee(string? address, decimal fee)
        => Assert.Throws<ArgumentException>(() => Create(Active(), address: address, fee: fee));

    [Fact]
    public void SuppliedAppointmentStillRequiresFutureUtcTime()
    {
        var product = Active();
        Assert.Throws<ArgumentException>(() => Create(product, pickup: Now));
        Assert.Throws<ArgumentException>(() => Create(product, pickup: DateTime.SpecifyKind(Now.AddHours(1), DateTimeKind.Unspecified)));
        Assert.Equal(Now.AddHours(1), Create(product, pickup: Now.AddHours(1)).PickupAtUtc);
    }

    [Fact]
    public void UnscheduledOfferPickupKeepsDeadlineAndCanBeCancelledToRestock()
    {
        var product = Active();
        var offer = Accepted(product);
        var order = Create(product, offer);
        product.Reserve();
        order.MarkReady();

        Assert.Null(order.PickupAtUtc);
        Assert.Equal(offer.ExpiresAtUtc, order.PickupDeadlineAtUtc);
        Assert.Equal(250000, order.ItemAmount);
        Assert.Throws<InvalidOperationException>(() => order.ConfirmPayment("admin", null, offer.ExpiresAtUtc));
        order.Cancel(product, true, offer.ExpiresAtUtc);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(ProductStatus.Active, product.Status);
    }

    [Fact]
    public void NoAppointmentDoesNotAllowAnExpiredOffer()
    {
        var product = Active();
        var offer = Accepted(product);
        Assert.Throws<InvalidOperationException>(() => Create(product, offer, now: offer.ExpiresAtUtc));
        Assert.Throws<ArgumentException>(() => Create(product, offer, pickup: offer.ExpiresAtUtc));
    }

    private static Order Create(Product product, Offer? offer = null, string? address = "536/538 Núi Thành",
        decimal fee = 0, DateTime? pickup = null, DateTime? now = null) => Order.Create(product, offer,
        "buyer", Guid.NewGuid(), FulfillmentType.Pickup, "Người kiểm thử", "0905551234", null, null, null,
        pickup, address, fee, 35, now ?? Now);

    private static Offer Accepted(Product product)
    {
        var offer = Offer.Create(product, "buyer", 250000, 225000, 0, Now);
        offer.Accept("seller", product.Status, Now);
        return offer;
    }

    private static Product Active()
    {
        var product = Product.Submit("seller", "CG-2026-990110", "Áo kiểm thử", "Zara", "Áo", "M", 600000, 300000,
            95, true, [new ProductImage("a.jpg", "image/jpeg", 0), new ProductImage("b.jpg", "image/jpeg", 1), new ProductImage("c.jpg", "image/jpeg", 2)]);
        product.ReviewPreliminarily("admin", PreliminaryReviewStatus.Approved, null, Now);
        product.RecordInspection("inspector", true, product.Name, product.Brand, product.Category, product.Size,
            product.OriginalPrice, product.AskingPrice, 95, "checked", [], Now);
        product.Publish("CC-2026-990110", Now);
        return product;
    }
}
