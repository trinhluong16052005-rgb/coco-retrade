using System.Globalization;
using System.Text;

namespace CoCoReTrade.Domain;

public static class VietnamProvinces
{
    // Nghị quyết 202/2025/QH15.
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly<string>(
    [
        "An Giang", "Bắc Ninh", "Cà Mau", "Cao Bằng", "Cần Thơ", "Đà Nẵng", "Đắk Lắk",
        "Điện Biên", "Đồng Nai", "Đồng Tháp", "Gia Lai", "Hà Nội", "Hà Tĩnh", "Hải Phòng",
        "Hồ Chí Minh", "Huế", "Hưng Yên", "Khánh Hòa", "Lai Châu", "Lâm Đồng", "Lạng Sơn",
        "Lào Cai", "Nghệ An", "Ninh Bình", "Phú Thọ", "Quảng Ngãi", "Quảng Ninh", "Quảng Trị",
        "Sơn La", "Tây Ninh", "Thái Nguyên", "Thanh Hóa", "Tuyên Quang", "Vĩnh Long"
    ]);

    private static readonly IReadOnlyDictionary<string, string> ByName = BuildLookup();

    public static string? Normalize(string? province)
    {
        if (string.IsNullOrWhiteSpace(province)) return null;
        var key = NameKey(province);
        foreach (var prefix in new[] { "thanhpho", "tinh", "tp" })
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                key = key[prefix.Length..];
                break;
            }
        }
        return ByName.GetValueOrDefault(key);
    }

    private static IReadOnlyDictionary<string, string> BuildLookup()
    {
        var names = Names.ToDictionary(NameKey);
        // Accept saved addresses that still use names from before the 2025 merger.
        foreach (var (current, previous) in new (string, string)[]
        {
            ("An Giang", "Kiên Giang"), ("Bắc Ninh", "Bắc Giang"), ("Cà Mau", "Bạc Liêu"),
            ("Cần Thơ", "Hậu Giang|Sóc Trăng"), ("Đà Nẵng", "Quảng Nam|Da Nang City"),
            ("Đắk Lắk", "Phú Yên"), ("Đồng Nai", "Bình Phước"), ("Đồng Tháp", "Tiền Giang"),
            ("Gia Lai", "Bình Định"), ("Hải Phòng", "Hải Dương"),
            ("Hồ Chí Minh", "Bà Rịa Vũng Tàu|Bình Dương|Sài Gòn|Ho Chi Minh City|HCM"),
            ("Huế", "Thừa Thiên Huế"), ("Hưng Yên", "Thái Bình"), ("Khánh Hòa", "Ninh Thuận"),
            ("Lâm Đồng", "Bình Thuận|Đắk Nông"), ("Lào Cai", "Yên Bái"), ("Ninh Bình", "Hà Nam|Nam Định"),
            ("Phú Thọ", "Hòa Bình|Vĩnh Phúc"), ("Quảng Ngãi", "Kon Tum"), ("Quảng Trị", "Quảng Bình"),
            ("Tây Ninh", "Long An"), ("Thái Nguyên", "Bắc Kạn"), ("Tuyên Quang", "Hà Giang"),
            ("Vĩnh Long", "Bến Tre|Trà Vinh")
        })
        {
            foreach (var name in previous.Split('|'))
                names.Add(NameKey(name), current);
        }
        return names;
    }

    private static string NameKey(string name)
    {
        var letters = name.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        return new string(letters.Where(c => char.IsLetter(c) &&
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
