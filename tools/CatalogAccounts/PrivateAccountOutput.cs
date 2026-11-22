using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace CoCoReTrade.CatalogAccounts;

public sealed class PrivateAccountOutput : IDisposable
{
    public const string TextPath = "C:/Users/laptop/Documents/CoCoReTrade-accounts-2026-11-17.txt";
    public const string JsonPath = "C:/Users/laptop/Documents/CoCoReTrade-accounts-2026-11-17.json";
    private readonly List<string> ownedFiles = [];
    private bool retain;

    public static void Validate() { ValidateCandidate(TextPath); ValidateCandidate(JsonPath); }

    public static void ValidateCandidate(string candidate)
    {
        var path = Path.GetFullPath(candidate);
        if (File.Exists(path) || Directory.Exists(path))
            throw new AccountToolException("Tệp tài khoản riêng đã tồn tại; từ chối ghi đè.");
        var parent = new DirectoryInfo(Path.GetDirectoryName(path)!);
        if (!parent.Exists) throw new AccountToolException("Thư mục tệp tài khoản riêng phải tồn tại.");
        for (var directory = parent; directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new AccountToolException("Tệp tài khoản riêng không được đi qua junction hoặc symlink.");
            if (directory.Name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.Combine(directory.FullName, ".git")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
                throw new AccountToolException("Từ chối lưu mật khẩu trong thư mục Git.");
        }
    }

    public void Write(IReadOnlyList<PrivateAccount> accounts)
    {
        Validate();
        WriteFile(TextPath, "Email\tPassword\tUserId\tRoles\n" + string.Join("\n", accounts.Select(
            item => $"{item.Email}\t{item.Password}\t{item.UserId}\t{string.Join(',', item.Roles)}")) + "\n");
        WriteFile(JsonPath, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private void WriteFile(string path, string content)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new AccountToolException("Không xác định được Windows user để đặt quyền tệp riêng.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(currentUser);
        security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl,
            FileShare.None, 4096, FileOptions.WriteThrough, security);
        ownedFiles.Add(path);
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    // Keep credentials once COMMIT starts, even if its response is lost. Never lose
    // passwords for users whose transaction may have committed on the server.
    public void Retain() => retain = true;

    public void Dispose()
    {
        if (retain) return;
        foreach (var path in ownedFiles)
        {
            try { File.Delete(path); }
            catch { /* Leave private files in place if cleanup cannot complete. */ }
        }
    }
}
