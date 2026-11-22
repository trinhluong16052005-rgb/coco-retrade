# Dashboard Tổng quan CoCo

Trang: /Admin/Dashboard. API: /api/v1/admin/dashboard.

## Giao diện

Giữ Header và Sidebar, sáu thẻ thống kê và liên kết chức năng hiện có. Xóa toàn bộ mục Thao tác nhanh.

Thêm biểu đồ đường doanh thu và hoa hồng, biểu đồ cột đơn hàng mới và sản phẩm đã bán, biểu đồ tròn trạng thái đơn, biểu đồ ngang tình hình sản phẩm. Chart.js 4.5.1 được đóng gói nội bộ, không cần CDN khi sử dụng. Có tooltip và bảng số liệu để đối chiếu.

Bỏ các đoạn giải thích nhỏ dưới tiêu đề biểu đồ và bộ lọc, số liệu “Kỳ trước” trên thẻ và dòng “Không đổi”. Chỉ hiện mức tăng/giảm hoặc “Mới phát sinh” khi số liệu có thay đổi; vẫn giữ kỳ so sánh trong bộ lọc. Không thay đổi cách tính thống kê.

Font Playfair Display cho tiêu đề trang, thẻ thống kê, kết quả kinh doanh và biểu đồ; tiêu đề thẻ 18–20px, đậm 600. Open Sans cho nội dung phụ và số liệu. Chữ Dashboard dùng màu đen, icon dùng hồng CoCo trên nền hồng nhẹ. Sáu thẻ chia ba cột, hai hàng trên desktop; điện thoại dùng hai cột. Viền #E5E7EB, bo góc 10–12px và bóng nhẹ; nền trắng, không dùng gradient. Ba chỉ số kinh doanh nằm chung một khối có vạch phân cách nhẹ. Biểu đồ dùng màu tươi sáng và lưới xám nhạt. Hover/focus chỉ đổi nền thẻ sang hồng nhẹ. Giữ header và sidebar ở bố cục gốc, CSS không đổi vỏ trang hoặc các trang Admin khác. Bộ lọc gọn, cập nhật qua API; hỗ trợ 7 ngày, 30 ngày, 3 tháng, 12 tháng và khoảng ngày tùy chọn. Chuyển động 200–250ms, tắt khi người dùng chọn giảm chuyển động.

## Điều hướng từ biểu đồ

- Bấm đúng điểm doanh thu hoặc hoa hồng để mở Báo cáo doanh thu theo khoảng ngày của điểm đó, đến đúng chỉ số đã chọn.
- Bấm cột đơn hàng mới để mở danh sách đơn theo ngày đặt; bấm cột sản phẩm đã bán để mở các đơn hoàn tất, đã thanh toán theo ngày hoàn tất.
- Bấm từng lát biểu đồ trạng thái để mở đơn hàng theo trạng thái và khoảng ngày đang xem.
- Bấm thanh Đang bán để mở Sản phẩm, Chờ duyệt để mở Kiểm định; thanh Đã bán mở đơn hoàn tất, đã thanh toán. Giữ khoảng ngày của Dashboard.
- Chú thích trạng thái và bảng số liệu có liên kết tương ứng để thao tác bằng bàn phím. Vùng trống và tâm biểu đồ tròn không điều hướng. Liên kết tôn trọng quyền của tài khoản.
- Trang đích hiển thị khoảng ngày/trạng thái đã chọn, giữ bộ lọc khi tìm kiếm và có liên kết quay lại Tổng quan. Bộ lọc chạy trực tiếp trong SQL, không ghi dữ liệu hoặc đổi ID/liên kết chi tiết hiện có.

## Quy tắc thống kê

