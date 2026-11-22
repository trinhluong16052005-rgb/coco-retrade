# CatalogCompletion

Hoàn thiện metadata của sản phẩm đã tồn tại và đổi tên hiển thị theo manifest đối chiếu. Không tạo sản phẩm, không đổi tên sản phẩm/danh mục/giá bán, trạng thái, duyệt sơ bộ, kiểm định, ảnh, đơn hàng hoặc tài khoản đăng nhập. Không khởi động web, migration hoặc seeder.

SQL cố định `.\SQLEXPRESS`, Windows auth. Bắt buộc có `--database CoCoReTrade` hoặc `--database CoCoReTradeDemo`, khớp chính xác trường `database` của manifest. Không nhận connection string, server, username, password hay biến cấu hình bí mật.

## Manifest

```json
{
  "id": "coco-metadata-20261117",
  "database": "CoCoReTrade",
  "actorEmail": "admin01@cocoretrade.test",
  "products": [
    {
      "id": "11111111-1111-1111-1111-111111111111",
      "before": {
        "name": "Áo sơ mi",
        "brand": "",
        "category": "Áo",
        "size": "M",
        "originalPrice": 300000,
        "askingPrice": 300000,
        "declaredCondition": 90,
        "description": null,
        "color": null,
        "status": "PendingApproval",
        "preliminaryStatus": "Pending",
        "sku": ""
      },
      "after": {
        "brand": "Chưa xác nhận",
        "size": "M",
        "description": "Mô tả được chủ website duyệt; chờ kiểm định hiện vật.",
        "color": "Trắng",
        "originalPrice": 300000
      },
      "notes": "Nêu nguồn/thông tin chưa xác nhận và phạm vi nội dung chủ website cho phép bổ sung."
    }
  ],
  "users": [
    {
      "email": "seller01@cocoretrade.test",
      "beforeName": "Seller 01",
      "afterName": "Nguyễn Minh Anh"
    }
  ]
}
```

ID và giá ở ví dụ chỉ mô tả schema. Manifest thật phải dùng snapshot hiện tại đã đọc từ đúng database. Có thể để `products` hoặc `users` trống nhưng tổng cần 1–100 mục; ID/email không được trùng. Mọi thuộc tính trong schema đều bắt buộc, kể cả `description`/`color` nullable trong `before`. JSON từ chối thuộc tính lạ và enum dạng số.

`after` cần đầy đủ chuỗi không rỗng, không khoảng trắng hai đầu: brand tối đa 100, size 30, description 2.000, color 80 ký tự. Tên hiển thị tối đa 150. Notes cần 1–2.000 ký tự và được lưu nguyên văn trong audit sản phẩm. Giá theo giới hạn model, số nguyên VNĐ, giá gốc không nhỏ hơn giá bán. Không tự tạo mức giảm cho hàng bán thật.

## Phạm vi cố định

| Database | Actor phải tồn tại, có role Admin | Sản phẩm | Tên hiển thị được đổi |
|---|---|---|---|
| CoCoReTrade | admin01@cocoretrade.test | Mã ký gửi `CI-`, SKU trống, `PendingApproval/Pending`; người bán seller01–seller08@cocoretrade.test. Giá gốc phải giữ nguyên. | seller01–seller08, buyer01, admin01@cocoretrade.test |
| CoCoReTradeDemo | admin@cocoretrade.vn | Chỉ `Active`, SKU `CC-2026-900xxx`, seller.demo@cocoretrade.test; bắt buộc `--allow-active`. Có thể bổ sung giá gốc tham khảo hợp lệ trong demo. | seller.demo@cocoretrade.test, buyer.demo@cocoretrade.test, admin@cocoretrade.vn |

## Chạy từ gốc repo

```powershell
dotnet run --project tools/CatalogCompletion -- --manifest .artifacts/catalog-completion/main.json --database CoCoReTrade --validate-only
```

`--validate-only` kiểm schema, giới hạn, scope và giá; không kết nối SQL, không ghi tệp.

```powershell
dotnet run --project tools/CatalogCompletion -- --manifest .artifacts/catalog-completion/main.json --database CoCoReTrade
```

Mặc định dry-run: kiểm actor/role, chủ sở hữu, scope và toàn bộ snapshot. Chỉ đọc, không ghi DB/audit/receipt. Dữ liệu hiện tại phải khớp toàn bộ `before` hoặc toàn bộ kết quả `after`; trạng thái sửa dở hoặc thay đổi ngoài manifest sẽ dừng toàn bộ đợt.

```powershell
dotnet run --project tools/CatalogCompletion -- --manifest .artifacts/catalog-completion/main.json --database CoCoReTrade --apply
```

Chỉ chạy `--apply` sau khi đối chiếu dry-run và sao lưu database. Toàn bộ cập nhật cùng audit nằm trong một transaction Serializable. Product dùng EF rowversion; user dùng ConcurrencyStamp gốc nhưng chỉ ghi FullName, không thay stamp, password, email, username, roles, claims hoặc cài đặt auth. Guard ChangeTracker từ chối mọi cột/thực thể ghi ngoài phạm vi.

Audit sản phẩm: `Catalog.MetadataCompleted`, EntityType `Product`. Audit tên hiển thị: `Accounts.DisplayNameUpdated`, EntityType `User`. Mục đã khớp toàn bộ after sẽ SKIP, không thêm audit hoặc update lại.

Receipt mặc định cạnh manifest với đuôi `.results.json`; có thể chỉ rõ `--results <file.json>`. Receipt có target, actor, trước/sau, quyết định và rowversion, không có credential/auth fields. Giữ manifest/receipt ngoài Git. Công cụ ghi receipt vào file tạm và flush trước commit, chuyển sang tên cuối sau commit. Nếu finalize thất bại sau commit, exit code 2 báo rõ DB đã COMMIT và vị trí file tạm; chạy dry-run để xác nhận, sau đó chạy lại cùng manifest để SKIP và tạo lại receipt. Không tự rollback DB đã commit.

Kiểm thử không dùng SQL, build riêng khỏi bin web:

```powershell
dotnet test tools/CatalogCompletion/tests/CatalogCompletion.Tests.csproj --artifacts-path .artifacts/catalog-completion-check -c Release
```
