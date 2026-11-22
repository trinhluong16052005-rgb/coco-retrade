# Cập nhật 10 hạng mục CoCo ReTrade — 19/11/2026

Đã triển khai trên bản chính `http://localhost:5001` và bản demo `http://localhost:5002`. Build Release không có lỗi hoặc cảnh báo. Bộ kiểm thử website đạt **138/138**, đã bật các bài tích hợp HTTP/SQL trên database thử riêng.

## Kết quả theo yêu cầu

| Mục | Thay đổi |
| --- | --- |
| 1. Bộ lọc | Bỏ trường “Nhận hàng” và bộ đếm tương ứng. Giữ tìm kiếm, danh mục, thương hiệu, kích cỡ, tình trạng, khoảng giá, sắp xếp và bảng lọc cạnh bên. Phương thức nhận hàng khi đặt hàng vẫn hoạt động. |
| 2. Quản trị sản phẩm | Icon thùng rác, chọn từng sản phẩm hoặc tất cả mục có thể xóa, xác nhận đơn lẻ/hàng loạt, ảnh thu nhỏ 64 × 64 cạnh tên. Kiểm tra quyền Admin, CSRF, phiên bản dữ liệu và giao dịch trước khi ghi. |
| 2. Tên quản trị | Đổi đúng tài khoản Admin đang mang tên “Lê Minh Quân” thành “Quản Lý CoCo” trong SQL bản chính, có audit. Không đổi email, mật khẩu, quyền hoặc dấu phiên đăng nhập. Liên kết trên header giữ tên “Quản trị”. |
| 3. Người dùng | Icon thùng rác, checkbox và chọn tất cả, xác nhận xóa đơn lẻ/hàng loạt. Xóa theo cơ chế lưu trữ tài khoản và thu hồi truy cập để giữ lịch sử đơn/ví. Không cho xóa Admin, tài khoản hiện tại, system hoặc tài khoản còn sản phẩm/giao dịch/số dư chưa xử lý. |
| 4. Banner | Dùng đúng `D:\DỰ ÁN\ẢNH COCO\CỬA HÀNG COCO.png`. Tạo WebP rộng 640, 960, 1448 px, giữ tỷ lệ 4:3 và toàn bộ ảnh. Desktop đặt chữ cạnh ảnh; mobile xếp dọc. Không còn video trong banner. |
| 5. Thẻ sản phẩm | Trái tim dùng đường vẽ trực tiếp, tô kín màu đỏ hồng thương hiệu khi lưu và trở lại nét viền khi bỏ lưu. Giữ lưu trạng thái qua API/SQL, đồng bộ các thẻ cùng sản phẩm. Giảm khoảng cách giữa ảnh, tên, kích cỡ, tình trạng, giá và nút mua. |
| 6. Gợi ý | Thêm “Sản phẩm bạn có thể thích” tại chi tiết và thanh toán. Áo/quần/váy/set đồ ưu tiên phụ kiện, phụ kiện ưu tiên quần áo. Chỉ lấy sản phẩm còn bán, loại sản phẩm hiện tại, sản phẩm đã chọn/trong giỏ và hàng của chính người xem. Giữ phần sản phẩm tương tự khi có kết quả riêng. |
| 7. Danh mục | Thêm “Set đồ”. “Phụ kiện” là nhóm cha bao gồm túi xách; lọc riêng Túi xách vẫn có. Đồng bộ truy vấn SQL, validation, ký gửi, kiểm định, preview và nhãn. Giữ mã danh mục SQL cũ, không cần migration hoặc viết lại sản phẩm hiện có. |
| 8. Thanh danh mục | Chỉ render trên `/Shop` với action Index. Ẩn khỏi trang chủ, chi tiết, ký gửi, tài khoản, thanh toán và các trang khác. Hiển thị Set đồ và quan hệ Phụ kiện/Túi xách. Footer cũng được đồng bộ. |
| 9. Đăng nhập/đăng ký | Popup chuyển giữa hai form, hiện/ẩn mật khẩu, giao diện mobile, lỗi validation trong popup. Giữ email/số điện thoại, Google, OTP và CSRF. Return URL chỉ chấp nhận địa chỉ nội bộ. Nếu JavaScript không hoạt động, trang form đầy đủ vẫn dùng được. |
| 10. Khoảng cách | Giảm padding cuối khu sản phẩm, margin trên nút xem tất cả và padding đầu khu thương hiệu; giữ logo chạy và các phần khác. |

Xóa sản phẩm chuyển sang trạng thái Removed. Xóa tài khoản lưu dấu archive, khóa đăng nhập, đổi security stamp và ẩn khỏi danh sách quản lý. Đây là xóa khỏi hoạt động website, không hủy lịch sử SQL. Bản thân đợt triển khai không xóa sản phẩm hoặc tài khoản thật.

Tài khoản archive bị chặn ở đăng nhập, Google, gửi/xác thực OTP, xác nhận email và khôi phục mật khẩu. Bulk thực hiện trong transaction; một mục không hợp lệ khiến toàn bộ đợt bị hủy, tránh xóa dở dang.

