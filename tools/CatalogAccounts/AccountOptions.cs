using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace CoCoReTrade.CatalogAccounts;

public sealed record AccountOptions(string Database, string Server, bool Apply)
{
    public static AccountOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var apply = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply")
            {
                if (apply) throw new AccountToolException("Tham số bị lặp.");
                apply = true;
            }
            else if (args[i] is "--database" or "--server" && i + 1 < args.Length &&
                     !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                if (!values.TryAdd(args[i], args[++i]))
                    throw new AccountToolException("Tham số bị lặp.");
            }
            else throw new AccountToolException("Tham số không hợp lệ hoặc thiếu giá trị.");
        }
        if (!values.TryGetValue("--database", out var database) ||
            !string.Equals(database, "CoCoReTrade", StringComparison.Ordinal))
            throw new AccountToolException("Bắt buộc chỉ định --database CoCoReTrade; không nhận database khác.");
        var server = values.GetValueOrDefault("--server", @".\SQLEXPRESS");
        var localPattern = @"^(?:\.|\(local\)|localhost|127\.0\.0\.1|\[?::1\]?|" +
            Regex.Escape(Environment.MachineName) + @")(?:\\[A-Za-z0-9_-]+)?(?:,\d{1,5})?$";
        if (!Regex.IsMatch(server, localPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new AccountToolException("Chỉ nhận SQL Server local qua Windows authentication.");
        return new AccountOptions(database, server, apply);
    }

    public string ConnectionString() => new SqlConnectionStringBuilder
    {
        DataSource = Server,
        InitialCatalog = Database,
        IntegratedSecurity = true,
        Encrypt = true,
        TrustServerCertificate = true,
        ConnectTimeout = 10,
        ApplicationName = "CoCoReTrade CatalogAccounts"
    }.ConnectionString;
}

public sealed class AccountToolException(string message) : Exception(message);
