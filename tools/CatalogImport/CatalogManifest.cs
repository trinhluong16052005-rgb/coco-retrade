using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using SkiaSharp;

namespace CoCoReTrade.CatalogImport;

public sealed record CatalogManifest(int SchemaVersion, string Id, IReadOnlyList<CatalogRecord> Items);

public sealed record CatalogRecord(string Id, string SourceUrl, string Name, string Brand, string Category,
    string Size, decimal OriginalPriceVnd, decimal AskingPriceVnd, int ConditionPercent, bool AllowOffers,
    IReadOnlyList<CatalogImage> Images, string? Description = null, string? Color = null, string? Notes = null,
    string? SellerEmail = null);

public sealed record CatalogImage(string File, string SourceUrl);
public sealed record PreparedImage(string FilePath, string SourceUrl, string ContentType, long Length,
    string Sha256, string PixelSha256);
public sealed record PreparedRecord(CatalogRecord Source, string CanonicalSourceUrl, string ConsignmentCode,
    string Fingerprint, IReadOnlyList<PreparedImage> Images);
public sealed record PreparedManifest(string Id, IReadOnlyList<PreparedRecord> Items);

public static class CatalogValidation
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static PreparedManifest Load(string file)
    {
        var path = Path.GetFullPath(file);
        if (new FileInfo(path).Length > 5_000_000) throw new ArgumentException("Manifest vượt quá 5 MB.");
        var manifest = JsonSerializer.Deserialize<CatalogManifest>(File.ReadAllText(path), Json)
            ?? throw new ArgumentException("Manifest trống.");
        return Prepare(manifest, Path.GetDirectoryName(path)!);
    }

    public static PreparedManifest Prepare(CatalogManifest manifest, string manifestDirectory)
    {
        if (manifest.SchemaVersion != 1) throw new ArgumentException("schemaVersion phải là 1.");
        ValidateId(manifest.Id, "Manifest");
        if (manifest.Items is null || manifest.Items.Count is < 1 or > 100)
            throw new ArgumentException("Manifest cần từ 1 đến 100 sản phẩm.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var prepared = new List<PreparedRecord>();
        foreach (var record in manifest.Items)
        {
            if (record is null) throw new ArgumentException("Sản phẩm trong manifest không được null.");
            ValidateId(record.Id, "Sản phẩm");
            if (!ids.Add(record.Id)) throw new ArgumentException($"Trùng record id: {record.Id}.");
            var source = CanonicalUrl(record.SourceUrl);
            if (!sources.Add(source)) throw new ArgumentException($"Trùng sourceUrl: {record.Id}.");
            // Validate against the application taxonomy, retaining the original category in
            // provenance and fingerprints so historical manifests/receipts stay compatible.
            try { _ = ProductCategories.Normalize(record.Category); }
            catch (ArgumentException exception)
            {
                throw new ArgumentException($"{record.Id}: {exception.Message}", exception);
            }
            if (record.Images is null || record.Images.Count is < 3 or > 10)
                throw new ArgumentException($"{record.Id}: cần 3–10 ảnh thực tế khác nhau.");
            // The same domain validation used by a normal submission runs before any DB connection.
            Product.Submit("validation-only", "CI-validation", record.Name, record.Brand ?? "", record.Category ?? "",
                record.Size, record.OriginalPriceVnd, record.AskingPriceVnd, record.ConditionPercent,
                record.AllowOffers, Enumerable.Range(0, record.Images.Count)
                    .Select(i => new ProductImage($"validation-{i}.jpg", "image/jpeg", i)).ToList(),
                record.Description, record.Color);
            if (record.OriginalPriceVnd > 999_999_999_999m || record.AskingPriceVnd > 999_999_999_999m)
                throw new ArgumentException($"{record.Id}: giá vượt quá giới hạn form ký gửi.");
            var images = record.Images.Select(image => PrepareImage(image, manifestDirectory)).ToList();
            if (images.Select(i => i.Sha256).Distinct(StringComparer.Ordinal).Count() != images.Count ||
                images.Select(i => i.PixelSha256).Distinct(StringComparer.Ordinal).Count() != images.Count)
                throw new ArgumentException($"{record.Id}: ảnh trùng nội dung; cần 3–10 ảnh khác nhau, không sao chép cùng ảnh.");
            var code = "CI-" + Hash(source)[..20];
            var fingerprint = Fingerprint(source, record.Name.Trim(), record.Brand?.Trim() ?? "",
                record.Category!.Trim(), record.Size.Trim(), record.OriginalPriceVnd, record.AskingPriceVnd,
                record.ConditionPercent, record.AllowOffers, images.Select(i => i.Sha256));
            prepared.Add(new PreparedRecord(record, source, code, fingerprint, images));
        }
        return new PreparedManifest(manifest.Id, prepared);
    }

    public static string CanonicalUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new ArgumentException("Nguồn cần là URL HTTP/HTTPS hoặc file URI Windows tuyệt đối.");
        if (uri.Scheme == "file") return CanonicalWindowsFileUrl(value, uri);
        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("URL nguồn phải là HTTP/HTTPS tuyệt đối, không chứa thông tin đăng nhập.");
        // Keep the HTTP identity byte-for-byte compatible with existing import receipts.
        var builder = new UriBuilder(uri) { Fragment = "", Host = uri.IdnHost.ToLowerInvariant() };
        if (builder.Path.Length > 1) builder.Path = builder.Path.TrimEnd('/');
        // Drop tracking only. Meaningful query parameters remain part of the listing identity.
        builder.Query = string.Join("&", uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !IsTrackingParameter(part.Split('=', 2)[0])).Order(StringComparer.Ordinal));
        return builder.Uri.AbsoluteUri;
    }

    private static string CanonicalWindowsFileUrl(string value, Uri uri)
    {
        // A source URI is provenance only. Files are read exclusively from CatalogImage.File;
        // never fetch or open a source URI, including UNC paths and Windows device namespaces.
        if (!Regex.IsMatch(value, "^file:///[A-Za-z]:/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !uri.IsFile || uri.IsUnc || !string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            value.IndexOfAny(['\\', '?', '#']) >= 0)
            throw new ArgumentException("Nguồn local cần dạng file:///D:/thu-muc/anh.jpg; không nhận host, UNC, thiết bị, query hoặc fragment.");
        var path = uri.LocalPath;
        if (!Regex.IsMatch(path, @"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant) ||
            path.Length <= 3 || path.EndsWith('\\') || path.EndsWith('/'))
            throw new ArgumentException("File URI cần đường dẫn file Windows tuyệt đối có ký tự ổ đĩa.");
        var segments = path[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment.Any(character => char.IsControl(character) || "<>:\"|?*".Contains(character)) ||
            segment.EndsWith('.') || segment.EndsWith(' ') ||
            Regex.IsMatch(segment, @"^(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw new ArgumentException("File URI không được trỏ tới thiết bị Windows hoặc tên file không hợp lệ.");
        var fullPath = Path.GetFullPath(path);
        // Windows drive paths use case-insensitive identity. The original spelling remains
        // in CatalogRecord/Images in the private receipt for source-file comparison.
        return new Uri(fullPath.ToUpperInvariant()).AbsoluteUri;
    }

    private static bool IsTrackingParameter(string key)
    {
        key = Uri.UnescapeDataString(key);
        return key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("gclid", StringComparison.OrdinalIgnoreCase) || key.Equals("fbclid", StringComparison.OrdinalIgnoreCase);
    }

    private static PreparedImage PrepareImage(CatalogImage source, string manifestDirectory)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.File)) throw new ArgumentException("Thiếu đường dẫn ảnh local.");
        var url = CanonicalUrl(source.SourceUrl);
        var path = Path.GetFullPath(source.File, Path.GetFullPath(manifestDirectory));
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 100 or > 5_000_000)
            throw new ArgumentException($"Ảnh không tồn tại hoặc dung lượng ngoài 100 byte–5 MB: {source.File}.");
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var expectedFormat = ext switch { ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".png" => SKEncodedImageFormat.Png, ".webp" => SKEncodedImageFormat.Webp, _ => (SKEncodedImageFormat?)null };
        if (expectedFormat is null) throw new ArgumentException("Chỉ nhận JPG, PNG hoặc WebP.");
        using var codec = SKCodec.Create(path) ?? throw new ArgumentException($"Không giải mã được ảnh: {source.File}.");
        if (codec.EncodedFormat != expectedFormat)
            throw new ArgumentException($"Đuôi ảnh không khớp nội dung: {source.File}.");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 40_000_000)
            throw new ArgumentException($"Ảnh lỗi hoặc vượt 40 megapixel: {source.File}.");
        if (codec.FrameCount > 1) throw new ArgumentException($"Cần ảnh tĩnh, không nhận ảnh động: {source.File}.");
        var pixelInfo = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var image = new SKBitmap(pixelInfo);
        if (codec.GetPixels(pixelInfo, image.GetPixels()) != SKCodecResult.Success)
            throw new ArgumentException($"Ảnh lỗi hoặc chưa tải đủ: {source.File}.");
        using var pixels = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        pixels.AppendData(Encoding.UTF8.GetBytes($"{image.Width}x{image.Height}:"));
        pixels.AppendData(image.GetPixelSpan());
        var mimeType = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", _ => "image/webp" };
        return new PreparedImage(path, url, mimeType, info.Length,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
            Convert.ToHexString(pixels.GetHashAndReset()).ToLowerInvariant());
    }

    public static string Fingerprint(string sourceUrl, string name, string brand, string category, string size,
        decimal original, decimal asking, int condition, bool allowOffers, IEnumerable<string> imageHashes) =>
        Hash(JsonSerializer.Serialize(new { sourceUrl, name, brand, category, size, original, asking, condition,
            allowOffers, imageHashes = imageHashes.ToArray() }, Json));

    public static IReadOnlyList<ImageUpload> ReadUploads(PreparedRecord record) => record.Images.Select(image =>
    {
        var bytes = File.ReadAllBytes(image.FilePath);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != image.Sha256)
            throw new ArgumentException($"{record.Source.Id}: ảnh đã thay đổi sau bước kiểm tra; chạy lại dry-run.");
        return new ImageUpload(Path.GetFileName(image.FilePath), image.ContentType, bytes.LongLength,
            () => new MemoryStream(bytes, writable: false));
    }).ToList();

    public static SubmitListing CreateSubmission(PreparedRecord record)
    {
        var source = record.Source;
        return new SubmitListing(source.Name, source.Brand, source.Category, source.Size,
            source.OriginalPriceVnd, source.AskingPriceVnd, source.ConditionPercent, source.AllowOffers,
            ReadUploads(record), source.Description, source.Color);
    }

    public static bool DetailsMatch(CatalogRecord source, string? description, string? color) =>
        NormalizeDetail(source.Description) == NormalizeDetail(description) &&
        NormalizeDetail(source.Color) == NormalizeDetail(color);

    private static string? NormalizeDetail(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void ValidateId(string? id, string label)
    {
        if (id is null || !Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant))
            throw new ArgumentException($"{label}: id cần 1–100 chữ/số hoặc dấu . _ -.");
    }
}
