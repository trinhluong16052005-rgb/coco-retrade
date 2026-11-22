# Chạy mã nguồn COCO RETRADE

Kho GitHub công khai chứa mã nguồn website, migration SQL, kiểm thử và tài nguyên giao diện có thể chia sẻ.

Dữ liệu SQL, ảnh tải lên và video không nằm trong lịch sử Git. Các tệp này có trong gói ZIP đầy đủ ở mục Releases.

## Có gói bàn giao đầy đủ

Tải [CoCoReTrade_BanGiao_20261122.zip trong Releases](https://github.com/trinhluong16052005-rgb/coco-retrade/releases/tag/ban-giao-20261122). Giải nén, đọc HUONG_DAN_KHOI_PHUC.md rồi chạy các script kiểm tra, khôi phục SQL và khởi động trong gói đó. Gói đã được chuẩn bị với đường dẫn tương đối và database mới để không ghi đè dữ liệu sẵn có.

Gói ZIP là bản dữ liệu tại thời điểm đóng gói ngày 22/11/2026, không tự cập nhật theo website đang chạy. Kích thước: 303.166.981 byte. SHA-256: `9A335277954421AF89F881B357B8B7C278CC64F031AB2C8A8902ADBDDA4F253E`.

## Chạy một database mới

1. Cài .NET 10 SDK, SQL Server 2022 hoặc mới hơn.
2. Đặt ConnectionStrings:CoCo bằng dotnet user-secrets hoặc ConnectionStrings__CoCo. Không ghi mật khẩu SQL vào Git.
3. Nếu tạo database hoàn toàn mới, đặt Admin:InitialPassword qua user-secrets hoặc biến môi trường COCO_ADMIN_INITIAL_PASSWORD bằng mật khẩu bạn tự chọn.
4. Chạy dotnet restore CoCoReTrade.slnx.
5. Chạy dotnet run --project src/CoCoReTrade.Web --launch-profile http.
6. Mở http://localhost:5001.

Migration tự áp dụng khi khởi động. Database mới không có sản phẩm, tài khoản hay đơn hàng của bản bàn giao.

Ảnh người dùng lưu ở src/CoCoReTrade.Web/PrivateUploads hoặc thư mục Storage:Root đã cấu hình. Video nằm ở src/CoCoReTrade.Web/wwwroot/media. Cần thêm đúng các tệp này từ bản bàn giao nếu muốn giữ nội dung có sẵn.

SMTP và Google OAuth cần cấu hình riêng. Tên biến và hướng dẫn nằm trong README.md và docs/CAU-HINH-OTP-GOOGLE.md; kho không chứa mật khẩu SMTP, Google ClientSecret hoặc API key.

