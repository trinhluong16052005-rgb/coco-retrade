# CatalogAccounts

Công cụ cấp 10 tài khoản mới trong SQL Server **CoCoReTrade** trên máy local, dùng Windows authentication. Không tạo hộp thư Gmail, không gửi email, không chạy migration hoặc seed, không sửa tài khoản/role/permission hiện có. Seller và buyer dùng role `User`; admin dùng `User`, `Inspector`, `Admin`, kế thừa permission từ role hiện có.

Chạy từ gốc repository; mặc định chỉ đọc:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --artifacts-path .artifacts/catalog-accounts --project tools/CatalogAccounts -- --database CoCoReTrade
```

Sau khi kiểm tra dry-run, lệnh ghi thật:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --artifacts-path .artifacts/catalog-accounts --project tools/CatalogAccounts -- --database CoCoReTrade --apply
```

Server mặc định `.\SQLEXPRESS`; có thể truyền `--server` cho SQL Server local khác. Không nhận chuỗi kết nối hoặc SQL authentication. Database khác, tham số lạ/lặp, email hoặc username đã tồn tại đều bị từ chối.

Kế hoạch cố định: `seller01@cocoretrade.test` đến `seller08@cocoretrade.test`, `buyer01@cocoretrade.test`, `admin01@cocoretrade.test`. Mỗi tài khoản nhận mật khẩu ngẫu nhiên riêng 28 ký tự, băm bởi `UserManager` / ASP.NET Core Identity. `EmailConfirmed=true` để tài khoản được cấp thủ công đăng nhập được; không giả số điện thoại. Tất cả có `MustChangePassword=true`, ứng dụng chuyển đến đổi mật khẩu khi đăng nhập lần đầu. Không tạo sản phẩm, đơn hàng, ví hoặc giao dịch.

Chỉ khi `--apply`, email, mật khẩu, user ID và roles được ghi vào hai tệp cố định ngoài Git:

- `C:/Users/laptop/Documents/CoCoReTrade-accounts-2026-11-17.txt`
- `C:/Users/laptop/Documents/CoCoReTrade-accounts-2026-11-17.json`

Tệp được tạo mới với ACL chỉ cho Windows user hiện tại và SYSTEM. Không ghi đè, không lưu trong cây Git hoặc qua junction/symlink. Stdout chỉ in số lượng, không in thông tin đăng nhập. Không đưa hai tệp này lên Git hoặc website.

Toàn bộ tài khoản tạo trong một SQL transaction. Lỗi trước COMMIT sẽ rollback và xóa riêng tệp công cụ vừa tạo. Khi COMMIT đã bắt đầu, tệp riêng được giữ dù mất phản hồi SQL, tránh mất mật khẩu nếu SQL đã commit; kiểm tra SQL và tệp trước khi chạy lại. Công cụ không reset hoặc sửa user đã tồn tại.

Kiểm thử riêng, không build web hoặc dừng dịch vụ:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tools/CatalogAccounts/tests/CatalogAccounts.Tests.csproj --artifacts-path .artifacts/catalog-accounts-check
```
