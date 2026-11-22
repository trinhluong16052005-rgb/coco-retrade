using System.Text;
using CoCoReTrade.Domain;

namespace CoCoReTrade.Tests;

public sealed class BankQrReaderTests
{
    [Theory]
    [InlineData("NGUYEN VAN A")]
    [InlineData("NGUYỄN VĂN A")]
    [InlineData("")]
    public void ReadsBankAccountAndOptionalHolder(string holder)
    {
        var data = VietQrReader.Read(Payload(holder));
        Assert.Equal("970436", data.BankBin); Assert.Equal("0123456789", data.AccountNumber);
        Assert.Equal(holder, data.AccountHolder);
    }
    [Fact]
    public void RejectsWrongChecksumUnsupportedServiceOrMalformedFields()
    {
        var valid = Payload("NGUYEN VAN A");
        Assert.Throws<ArgumentException>(() => VietQrReader.Read(valid[..^4] + "0000"));
        Assert.Throws<ArgumentException>(() => VietQrReader.Read(Payload("", "QRIBFTTC")));
        Assert.Throws<ArgumentException>(() => VietQrReader.Read("https://example.test/not-a-bank-qr"));
        Assert.Throws<ArgumentException>(() => VietQrReader.Read("0002010002016304FFFF"));
        Assert.Throws<ArgumentException>(() => VietQrReader.Read(new string('0', 5000)));
    }
    internal static string Payload(string holder = "NGUYEN VAN A", string service = "QRIBFTTA")
    {
        static string Tag(string key, string value) => key + Encoding.UTF8.GetByteCount(value).ToString("D2") + value;
        var content = Tag("00", "01") + Tag("01", "11") + Tag("38", Tag("00", "A000000727") +
            Tag("01", Tag("00", "970436") + Tag("01", "0123456789")) + Tag("02", service)) +
            Tag("53", "704") + Tag("58", "VN") + (holder.Length > 0 ? Tag("59", holder) : "") + "6304";
        ushort crc = 0xffff;
        foreach (var value in Encoding.UTF8.GetBytes(content))
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? crc << 1 ^ 0x1021 : crc << 1);
        }
        return content + crc.ToString("X4");
    }
}