- Mọi ngày dùng UTC+7. Ngày bắt đầu bao gồm 00:00, ngày kết thúc bao gồm đến trước 00:00 ngày kế tiếp.
- Kỳ so sánh là khoảng liền trước có cùng số ngày. Kỳ trước bằng 0 và kỳ này có dữ liệu ghi Mới phát sinh; không tạo phần trăm tăng vô hạn.
- Hồ sơ sản phẩm, đơn và yêu cầu rút lọc theo ngày tạo, trạng thái tại thời điểm truy vấn. Đây là so sánh nhóm hồ sơ của hai kỳ, không phải bản chụp số tồn lịch sử.
- Sáu thẻ giữ nguyên tên: Ký gửi chờ xử lý, Đang mở bán, Cần giữ cho khách, Đang giao hàng, Đã bán thành công, Yêu cầu rút tiền chờ duyệt.
- Chờ xử lý và đang mở bán dùng trạng thái PendingApproval và Active. Đang giao hàng giữ định nghĩa cũ: đơn giao tận nơi còn PendingProcessing hoặc Shipping.
- Cần giữ cho khách giữ định nghĩa cũ: đơn nhận tại cửa hàng chưa thanh toán, còn hiệu lực, sản phẩm Reserved.
- Yêu cầu rút tiền chờ duyệt gồm Pending và Approved, theo quy tắc màn hình cũ.
- Doanh thu chỉ nhận Orders có Status Completed, PaymentStatus Paid và CompletedAtUtc thuộc kỳ. Doanh thu tiền hàng = ItemAmount − DiscountAmount, không cộng ShippingFee.
- Hoa hồng CoCo = CommissionAmount − DiscountAmount. Tiền người ký gửi vẫn được bảo toàn theo quy tắc hiện có.
- Đã bán thành công đếm ProductId phân biệt của các đơn hoàn tất, đã thanh toán theo ngày hoàn tất. Sản phẩm đã gỡ khỏi danh mục vẫn được tính trong lịch sử bán.
- Đơn hàng mới và biểu đồ tròn dùng ngày tạo đơn; biểu đồ tròn trình bày trạng thái hiện tại. Vì thế đơn đặt kỳ trước và hoàn tất kỳ này có thể tăng số sản phẩm đã bán mà không tăng số đơn hàng mới kỳ này.
- Thống kê trực tiếp từ SQL Server qua EF Core, gom theo ngày trong SQL; không JOIN ảnh hoặc các dòng sổ ví vào tổng bán hàng nên không nhân đôi doanh thu.
- Biểu đồ dùng ngày với khoảng tối đa 45 ngày, tuần với khoảng tối đa 120 ngày và tháng với khoảng dài hơn. Các khoảng trống được điền số 0 thực tế.
- API kiểm tra quyền ReadPending; quyền ManageOrders và ManageWallets quyết định phần dữ liệu đơn hàng và tài chính được trả về. Kiểm định viên không được nhận doanh thu hoặc hoa hồng.
- Khi tải API thất bại, giữ số liệu thành công gần nhất và thông báo lỗi; không đổi thành 0 hoặc báo cập nhật thành công.

## Kiểm thử

- Kiểm thử SQL riêng xác minh mốc chuyển ngày Việt Nam, ngày kết thúc, doanh thu sau giảm giá, hoa hồng ròng, đơn chưa thu tiền, đơn đang giao, đơn hủy, nhiều dòng sổ ví, đơn đặt kỳ trước hoàn tất kỳ này, tất cả bộ lọc và phân quyền.
- Kiểm thử trình duyệt tại 320, 375, 390, 430, 768, 1024, 1440 và 1920px: đủ sáu thẻ, bốn biểu đồ, không tràn ngang hoặc chồng chữ, số trên thẻ và biểu đồ khớp nhau.
- Đã thử tự cập nhật bộ lọc, khoảng ngày trống, lỗi API, phục hồi sau lỗi, tooltip, chế độ giảm chuyển động và xác minh CSS không tải tại trang Sản phẩm.
- Dữ liệu kiểm thử nằm trong database riêng có tiền tố CoCoAdminSyncTests_; được xóa sau kiểm thử. Dashboard không cần migration và không ghi dữ liệu nghiệp vụ.
- Kiểm thử SQL đối chiếu số lượng ở từng trạng thái và khoảng ngày với trang đích, bao gồm đơn tạo kỳ trước nhưng hoàn tất kỳ này, sản phẩm chờ duyệt/đang bán, khoảng báo cáo tùy chọn và phân quyền.
- Tổng cộng 249 kiểm tra hồi quy đạt khi bật kiểm thử SQL. Các tài nguyên Dashboard trả 200, API yêu cầu đăng nhập trả 401 khi chưa xác thực. Không cần migration cho các thay đổi này.
