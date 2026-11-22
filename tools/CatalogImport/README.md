# CatalogImport

Nhập sản phẩm từ manifest vào **ký gửi chờ duyệt**. Không tự duyệt sơ bộ, kiểm định, cấp SKU hoặc đăng bán. Không chạy migration/seeder, không tạo hay sửa tài khoản và phân quyền. Công cụ không tải ảnh hoặc truy cập website nguồn; ảnh cần được chuẩn bị local trước.

Chạy từ thư mục gốc CoCoRetrade với .NET 10. Chỉ dùng SQL Server local và Windows auth; không nhận connection string, username hoặc mật khẩu. Bắt buộc ghi rõ `--database`. Mỗi món phải có `sellerEmail` của tài khoản đã tồn tại, hoặc dùng `--seller-email` làm mặc định. Có thể phân nhiều người bán trong cùng manifest; toàn bộ chủ sở hữu được đối chiếu trước khi ghi món đầu tiên.

## Manifest

```json
{
  "schemaVersion": 1,
  "id": "coco-catalog-20261117",
  "items": [
    {
      "id": "source-listing-id",
      "sourceUrl": "https://www.depop.com/products/source-listing-id/",
      "sellerEmail": "seller@example.com",
      "name": "Tên sản phẩm tiếng Việt",
      "brand": "Thương hiệu trên nhãn",
      "category": "Áo",
      "size": "M",
      "originalPriceVnd": 450000,
      "askingPriceVnd": 300000,
      "conditionPercent": 95,
      "allowOffers": true,
      "images": [
        { "file": "photos/front.webp", "sourceUrl": "https://example.com/original-front.webp" },
        { "file": "photos/back.webp", "sourceUrl": "https://example.com/original-back.webp" },
        { "file": "photos/label.webp", "sourceUrl": "https://example.com/original-label.webp" }
      ],
      "description": "Mô tả đối chiếu từ listing gốc.",
      "color": "Màu trên ảnh hoặc nhãn.",
      "notes": "Ghi nguồn giá quy đổi, quyền sử dụng ảnh hoặc điều cần kiểm tra."
    }
  ]
}
```

Ví dụ trên mô tả cấu trúc, không phải hàng đã được nhập. Thay bằng URL và ảnh thật. `file` tính từ thư mục chứa manifest; hỗ trợ đường dẫn tuyệt đối. Danh mục hiện tại gồm `Áo`, `Quần`, `Đầm/Váy`, `Set đồ`, `Phụ kiện`. Mã cũ `Túi` hoặc `Túi xách` vẫn được đọc để đối chiếu receipt và được lưu thành `Phụ kiện` khi nhập mới; không sửa danh mục gốc trong manifest cũ. Mỗi món cần 3–10 ảnh tĩnh JPG/PNG/WebP khác nhau, mỗi ảnh 100 byte–5 MB, tối đa 40 megapixel. Công cụ giải mã ảnh bằng SkiaSharp (MIT), đối chiếu đuôi file/định dạng và loại ảnh trùng theo cả SHA-256 file lẫn pixel; đổi tên hoặc đổi metadata ảnh không giúp vượt kiểm tra.

### Nguồn ảnh người dùng cung cấp trên ổ local

`sourceUrl` của sản phẩm và ảnh có thể dùng file URI Windows tuyệt đối, ví dụ `file:///D:/DỰ%20ÁN/A01.jpg`. Với một món lấy từ ảnh local, dùng file gốc đầu tiên làm nguồn ổn định của sản phẩm; mỗi `images[].sourceUrl` trỏ đúng file gốc tương ứng. `images[].file` vẫn trỏ tới ảnh local đã chuẩn bị để nhập, có thể là WebP tối ưu. Ví dụ phần nguồn:

```json
{
  "id": "user-local-a01",
  "sourceUrl": "file:///D:/DỰ%20ÁN/A01.jpg",
  "images": [
    { "file": "photos/user-local-a01/01.webp", "sourceUrl": "file:///D:/DỰ%20ÁN/A01.jpg" },
    { "file": "photos/user-local-a01/02.webp", "sourceUrl": "file:///D:/DỰ%20ÁN/A01%20(2).jpg" },
    { "file": "photos/user-local-a01/03.webp", "sourceUrl": "file:///D:/DỰ%20ÁN/A01%20(3).jpg" }
  ]
}
```

