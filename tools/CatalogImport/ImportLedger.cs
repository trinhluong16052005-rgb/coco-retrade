using System.Text.Json;

namespace CoCoReTrade.CatalogImport;

public sealed record ImportReceipt(string ManifestId, string RecordId, string SourceUrl, string Fingerprint,
    Guid ProductId, string ConsignmentCode, DateTime ImportedAtUtc, CatalogRecord Source,
    IReadOnlyList<PreparedImage> Images, string SellerId, bool DetailsStored = false);

public sealed class ImportLedger
{
    public int SchemaVersion { get; init; } = 1;
    public string Server { get; init; } = "";
    public string Database { get; init; } = "";
    public string StorageRoot { get; init; } = "";
    public List<ImportReceipt> Entries { get; init; } = [];

    public static ImportLedger Load(string path, string server, string database, string storageRoot)
    {
        if (!File.Exists(path)) return new ImportLedger
            { Server = server, Database = database, StorageRoot = storageRoot };
        var ledger = JsonSerializer.Deserialize<ImportLedger>(File.ReadAllText(path), CatalogValidation.Json)
            ?? throw new ArgumentException("Tệp kết quả không hợp lệ.");
        if (ledger.SchemaVersion != 1 || !string.Equals(ledger.Server, server, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ledger.Database, database, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(ledger.StorageRoot), storageRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Tệp kết quả thuộc server/database/storage khác. Không dùng lẫn kết quả nhập kho.");
        if (ledger.Entries is null || ledger.Entries.GroupBy(x => (x.ManifestId, x.RecordId)).Any(g => g.Count() > 1) ||
            ledger.Entries.Any(x => x.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(x.SellerId)) || ledger.Entries.GroupBy(x => x.SourceUrl)
                .Any(g => g.Select(x => (x.ProductId, x.Fingerprint, x.ConsignmentCode, x.SellerId)).Distinct().Count() > 1))
            throw new ArgumentException("Tệp kết quả chứa khóa trùng hoặc receipt mâu thuẫn.");
        return ledger;
    }

    public ImportReceipt? Find(string manifestId, PreparedRecord record)
    {
        var byId = Entries.SingleOrDefault(x => x.ManifestId == manifestId && x.RecordId == record.Source.Id);
        if (byId is not null && byId.SourceUrl != record.CanonicalSourceUrl)
            throw new ArgumentException($"{record.Source.Id}: manifest/record id đã được dùng cho URL khác.");
        var bySource = Entries.FirstOrDefault(x => x.SourceUrl == record.CanonicalSourceUrl);
        var entry = byId ?? bySource;
        if (entry is not null && (entry.Fingerprint != record.Fingerprint || entry.ConsignmentCode != record.ConsignmentCode ||
            !CatalogValidation.DetailsMatch(record.Source, entry.Source.Description, entry.Source.Color)))
            throw new ArgumentException($"{record.Source.Id}: nguồn đã nhập nhưng nội dung/ảnh khác receipt. Dùng luồng sửa ký gửi; không tạo bản trùng.");
        return entry;
    }

    public void Record(string manifestId, PreparedRecord record, Guid productId, DateTime importedAt, string sellerId)
    {
        var previous = Find(manifestId, record);
        if (Entries.Any(x => x.ManifestId == manifestId && x.RecordId == record.Source.Id)) return;
        // A new alias of a legacy source must not claim its receipt-only metadata reached SQL.
        var detailsStored = previous?.DetailsStored ?? true;
        Entries.Add(new ImportReceipt(manifestId, record.Source.Id, record.CanonicalSourceUrl, record.Fingerprint,
            productId, record.ConsignmentCode, importedAt, record.Source, record.Images, sellerId, detailsStored));
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(this, CatalogValidation.Json);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                8192, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
