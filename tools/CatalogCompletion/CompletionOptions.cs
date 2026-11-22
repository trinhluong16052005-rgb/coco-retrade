using Microsoft.Data.SqlClient;

namespace CoCoReTrade.CatalogCompletion;

public sealed record CompletionOptions(string Manifest, string Database, string Results, bool Apply, bool AllowActive, bool ValidateOnly)
{
    public static CompletionOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (key is "--apply" or "--allow-active" or "--validate-only")
            {
                if (!flags.Add(key)) throw new ArgumentException($"Tham số lặp: {key}.");
            }
            else if ((key is "--manifest" or "--database" or "--results") && index + 1 < args.Length &&
                !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                if (!values.TryAdd(key, args[++index])) throw new ArgumentException($"Tham số lặp: {key}.");
            }
            else throw new ArgumentException($"Tham số không hợp lệ hoặc thiếu giá trị: {key}.");
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Bắt buộc có {key}.");
        var manifest = Path.GetFullPath(Required("--manifest"));
        var database = Required("--database");
        CompletionValidation.ValidateDatabase(database);
        if (flags.Contains("--allow-active") && database != CompletionValidation.DemoDatabase)
            throw new ArgumentException("--allow-active chỉ dùng với CoCoReTradeDemo.");
        if (flags.Contains("--apply") && flags.Contains("--validate-only"))
            throw new ArgumentException("--validate-only không dùng cùng --apply.");
        var results = Path.GetFullPath(values.GetValueOrDefault("--results", Path.ChangeExtension(manifest, ".results.json")));
        if (string.Equals(manifest, results, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Tệp kết quả phải khác manifest.");
        return new CompletionOptions(manifest, database, results, flags.Contains("--apply"),
            flags.Contains("--allow-active"), flags.Contains("--validate-only"));
    }

    public string ConnectionString() => new SqlConnectionStringBuilder
    {
        DataSource = @".\SQLEXPRESS", InitialCatalog = Database, IntegratedSecurity = true,
        Encrypt = true, TrustServerCertificate = true, ConnectTimeout = 10,
        ApplicationName = "CoCoReTrade CatalogCompletion"
    }.ConnectionString;
}
