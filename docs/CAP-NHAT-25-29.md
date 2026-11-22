# Cập nhật CoCo: thẻ gợi ý, Đồ đã lưu, Tủ đồ và trang ký gửi

Ngày kiểm tra: 21/11/2026. Bản đang chạy: http://localhost:5001.

## Những phần đã sửa

| Yêu cầu | Kết quả |
| --- | --- |
| 25 — Hai mục gợi ý ở chi tiết sản phẩm | Ảnh cùng tỷ lệ 3:4, `object-fit: cover`, bỏ khoảng đệm ảnh. Thương hiệu một dòng, tên tối đa hai dòng, kích cỡ và tình trạng có vùng hiển thị đồng nhất. Giá và hai nút mua thẳng hàng. Desktop từ 1200px hiển thị năm cột; màn hình nhỏ hơn dùng bốn, ba, hai hoặc một cột phù hợp. |
| 26 — Đồ đã lưu | Thêm checkbox, Chọn tất cả, Bỏ chọn và Xóa khỏi yêu thích. Xóa nhiều có xác nhận; bỏ lưu bằng trái tim gỡ thẻ ngay. Số lượng, trạng thái chọn và nội dung khi danh sách trống cập nhật ngay. Lỗi kết nối hoặc lỗi máy chủ giữ nguyên danh sách và báo lỗi. |
| 27 — Tủ đồ của tôi | Thêm checkbox và thao tác chọn/xóa nhiều có xác nhận. Chỉ mở chọn món chưa kiểm định chính thức và chưa từng đăng bán; món đã duyệt sơ bộ nhưng chưa kiểm định vẫn được xóa. Máy chủ kiểm tra lại chủ sở hữu và trạng thái của toàn bộ lượt chọn. |
| 28 — Trang giới thiệu ký gửi | Tăng độ tối banner và phần liên hệ; thu gọn khoảng cách, ảnh ba bước, phần lợi ích và Q&A. Bỏ hai dòng phụ đã chỉ định và thanh điều hướng dưới banner. Giữ ID các section, câu hỏi, tỷ lệ 65% và các liên kết hiện có. |
| 29 — Biểu mẫu và quy trình ký gửi | Thêm viền đen mảnh cho vùng tải ảnh và hai khối thông tin/giá; nối các bước bằng line đen mảnh. Tăng độ rõ của tiêu đề, nhãn và nút gửi. Giữ nguyên trường thông tin, hai công tắc và điều kiện xác nhận bắt buộc. |

Xóa khỏi tủ chuyển sản phẩm sang trạng thái `Removed`, giữ ảnh và lịch sử trong SQL. Không cần migration. Xóa yêu thích chỉ tác động bản ghi thuộc tài khoản đang đăng nhập. Cả hai thao tác xóa nhiều dùng giao dịch SQL; nếu một món không hợp lệ hoặc dữ liệu thay đổi trong lúc xử lý, toàn bộ lượt xóa được hoàn tác.

## File mã nguồn thay đổi trong lượt này

Đường dẫn tính từ `D:\Hoc Tap\31. QUẢN TRỊ DỰ ÁN\CoCoRetrade`.

