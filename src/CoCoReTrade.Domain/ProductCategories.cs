namespace CoCoReTrade.Domain;

public static class ProductCategories
{
    public const string Tops = "Áo";
    public const string Bottoms = "Quần";
    public const string Dresses = "Đầm/Váy";
    public const string Sets = "Set đồ";
    public const string Accessories = "Phụ kiện";
    public const string Bags = "Túi"; // Legacy code accepted by older listings and links.
    public static IReadOnlyList<string> Clothing { get; } = [Tops, Bottoms, Dresses, Sets];
    public static IReadOnlyList<string> Main { get; } = [Tops, Bottoms, Dresses, Sets, Accessories];
    public static IReadOnlyList<string> All { get; } = Main;

    // Bag aliases resolve to the single accessories category for new or edited listings.
    public static string Normalize(string? category) => category?.Trim() switch
    {
        Tops or "Shirts" => Tops,
        Bottoms => Bottoms,
        Dresses or "Váy" or "Váy/Đầm" or "Váy / Đầm" or "Đầm" => Dresses,
        Sets => Sets,
        Accessories or Bags or "Túi xách" => Accessories,
        _ => throw new ArgumentException("Chọn danh mục Áo, Quần, Váy, Set đồ hoặc Phụ kiện.")
    };
    public static bool IsAccessory(string? category) => category?.Trim() is Accessories or Bags or "Túi xách";
    public static bool IsClothing(string? category) => category?.Trim() is Tops or "Shirts" or Bottoms or Dresses or "Váy" or "Váy/Đầm" or "Váy / Đầm" or "Đầm" or Sets;
    public static string Parent(string category) => IsAccessory(category) ? Accessories : Normalize(category);
    public static string Display(string? category) => category?.Trim() switch
    {
        Dresses or "Váy/Đầm" or "Váy / Đầm" or "Đầm" => "Váy",
        Bags or "Túi xách" => Accessories,
        "Shirts" => Tops,
        var other => other ?? ""
    };
    // Query expansions keep older links and stored category aliases usable during migration.
    public static string[] MatchingStoredValues(string category)
    {
        string normalized;
        try { normalized = Normalize(category); }
        catch (ArgumentException) { return []; }
        return normalized switch
        {
            Accessories => [Accessories, Bags, "Túi xách"],
            Dresses => [Dresses, "Váy", "Váy/Đầm", "Váy / Đầm", "Đầm"],
            Tops => [Tops, "Shirts"],
            var other => [other]
        };
    }
    public static bool Matches(string storedCategory, string filterCategory) =>
        MatchingStoredValues(filterCategory).Contains(storedCategory.Trim(), StringComparer.OrdinalIgnoreCase);
}
