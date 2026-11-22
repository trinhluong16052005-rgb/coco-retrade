namespace CoCoReTrade.Domain;

public sealed class CheckoutVoucher
{
    public string Code { get; private set; } = "";
    public int Percent { get; private set; }
    public decimal MinimumAmount { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public bool Enabled { get; private set; }
    private CheckoutVoucher() { }
    public CheckoutVoucher(string code, int percent, decimal minimumAmount, DateTime endsAtUtc, bool enabled = true)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 30 || percent is < 1 or > 35 ||
            minimumAmount < 0 || minimumAmount != decimal.Truncate(minimumAmount) || endsAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Thông tin mã giảm giá không hợp lệ.");
        Code = code.Trim().ToUpperInvariant(); Percent = percent; MinimumAmount = minimumAmount;
        EndsAtUtc = endsAtUtc; Enabled = enabled;
    }
    public decimal Discount(decimal merchandiseAmount, DateTime nowUtc)
    {
        if (Percent is < 1 or > 35) throw new InvalidOperationException("Mã giảm giá không được vượt quá 35% tiền hàng.");
        if (!Enabled || nowUtc >= EndsAtUtc) throw new InvalidOperationException("Mã giảm giá đã hết hạn hoặc chưa được kích hoạt.");
        if (merchandiseAmount < MinimumAmount)
            throw new InvalidOperationException($"Mã {Code} áp dụng cho tiền hàng từ {MinimumAmount:N0} VNĐ.");
        return decimal.Floor(merchandiseAmount * Percent / 100m);
    }
}

public sealed class CheckoutBatch
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string BuyerId { get; private set; } = "";
    public Guid IdempotencyKey { get; private set; }
    public string RequestHash { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }
    private CheckoutBatch() { }
    public CheckoutBatch(string buyerId, Guid key, string requestHash, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(buyerId) || key == Guid.Empty || requestHash.Length != 64)
            throw new ArgumentException("Thiếu mã xác nhận giỏ hàng.");
        BuyerId = buyerId; IdempotencyKey = key; RequestHash = requestHash; CreatedAtUtc = nowUtc;
    }
}

public static class CheckoutAllocation
{
    public static decimal[] WithinCommission(decimal amount, IReadOnlyList<decimal> prices, IReadOnlyList<decimal> capacities)
    {
        if (prices.Count != capacities.Count || capacities.Any(c => c < 0 || c != decimal.Truncate(c)) || amount > capacities.Sum())
            throw new ArgumentException("Giảm giá không được vượt quá hoa hồng CoCo.");
        var shares = Proportional(amount, prices);
        for (var i = 0; i < shares.Length; i++) shares[i] = Math.Min(shares[i], capacities[i]);
        var remaining = amount - shares.Sum();
        var total = prices.Sum();
        foreach (var i in Enumerable.Range(0, shares.Length).OrderByDescending(i => amount * prices[i] / total - shares[i]).ThenBy(i => i))
        {
            var extra = Math.Min(remaining, capacities[i] - shares[i]);
            shares[i] += extra; remaining -= extra;
        }
        return shares;
    }

    // Largest remainder method: whole VND shares always sum to the invoice amount.
    public static decimal[] Proportional(decimal amount, IReadOnlyList<decimal> weights)
    {
        if (amount < 0 || amount != decimal.Truncate(amount) || weights.Count == 0 || weights.Any(x => x <= 0))
            throw new ArgumentException("Số tiền phân bổ không hợp lệ.");
        var total = weights.Sum();
        var exact = weights.Select(x => amount * x / total).ToArray();
        var result = exact.Select(decimal.Floor).ToArray();
        var remaining = (int)(amount - result.Sum());
        foreach (var index in Enumerable.Range(0, weights.Count).OrderByDescending(i => exact[i] - result[i]).ThenBy(i => i).Take(remaining))
            result[index]++;
        return result;
    }
}
