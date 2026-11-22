using System.Text;
using System.Text.RegularExpressions;

namespace CoCoReTrade.Domain;

public sealed record BankQrDetails(string BankBin, string AccountNumber, string AccountHolder);

public static class VietQrReader
{
    public static BankQrDetails Read(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 4096)
            throw new ArgumentException("Mã QR ngân hàng không hợp lệ.");
        var bytes = Encoding.UTF8.GetBytes(payload);
        var fields = Fields(bytes);
        if (bytes.Length < 8 || !payload[^8..].StartsWith("6304", StringComparison.Ordinal) ||
            !fields.TryGetValue("63", out var supplied) ||
            !string.Equals(supplied, Crc16(bytes.AsSpan(0, bytes.Length - 4)), StringComparison.OrdinalIgnoreCase) ||
            fields.GetValueOrDefault("00") != "01" || fields.GetValueOrDefault("53") != "704" ||
            fields.GetValueOrDefault("58") != "VN" || !fields.TryGetValue("38", out var beneficiary))
            throw new ArgumentException("QR không phải VietQR hợp lệ hoặc bị thiếu dữ liệu. Hãy chọn ảnh rõ nét khác.");
        var info = Fields(Encoding.UTF8.GetBytes(beneficiary));
        if (info.GetValueOrDefault("00") != "A000000727" || info.GetValueOrDefault("02") != "QRIBFTTA" ||
            !info.TryGetValue("01", out var bankAccount))
            throw new ArgumentException("Chỉ hỗ trợ VietQR chuyển khoản vào tài khoản ngân hàng.");
        var account = Fields(Encoding.UTF8.GetBytes(bankAccount));
        var bin = account.GetValueOrDefault("00") ?? "";
        var number = account.GetValueOrDefault("01") ?? "";
        if (!Regex.IsMatch(bin, @"^[0-9]{6}$") || !Regex.IsMatch(number, @"^[0-9]{6,30}$"))
            throw new ArgumentException("QR chưa có thông tin tài khoản hợp lệ. Hãy nhập thủ công.");
        var holder = fields.GetValueOrDefault("59")?.Trim() ?? "";
        if (holder.Length > 150 || holder.Any(char.IsControl)) holder = "";
        return new(bin, number, holder);
    }

    private static Dictionary<string, string> Fields(byte[] data)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var offset = 0; offset < data.Length;)
        {
            if (offset + 4 > data.Length || data.AsSpan(offset, 4).ContainsAnyExceptInRange((byte)'0', (byte)'9'))
                throw new ArgumentException("Dữ liệu QR bị thiếu hoặc không đúng định dạng.");
            var tag = Encoding.ASCII.GetString(data, offset, 2);
            var length = (data[offset + 2] - '0') * 10 + data[offset + 3] - '0';
            offset += 4;
            if (offset + length > data.Length || !result.TryAdd(tag, Encoding.UTF8.GetString(data, offset, length)))
                throw new ArgumentException("Dữ liệu QR bị thiếu hoặc trùng trường.");
            offset += length;
        }
        return result;
    }

    private static string Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xffff;
        foreach (var value in data)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? crc << 1 ^ 0x1021 : crc << 1);
        }
        return crc.ToString("X4");
    }
}
