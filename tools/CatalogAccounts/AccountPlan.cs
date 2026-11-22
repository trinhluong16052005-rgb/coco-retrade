using System.Security.Cryptography;

namespace CoCoReTrade.CatalogAccounts;

public sealed record PlannedAccount(string Email, string FullName, string[] Roles);
public sealed record PrivateAccount(string Email, string Password, string UserId, string[] Roles);

public static class AccountPlan
{
    public static IReadOnlyList<PlannedAccount> Build() =>
    [
        new("seller01@cocoretrade.test", "Nguyễn Thị Ngọc Mai", ["User"]),
        new("seller02@cocoretrade.test", "Trần Minh Anh", ["User"]),
        new("seller03@cocoretrade.test", "Lê Thảo Nhi", ["User"]),
        new("seller04@cocoretrade.test", "Phạm Khánh Linh", ["User"]),
        new("seller05@cocoretrade.test", "Nguyễn Hoàng Yến", ["User"]),
        new("seller06@cocoretrade.test", "Võ Bảo Ngọc", ["User"]),
        new("seller07@cocoretrade.test", "Đặng Thu Hà", ["User"]),
        new("seller08@cocoretrade.test", "Huỳnh Gia Hân", ["User"]),
        new("buyer01@cocoretrade.test", "Trần Quốc Bảo", ["User"]),
        new("admin01@cocoretrade.test", "Quản Lý CoCo", ["User", "Inspector", "Admin"])
    ];

    public static string NewPassword()
    {
        string[] groups = ["ABCDEFGHJKLMNPQRSTUVWXYZ", "abcdefghjkmnpqrstuvwxyz", "23456789", "!@#$%*+-=?_"];
        var alphabet = string.Concat(groups);
        var chars = new char[28];
        for (var i = 0; i < groups.Length; i++)
            chars[i] = groups[i][RandomNumberGenerator.GetInt32(groups[i].Length)];
        for (var i = groups.Length; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var other = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[other]) = (chars[other], chars[i]);
        }
        return new string(chars);
    }
}
