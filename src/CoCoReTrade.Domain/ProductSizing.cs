namespace CoCoReTrade.Domain;

public static class ProductSizing
{
    public const string FreeSize = "Free size";
    public const int MaximumLength = 30;
    public static IReadOnlyList<string> Clothing { get; } = ["XS", "S", "M", "L", "XL", "2XL", "3XL"];
    public static bool IsOneSize(string? category) => ProductCategories.IsAccessory(category);
    public static string Normalize(string? category, string? size)
    {
        if (IsOneSize(category))
        {
            var custom = size?.Trim();
            if (string.IsNullOrWhiteSpace(custom) || custom.Length > MaximumLength || custom.Any(char.IsControl))
                throw new ArgumentException("Nhập kích cỡ phụ kiện từ 1 đến 30 ký tự.");
            return custom;
        }
        var value = size?.Trim().ToUpperInvariant() switch { "XXL" => "2XL", "XXXL" => "3XL", var other => other };
        if (value is null || !Clothing.Contains(value))
            throw new ArgumentException("Chọn kích cỡ quần áo từ XS đến 3XL.");
        return value;
    }
}
