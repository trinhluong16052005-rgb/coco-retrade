# CoCo — cập nhật giao diện 30, 31, 33, 34 và 35

Ngày hoàn tất kiểm tra: 21/11/2026. Đã áp dụng vào ứng dụng đang chạy tại http://localhost:5001.

## Kết quả

| Mục | Thay đổi |
| --- | --- |
| 30 — Footer | Các line phân cách đang có đều dùng màu đen `#000000`, độ dày `1px`, gồm phía trên footer, giữa cột liên hệ với các cột nội dung, phía trên logo/bản quyền và các line trên tablet/mobile. Giữ nguyên kích thước, khoảng cách, nội dung và liên kết. |
| 31 — Chi tiết hỗ trợ | Căn giữa nội dung, rộng tối đa 960px. Tiêu đề, hội thoại và biểu mẫu cùng một trục. Bỏ chiều cao tối thiểu gây khoảng trắng ở hội thoại ngắn; hội thoại dài cuộn trong khung. Giữ tin của người gửi bên phải, phản hồi bên trái. Ô nhập gọn hơn; nút gửi nằm bên phải trên desktop và rộng theo form trên mobile. |
| 33 — Admin duyệt sơ bộ | Đối chiếu file HTML đã cung cấp. Chia ảnh và hồ sơ thành hai cột bằng nhau trên desktop; dùng ảnh lớn cùng dải thumbnail có thể chuyển ảnh. Đặt khối Bước 1 riêng bên dưới. Ba lựa chọn nằm ngang; chỉ hiện ô lý do của lựa chọn cập nhật hoặc từ chối. Mỗi thao tác vẫn gửi đến endpoint cũ với `ExpectedRowVersion`, quyết định và lý do tương ứng. Có xác nhận trước khi gửi. |
| 34 — Về CoCo | Xóa đủ sáu nội dung được chỉ định. Sáu nhãn mục chuyển sang Playfair Display đậm, màu hồng, 20–26px. Làm nổi bật 2030 và từng chữ TRUST. Rút gọn các đoạn lặp, bỏ thông tin liên hệ lặp với footer; giữ nút đến trang Liên hệ. Giữ hai lựa chọn mua sắm/ký gửi và các nút điều hướng. |
| 35 — Ba bước ký gửi | Hai trang dùng chung CSS Grid và cùng công thức nối theo tâm số. Ba cột bằng nhau; hai đoạn line đen 1px có chiều dài và khoảng cách đầu/cuối bằng nhau. Dùng subgrid để căn ảnh, tên bước và mô tả. Giữ ba cột trên desktop, tablet và mobile, điều chỉnh font/khoảng cách theo màn hình. |

Ảnh `community-003.webp` trước đây xuất hiện ở cả collage và thẻ Đông ấm áp. Nay chỉ xuất hiện ở thẻ hoạt động; collage được căn lại để kín bố cục. Toàn bộ tám ảnh cộng đồng gốc vẫn xuất hiện trên trang, mỗi ảnh một vị trí. Bốn hoạt động và liên kết Facebook tương ứng được giữ nguyên.

## File đã sửa/thêm

Đường dẫn tính từ `D:\Hoc Tap\31. QUẢN TRỊ DỰ ÁN\CoCoRetrade`:

- `src/CoCoReTrade.Web/Views/Shared/_Layout.cshtml`
- `src/CoCoReTrade.Web/Views/Support/Details.cshtml`
- `src/CoCoReTrade.Web/Areas/Admin/Views/Inspection/Review.cshtml`
- `src/CoCoReTrade.Web/Views/About/Index.cshtml`
- `src/CoCoReTrade.Web/Views/Home/Clearout.cshtml`
- `src/CoCoReTrade.Web/Views/Listing/Create.cshtml`
- `src/CoCoReTrade.Web/wwwroot/css/layout-polish.css`
- `src/CoCoReTrade.Web/wwwroot/css/about-esg.css`
- `src/CoCoReTrade.Web/wwwroot/css/listing-create.css`
- `src/CoCoReTrade.Web/wwwroot/css/clearout-editorial.css`
- `src/CoCoReTrade.Web/wwwroot/css/consign-steps.css` — mới
- `src/CoCoReTrade.Web/wwwroot/css/support-detail.css` — mới
- `src/CoCoReTrade.Web/wwwroot/css/inspection-review.css` — mới
- `src/CoCoReTrade.Web/wwwroot/js/inspection-review.js` — mới

## Kiểm tra

- Build Release mới: đạt, 0 lỗi, 0 cảnh báo. Bộ kiểm thử hiện có: 210/210 đạt.
- Kiểm tra lần lượt ở 1440, 1024, 768, 390 và 320px. Không tràn ngang; ảnh tải được; các cột/khối xếp đúng theo màn hình.
- So sánh footer trước/sau: kích thước, padding, margin, gap, nội dung và các liên kết giữ nguyên. Các line hiển thị đều đen 1px, kể cả vùng 1024px từng còn sót màu hồng.
- Đo vị trí số và line ở cả Clearout lẫn Listing/Create: ba cột bằng nhau; số, tên bước và mô tả thẳng hàng; hai đoạn line bằng nhau và cách đều hai số kề nhau.
- Admin: kiểm tra chuyển ảnh, ba lựa chọn, trạng thái ẩn/hiện và bật/tắt ô lý do, điều kiện nhập tối thiểu, hủy xác nhận và dữ liệu POST. Kiểm tra thêm trạng thái đã duyệt sơ bộ, đã kiểm định, quyền kiểm định viên và trường hợp chủ sản phẩm.
- Hỗ trợ: kiểm tra tin ngắn, hội thoại dài, dòng chữ dài, gửi trả lời, vị trí tin vừa gửi, xóa nội dung ô nhập sau khi gửi và yêu cầu đã đóng.
- Biểu mẫu ký gửi: hai công tắc, xác nhận bắt buộc, chọn/kéo thả/xóa ảnh, kích cỡ phụ kiện và định dạng giá tiếp tục hoạt động.
- Đối chiếu hash: controller hỗ trợ, controller kiểm định, model biểu mẫu, dịch vụ AdminProducts, dịch vụ Support và `support-ticket.js` giữ nguyên. Các khối kiểm định thực tế, đăng bán và từ chối sau kiểm định được giữ nguyên nội dung mã nguồn.

Kiểm tra trang công khai dùng ứng dụng đang chạy. Các thao tác gửi phản hồi và quyết định duyệt được thử trên host riêng với dữ liệu thử/phản hồi có kiểm soát, tránh gửi tin nhắn hoặc thông báo thật. Host thử không tải cấu hình production, không truy cập SQL và sẽ được tắt sau kiểm tra. Trang riêng tư trên ứng dụng chính vẫn yêu cầu đăng nhập.

Ảnh và kết quả kiểm tra lưu tại `C:\Users\laptop\Documents\ChatGPT\COCORETRADE\tmp\ui-30-35`. Không ghi nhận lỗi trong phạm vi các kiểm tra trên; dữ liệu SQL và cấu hình xác thực không được chỉnh sửa trong lượt này.
