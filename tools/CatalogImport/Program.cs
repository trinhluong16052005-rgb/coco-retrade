using CoCoReTrade.CatalogImport;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine("CatalogImport: tạo ký gửi chờ duyệt, không kiểm định hoặc đăng bán.");
    Console.WriteLine("--manifest <file.json> --database <database> [--seller-email <email mặc định>] [--server .\\SQLEXPRESS]");
    Console.WriteLine("[--web-root <src/CoCoReTrade.Web>] [--storage-root <PrivateUploads>] [--results <results.json>]");
    Console.WriteLine("Mặc định dry-run chỉ đọc SQL. --validate-only chỉ kiểm tra manifest/ảnh, không kết nối SQL.");
    Console.WriteLine("--apply ghi sản phẩm PendingApproval / Pending; bắt buộc chỉ rõ database và người bán đã tồn tại.");
    return 0;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
try
{
    var options = ImportOptions.Parse(args);
    var manifest = CatalogValidation.Load(options.Manifest);
    foreach (var record in manifest.Items) _ = options.SellerEmailFor(record.Source);
    if (options.ValidateOnly)
    {
        Console.WriteLine($"VALID: {manifest.Id}; records={manifest.Items.Count}; images={manifest.Items.Sum(item => item.Images.Count)}. Không kết nối SQL hoặc ghi tệp.");
        return 0;
    }
    await ImportRunner.RunAsync(options, manifest, cancellation.Token);
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Đã dừng. Chạy lại dry-run với cùng manifest/results để đối chiếu phần đã nhập."); return 130; }
catch (SqlException error) { Console.Error.WriteLine($"Lỗi SQL ({error.Number}). Kiểm tra server/database, Windows auth và quyền truy cập. Không in chuỗi kết nối."); return 1; }
catch (DbUpdateException) { Console.Error.WriteLine("Không lưu được sản phẩm. Chạy lại dry-run để kiểm tra trùng mã và receipt trước khi thử lại."); return 1; }
catch (Exception error) when (error is ArgumentException or IOException or JsonException or InvalidOperationException or NotSupportedException)
{ Console.Error.WriteLine(error.Message); return 1; }
