using System.Text.Json;
using CoCoReTrade.CatalogCompletion;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine("CatalogCompletion: hoàn thiện metadata và tên hiển thị đã được đối chiếu, giữ nghiệp vụ/auth.");
    Console.WriteLine("--manifest <file.json> --database <CoCoReTrade|CoCoReTradeDemo> [--results <file.json>]");
    Console.WriteLine("Mặc định dry-run. --validate-only không kết nối SQL. --apply ghi tất cả trong một transaction.");
    Console.WriteLine("--allow-active chỉ cho demo SKU CC-2026-900xxx. SQL cố định .\\SQLEXPRESS, Windows auth.");
    return 0;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
try
{
    var options = CompletionOptions.Parse(args);
    var manifest = CompletionValidation.Load(options.Manifest, options.Database, options.AllowActive);
    if (options.ValidateOnly)
    {
        Console.WriteLine($"VALID {manifest.Id}; products={manifest.Products.Count}; users={manifest.Users.Count}. Không kết nối SQL hoặc ghi tệp.");
        return 0;
    }
    await CompletionRunner.RunAsync(options, manifest, cancellation.Token);
    return 0;
}
catch (ReceiptFinalizationException error) { Console.Error.WriteLine(error.Message); return 2; }
catch (OperationCanceledException) { Console.Error.WriteLine("Đã dừng; chạy lại dry-run để đối chiếu trước khi thử tiếp."); return 130; }
catch (SqlException error) { Console.Error.WriteLine($"Lỗi SQL ({error.Number}); kiểm tra SQL local/Windows auth. Không in chuỗi kết nối."); return 1; }
catch (DbUpdateConcurrencyException) { Console.Error.WriteLine("Dữ liệu thay đổi đồng thời; transaction bị hủy. Chạy lại dry-run và đối chiếu snapshot."); return 1; }
catch (DbUpdateException) { Console.Error.WriteLine("Không lưu được thay đổi; transaction bị hủy. Chạy lại dry-run để đối chiếu."); return 1; }
catch (Exception error) when (error is ArgumentException or IOException or JsonException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
{ Console.Error.WriteLine(error.Message); return 1; }