Đây chỉ là phần nguồn của record; vẫn cần đầy đủ trường sản phẩm như mẫu chính. URI cho phép khoảng trắng/tiếng Việt hoặc dạng percent-encoded tương đương; chuẩn hóa thành URI escaped với đường dẫn viết hoa để khóa nguồn Windows không đổi khi chỉ khác cách viết hoa. Giữ tên file neo ổn định giữa các lần chạy. Di chuyển/đổi tên file neo làm đổi khóa nguồn; cùng manifest/record id sẽ bị chặn để đối chiếu, không tự tạo bản thay thế.

Chỉ nhận dạng `file:///D:/thu-muc/anh.jpg` có ký tự ổ đĩa. Không nhận host/UNC, đường dẫn tương đối, namespace/tên thiết bị Windows, alternate data stream, userinfo, query hoặc fragment. File URI chỉ là thông tin đối chiếu trong manifest/receipt riêng, không được tải, mở hoặc gửi lên website bởi công cụ. Chỉ trường `images[].file` được đọc và giải mã để nhập. Giữ bản gốc, SHA-256 và bảng đối chiếu ảnh trước/sau tối ưu trong báo cáo chuẩn bị riêng ngoài Git; file URI không tự chứng minh quyền dùng ảnh hoặc món hàng thực tế.

Khi người dùng thay ảnh bằng món khác, không giữ URL listing/ảnh Depop cũ chỉ vì tên file tương ứng checklist. Dùng record id/nguồn local mới cho món mới và ghi dữ liệu theo ảnh thực tế. Bảy receipt Depop đã nhập không được sửa hoặc gán lại bằng ảnh local. Nếu ảnh local thực sự của cùng món đã nhập, dùng luồng sửa ký gửi hiện có thay vì nhập thành món mới.

Giá là số nguyên VNĐ, từ 1.000 đến 999.999.999.999, giá gốc không thấp hơn giá mong muốn. Tình trạng 50–100; tên 3–200 ký tự, thương hiệu tối đa 100, size 1–30. Phải nhập dữ liệu theo món đồ thực tế. `description` (tối đa 2.000 ký tự) và `color` (tối đa 80 ký tự) được kiểm tra trước khi kết nối SQL, truyền qua `SubmitListing` và lưu vào sản phẩm khi nhập mới. Giới hạn tính sau khi bỏ khoảng trắng hai đầu; chuỗi trống được lưu thành null. `notes`, URL nguồn và URL ảnh chỉ giữ trong receipt riêng để đối chiếu, không tự xuất lên website.

## Chạy kiểm tra

```powershell
dotnet run --project tools/CatalogImport -- --manifest .artifacts/catalog/manifest.json --database CoCoReTrade --seller-email seller@example.com --validate-only
```

`--validate-only`: kiểm tra manifest/ảnh, không kết nối SQL, không ghi file.

```powershell
dotnet run --project tools/CatalogImport -- --manifest .artifacts/catalog/manifest.json --database CoCoReTrade --seller-email seller@example.com
```

Không có `--apply` là **dry-run**: kiểm tra ảnh, kết nối đúng database, xác nhận người bán tồn tại, đối chiếu nguồn/receipt/sản phẩm đã nhập. Chỉ thực hiện truy vấn đọc; không ghi DB, ảnh, tệp kết quả hoặc khóa file.

Tham số tùy chọn:

- `--server '.\SQLEXPRESS'`: mặc định như trên; chỉ nhận máy local.
- `--web-root 'D:\...\src\CoCoReTrade.Web'`: dùng khi chạy ngoài gốc repo.
- `--storage-root 'D:\...\PrivateUploads'`: phải trùng `Storage:Root` của web; mặc định `PrivateUploads` trong web root.
- `--results '.artifacts/catalog/import.results.json'`: receipt dùng cho các lần chạy lại. Mặc định `.artifacts/catalog-import/<target-hash>.results.json`, tách theo SQL server/database; mỗi receipt giữ `sellerId` riêng.

## Ghi ký gửi sau khi kiểm tra

```powershell
dotnet run --project tools/CatalogImport -- --manifest .artifacts/catalog/manifest.json --database CoCoReTrade --seller-email seller@example.com --apply
```

`--apply` gọi `IProductService.SubmitAsync` với người bán đã tồn tại, ưu tiên `item.sellerEmail` rồi mới dùng `--seller-email`. Từng món được ghi trong transaction riêng, giữ thông báo ký gửi của hệ thống. Kết quả luôn `PendingApproval` + `Pending`, `VerifiedCondition = null`, SKU trống, chưa có ngày đăng bán. Cần kiểm định và duyệt qua quy trình CoCo sau đó. Một đợt có thể dừng sau vài món đã hoàn tất; chạy lại dry-run để đối chiếu và chỉ nhập phần còn lại.