| Nhóm | File |
| --- | --- |
| Quy tắc và dịch vụ | `src/CoCoReTrade.Domain/Marketplace.cs`; `src/CoCoReTrade.Application/Products.cs`; `src/CoCoReTrade.Application/Social.cs`; `src/CoCoReTrade.Infrastructure/ProductRepository.cs` |
| Endpoint web | `src/CoCoReTrade.Web/Controllers/ListingController.cs`; `src/CoCoReTrade.Web/Controllers/SocialController.cs` |
| Danh sách và chi tiết | `src/CoCoReTrade.Web/Views/Shared/_ProductGrid.cshtml`; `src/CoCoReTrade.Web/Views/Shop/Details.cshtml`; `src/CoCoReTrade.Web/Views/Profile/Saved.cshtml`; `src/CoCoReTrade.Web/Views/Listing/Closet.cshtml` |
| CSS và JavaScript | `src/CoCoReTrade.Web/wwwroot/css/product-collections.css` (mới); `src/CoCoReTrade.Web/wwwroot/js/product-collections.js` (mới); `src/CoCoReTrade.Web/wwwroot/js/site.js`; `src/CoCoReTrade.Web/Views/Shared/_Layout.cshtml` |
| Ký gửi | `src/CoCoReTrade.Web/Views/Home/Clearout.cshtml`; `src/CoCoReTrade.Web/wwwroot/css/clearout-editorial.css`; `src/CoCoReTrade.Web/wwwroot/css/listing-create.css` |
| Chế độ xem trước | `src/CoCoReTrade.Web/Preview/PreviewProductService.cs` — bổ sung phương thức mới, tiếp tục chặn thay đổi dữ liệu ở chế độ chỉ đọc |
| Kiểm thử mới | `tests/CoCoReTrade.Tests/CollectionRemovalTests.cs`; `tests/CoCoReTrade.Tests/CollectionRemovalSqlTests.cs` |
| Cập nhật lớp giả lập theo interface | `tests/CoCoReTrade.Tests/AccessorySizeTests.cs`; `tests/CoCoReTrade.Tests/ProductCategoryRecommendationTests.cs`; `tests/CoCoReTrade.Tests/ProductRulesTests.cs`; `tools/CatalogImport/tests/CatalogValidationTests.cs` |

## Kiểm tra đã thực hiện

- Build Release ứng dụng web: thành công, không lỗi hoặc cảnh báo.
- Bộ kiểm thử chính: **210/210 đạt**. Bộ kiểm thử công cụ nhập liệu: **68/68 đạt**.
- Chạy riêng kiểm thử SQL thực tế trong database tạm `CoCoCollectionTests_<GUID>`: đạt. Kiểm tra quyền sở hữu, món đã kiểm định, xung đột `RowVersion`, hoàn tác cả lượt xóa và chỉ xóa yêu thích của đúng tài khoản. Database tạm được xóa sau kiểm thử.
- Kiểm tra thẻ gợi ý, Đồ đã lưu và Tủ đồ ở 1920, 1440, 1200, 1024, 768, 390 và 320px. Không tràn ngang; ảnh tải được; các vùng tên, thương hiệu, kích cỡ, tình trạng, giá và nút mua thẳng hàng theo từng hàng.
- Kiểm tra thao tác chọn/bỏ chọn, hủy xác nhận, xóa thành công, phản hồi lỗi, xóa bằng trái tim, danh sách trống và bộ đếm trên desktop/mobile. Chặn xóa nhiều khi một thao tác cập nhật trái tim còn đang chờ để tránh yêu cầu chồng nhau.
- Luồng thao tác giao diện dùng host tách biệt, dữ liệu thử và phản hồi API được kiểm soát. Endpoint thật được kiểm tra riêng qua MVC có xác thực/CSRF và qua repository SQL thực tế; không tạo hoặc xóa tài khoản/sản phẩm thử trong database chính.
- Kiểm tra thêm trang chi tiết với sản phẩm thật trên ứng dụng đang chạy ở 1440, 1024, 768, 390 và 320px. Hai trang cá nhân vẫn yêu cầu đăng nhập. CSS mới không được nạp vào trang Chủ, Mua sắm, Về CoCo hoặc Liên hệ.
- Trang Clearout đạt kiểm tra ở sáu kích thước màn hình; biểu mẫu ký gửi đạt ở năm kích thước. Hai công tắc, xác nhận bắt buộc, tải/kéo thả/xóa ảnh, giới hạn mười ảnh, kích cỡ phụ kiện và định dạng giá vẫn hoạt động.

## Ghi nhận còn lại

Không ghi nhận lỗi trong các chức năng vừa sửa qua các kiểm tra trên. Công cụ phụ `CatalogImport` còn cảnh báo bảo mật NuGet đối với `System.Security.Cryptography.Pkcs` phiên bản `9.0.11` (NU1902/NU1903). Gói phụ thuộc này chưa được thay đổi trong lượt sửa giao diện và chức năng xóa.

Ảnh và kết quả kiểm tra lưu tại `C:\Users\laptop\Documents\ChatGPT\COCORETRADE\tmp\product-collections` và `C:\Users\laptop\Documents\ChatGPT\COCORETRADE\tmp\listing-editorial`.
