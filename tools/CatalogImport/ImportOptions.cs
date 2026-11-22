using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace CoCoReTrade.CatalogImport;

public sealed record ImportOptions(string Manifest, string Database, string? SellerEmail, string Server,
    string WebRoot, string StorageRoot, string? Results, bool Apply, bool ValidateOnly)
{
    public static ImportOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var named = new HashSet<string>(["--manifest", "--database", "--seller-email", "--server", "--web-root",
            "--storage-root", "--results"], StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key is "--apply" or "--validate-only")
            {
                if (!flags.Add(key)) throw new ArgumentException($"Tham số lặp: {key}.");
            }
            else if (named.Contains(key) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                if (!values.TryAdd(key, args[++i])) throw new ArgumentException($"Tham số lặp: {key}.");
            }
            else throw new ArgumentException($"Tham số không hợp lệ hoặc thiếu giá trị: {key}.");
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Bắt buộc có {key}.");
        var manifest = Path.GetFullPath(Required("--manifest"));
        var database = Required("--database");
        if (!Regex.IsMatch(database, "^[A-Za-z0-9_][A-Za-z0-9_-]{0,127}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Tên database không hợp lệ.");
        var email = values.GetValueOrDefault("--seller-email")?.Trim();
        if (email is not null && !ValidEmail(email))
            throw new ArgumentException("--seller-email cần là email của tài khoản đã tồn tại.");
        var server = values.GetValueOrDefault("--server", @".\SQLEXPRESS");
        var local = @"^(?:\.|\(local\)|localhost|127\.0\.0\.1|\[?::1\]?|" + Regex.Escape(Environment.MachineName) +
            @")(?:\\[A-Za-z0-9_-]+)?(?:,\d{1,5})?$";
        if (!Regex.IsMatch(server, local, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Công cụ chỉ nhận SQL Server trên máy local qua Windows auth.");
        var webRoot = Path.GetFullPath(values.GetValueOrDefault("--web-root", Path.Combine("src", "CoCoReTrade.Web")));
        if (!File.Exists(Path.Combine(webRoot, "CoCoReTrade.Web.csproj")))
            throw new ArgumentException("--web-root phải trỏ đúng thư mục src/CoCoReTrade.Web. Chạy từ gốc repo hoặc ghi đường dẫn đầy đủ.");
        var storage = Path.GetFullPath(values.GetValueOrDefault("--storage-root", Path.Combine(webRoot, "PrivateUploads")));
        var results = values.TryGetValue("--results", out var resultPath) ? Path.GetFullPath(resultPath) : null;
        if (results is not null && string.Equals(manifest, results, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Tệp kết quả phải khác manifest đầu vào.");
        if (flags.Contains("--apply") && flags.Contains("--validate-only"))
            throw new ArgumentException("--validate-only không được dùng cùng --apply.");
        return new ImportOptions(manifest, database, email, server, webRoot, storage, results,
            flags.Contains("--apply"), flags.Contains("--validate-only"));
    }

    public string SellerEmailFor(CatalogRecord record)
    {
        var email = record.SellerEmail?.Trim() ?? SellerEmail;
        if (email is null || !ValidEmail(email))
            throw new ArgumentException($"{record.Id}: cần sellerEmail hợp lệ trong record hoặc --seller-email làm mặc định.");
        return email;
    }

    private static bool ValidEmail(string value) => MailAddress.TryCreate(value, out var address) && address.Address == value;

    public string ConnectionString() => new SqlConnectionStringBuilder
    {
        DataSource = Server,
        InitialCatalog = Database,
        IntegratedSecurity = true,
        Encrypt = true,
        TrustServerCertificate = true,
        ConnectTimeout = 10,
        ApplicationName = "CoCoReTrade CatalogImport"
    }.ConnectionString;
}