Chống trùng dựa trên URL nguồn chuẩn hóa và `(manifest id, record id)`. Bỏ fragment, dấu `/` cuối và tham số tracking `utm_*`, `gclid`, `fbclid`; tham số có ý nghĩa khác được giữ. Mã ký gửi ổn định `CI-<20 ký tự SHA-256 URL>` dùng unique index sẵn có trong SQL; đây là mã chờ duyệt, **không phải SKU**. Receipt lưu `productId`, nguồn, fingerprint dữ liệu/ảnh và bản ghi đầu vào, ghi nguyên tử sau từng commit. Nếu tiến trình dừng giữa commit và ghi receipt, lần sau đối chiếu mã ký gửi/nội dung/ảnh trong DB để khôi phục, không tạo món mới.

Quy tắc bỏ tracking/fragment nêu trên chỉ áp dụng cho HTTP/HTTPS; file URI từ chối query/fragment. Nhánh HTTP/HTTPS, công thức fingerprint và schema receipt v1 giữ nguyên để đọc/chạy lại receipt trước khi hỗ trợ file URI. Nguồn local chỉ mở rộng kiểu giá trị nguồn, không thay nghiệp vụ chờ duyệt hoặc yêu cầu tối thiểu ba ảnh khác nhau.

Receipt mới thêm `detailsStored: true` để ghi nhận mô tả/màu sắc đã được lưu SQL. Receipt cũ thiếu trường này được đọc thành false: vẫn SKIP đúng sản phẩm cũ, không tự bổ sung hoặc so metadata SQL chưa từng được lưu. Alias manifest/record của nguồn cũ kế thừa cờ false. Mọi receipt vẫn so mô tả/màu sắc đầu vào với bản nguồn trong receipt sau khi chuẩn hóa như model; sửa các trường này sẽ dừng để dùng luồng sửa ký gửi, dù fingerprint cốt lõi giữ nguyên. Với receipt mới, hoặc khi khôi phục sau commit mà chưa có receipt, metadata trong DB phải khớp dữ liệu nhập.

Nguồn đã nhập với dữ liệu khác, record id dùng lại cho URL khác, receipt lệch DB hoặc người bán khác sẽ dừng để đối chiếu. Công cụ không tự cập nhật, xóa hay tạo bản thay thế. Giữ receipt ngoài Git và sao lưu cùng DB/PrivateUploads. Những lần chạy lại với cùng dữ liệu chỉ bỏ qua món đã nhập.

## Chuẩn bị gallery Depop đã được quan sát

`tools/prepare_depop_catalog.py` dùng Python + Pillow, chỉ tải URL `metadata.image` đã thu thập từ trang công khai, không đăng nhập hoặc vượt hạn chế. File override theo URL listing chứa tên/mô tả tiếng Việt, size theo nguồn, màu và `sellerEmail`. Dữ liệu raw và override nằm trong `.artifacts`, ảnh chuẩn bị giữ nguyên nội dung, thu nhỏ tối đa 1400 px và chuyển WebP chất lượng 85. Ảnh trùng nội dung được bỏ; nếu còn dưới ba ảnh, script dừng. Báo cáo `.photos-report.json` giữ URL, hash, kích thước và các ảnh bị loại.

```powershell
python tools/prepare_depop_catalog.py --raw .artifacts/depop-raw.json --overrides .artifacts/catalog-import/bags.overrides.json --output .artifacts/catalog-import/bags.fragment.json --category "Phụ kiện" --manifest-id coco-live-bags
```

Script chỉ hỗ trợ tỷ lệ tham khảo USD do người vận hành chỉ rõ, mặc định 26.000 VNĐ/USD; không gọi API tỷ giá. Giá gốc chưa biết được đặt bằng giá mong muốn và ghi rõ trong notes, không tự tạo mức giảm. Tình trạng chuyển từ nhãn nguồn sang mức khai báo, chưa phải kết luận kiểm định. Mô tả lỗi và thông tin chưa xác nhận phải giữ trong override/receipt; không suy đoán chất liệu, số đo hoặc khả năng khắc phục lỗi. Script không ghi SQL.

Kiểm thử độc lập, không dùng SQL:

```powershell
dotnet test tools/CatalogImport/tests/CatalogImport.Tests.csproj
```
