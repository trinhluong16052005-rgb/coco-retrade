# Chạy mã nguồn COCO RETRADE

Kho GitHub công khai chứa mã nguồn website, migration SQL, kiểm thử và tài nguyên giao diện có thể chia sẻ.

Dữ liệu tài khoản, đơn hàng, số dư, ảnh tải lên và tệp .bak không nằm trong Git. Video banner được bàn giao riêng để giữ kho mã nguồn gọn và tránh đưa tệp lớn vào lịch sử Git.

## Có gói bàn giao đầy đủ

Lấy gói ZIP đầy đủ trực tiếp từ chủ dự án qua kênh riêng. Giải nén, đọc HUONG_DAN_KHOI_PHUC.md rồi chạy các script kiểm tra, khôi phục SQL và khởi động trong gói đó. Gói đã được chuẩn bị với đường dẫn tương đối và database mới để không ghi đè dữ liệu sẵn có.

Không tải gói ZIP chứa dữ liệu thật lên kho công khai.

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

