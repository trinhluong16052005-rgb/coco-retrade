using System.Globalization;

namespace CoCoReTrade.Domain;

public static class OrderCodeRules
{
    public static DateOnly VietnamDate(DateTime createdAtUtc) => DateOnly.FromDateTime(createdAtUtc.AddHours(7));
    public static string Format(DateTime createdAtUtc, long sequence)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        return "ORD-" + VietnamDate(createdAtUtc).ToString("yyyyMMdd", CultureInfo.InvariantCulture) +
            "-" + sequence.ToString("D3", CultureInfo.InvariantCulture);
    }
}