## Các file mã nguồn trong phạm vi

- Domain: `ProductCategories.cs` mới; `Marketplace.cs`, `ProductSizing.cs`.
- Application: `AdminProducts.cs`, `AdminUsers.cs`, `ProductRecommendations.cs`.
- Infrastructure: `AdminProductRepository.cs`, `AdminUserRepository.cs`, `AccountService.cs`, `AccountOtpService.cs`, `ProductRepository.cs`.
- Web controllers/services: `Controllers/AccountController.cs`, `Controllers/ShopController.cs`, `CheckoutPageService.cs`, `Areas/Admin/Controllers/ProductsController.cs`, `Areas/Admin/Controllers/UsersController.cs`.
- Danh mục và preview: `Models/UiText.cs`, `Preview/PreviewProductService.cs`, `Views/Shared/_CategoryOptions.cshtml`, các form `Views/Listing/Create.cshtml`, `Edit.cshtml`, `Areas/Admin/Views/Inspection/Review.cshtml` và `Views/Home/Clearout.cshtml`.
- Trang mua sắm/trang chủ: `Views/Home/Index.cshtml`, `Views/Shop/Index.cshtml`, `Details.cshtml`, `Views/Shared/_Layout.cshtml`, `_ShopFilters.cshtml`, `_ProductGrid.cshtml`, `_Checkout.cshtml`.
- Quản trị: `Areas/Admin/Views/Products/Index.cshtml`, `Areas/Admin/Views/Users/Index.cshtml`, `Views/Shared/_AdminTrashIcon.cshtml`.
- Popup tài khoản: `Views/Shared/_AuthModal.cshtml`, `_GoogleSignIn.cshtml`, `Views/Account/Login.cshtml`, `Register.cshtml`, `_LoginForm.cshtml`, `_RegisterForm.cshtml`, `CheckEmail.cshtml`, `ConfirmOtp.cshtml`.
- Giao diện: `wwwroot/css/coco-update.css`, `auth-modal.css`, `admin-bulk.css`; `wwwroot/js/site.js`, `auth-modal.js`, `admin-bulk.js`.
- Ảnh: ba biến thể `wwwroot/images/coco/coco-storefront-banner*.webp`.
- Kế hoạch tạo tài khoản mới: `tools/CatalogAccounts/AccountPlan.cs` đồng bộ tên quản trị.
- Kiểm thử: `AdminBulkIntegrationTests.cs`, `AuthModalFlowTests.cs`, `ProductCategoryRecommendationTests.cs`, `WebCheckoutIntegrationTests.cs`.

Repo có các thay đổi từ những đợt trước; danh sách trên mô tả nhóm thay đổi của yêu cầu hiện tại, không phải toàn bộ `git status`.

## Kiểm chứng

- Build Release: 0 lỗi, 0 cảnh báo.
- Website: 138 bài đạt, gồm tích hợp SQL/HTTP cho bulk delete, quyền/CSRF, rowversion, tính nguyên tử, archive/OTP, favorite, giỏ hàng, voucher, đơn hàng và luồng popup.
- Công cụ tài khoản: 11/11 bài đạt sau khi đồng bộ tên quản trị; kết quả tại `tests/coco10-accounts.trx`.
- `node --check` đạt cho `site.js`, `auth-modal.js`, `admin-bulk.js`, `checkout.js`.
- HTTP kiểm tra 8 trang × 2 cổng: 200. Banner ảnh, điều kiện hiện thanh danh mục, bỏ bộ lọc nhận hàng đúng kết quả render.
- Asset ảnh/CSS/JS trong trang chủ và mua sắm bản chính tải thành công.
- Fragment đăng nhập/đăng ký có CSRF, đúng loại form và không chứa layout đầy đủ.
- Hai localhost và đường dẫn Cloudflare hiện tại: form Google tạo đúng callback `/signin-google`; form đăng nhập trống trả validation trong popup.
- Tên Admin trong SQL đã đọc lại và khớp “Quản Lý CoCo”.

Chứng cứ nằm tại `.artifacts/coco10-20261119/`, gồm `tests/coco10-all-final.trx`, `http-pages-assets.json`, `auth-http-runtime.json`, nguồn banner và snapshot tên quản trị trước khi đổi.

## Giới hạn kiểm chứng

CSS đã có bố cục desktop/mobile, nhưng chưa đối chiếu trực quan bằng trình duyệt ở các kích thước: kết nối báo `The browser access host is unavailable`. Coolmate chưa truy cập được để so trực quan, nên không khẳng định popup sao chép chính xác mẫu. Kiểm thử OTP trong đợt này dùng dịch vụ giả trên host thử, không gửi email cho khách. Đăng nhập Google thật sau thay đổi popup vẫn cần người dùng hoàn tất chọn tài khoản trong trình duyệt.

Danh mục Set đồ đã sẵn sàng, không tự tạo thêm hàng tồn. Không thay cấu hình SMTP, OAuth, database connection, không reset database và không commit/push.
