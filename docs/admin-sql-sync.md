# Đồng bộ SQL của Admin và Ví ký gửi CoCo

Ngày kiểm tra: 22/11/2026. Website: http://localhost:5001.

## Phạm vi

Rà soát 46 handler ghi dữ liệu của Admin MVC và API: sản phẩm, ký gửi, kiểm định, đơn hàng, thanh toán, người dùng, phân quyền, ví, rút tiền và hỗ trợ. Flash Sale được bỏ qua theo xác nhận của người dùng.

Các chức năng hiện có đã dùng SQL Server qua Entity Framework Core. Không thay chúng bằng bộ nhớ, localStorage hoặc dữ liệu giao diện. Bản đang chạy tắt Preview và Demo; danh sách 22 sản phẩm đang bán khớp các ID trong SQL.

| Nhóm | Nguồn dữ liệu và bảo toàn lịch sử |
| --- | --- |
| Sản phẩm, ký gửi, ảnh, kiểm định | Products, ProductImages, Inspections; kiểm tra RowVersion; duyệt và đăng bán ghi cùng giao dịch; xóa mềm chuyển Removed, giữ ID và đơn hàng |
| Đơn hàng, thanh toán | Orders, CheckoutBatches, OrderCodeCounters; xác nhận, vận chuyển, thanh toán, hoàn tất và hủy lưu SQL; trạng thái sản phẩm và sổ tiền cập nhật qua giao dịch |
| Người dùng và quyền | AspNetUsers, AspNetUserRoles, AspNetUserClaims; thay đổi quyền, khóa và điểm tín nhiệm có giao dịch và AuditLogs |
| Ví và tiền rút | Wallets, WalletTransactions, WithdrawalRequests, PlatformCommissionEntries; giữ tiền khi gửi yêu cầu; chỉ xác nhận Transferred sau thao tác nhân viên xác nhận chuyển khoản |
| Hỗ trợ, thông báo | SupportTickets, SupportMessages, MarketplaceNotifications, AuditLogs; dữ liệu phản hồi và trạng thái đọc được lưu SQL |
| Mã giảm giá | CheckoutVouchers là nguồn đọc của giỏ hàng và thanh toán; bản nguồn chưa có màn hình Admin CRUD mã giảm giá riêng, không bổ sung một module ngoài phạm vi rà soát |

## Thay đổi

- Thêm bộ xử lý lỗi SQL chung cho MVC/API. Lỗi lưu, xung đột, khóa ngoại và mất kết nối trả về lỗi phù hợp, không báo thành công giả hoặc lộ câu lệnh SQL.
- Trang/API của người dùng đã đăng nhập và Admin dùng no-store để tải lại từ server.
- Ví giữ hai cột trên desktop; trên mobile xếp một cột theo cùng phong cách. Dropdown tài khoản có cuộn, chọn, sửa, xóa, đặt mặc định và thêm tài khoản.
- SavedBankAccounts lưu theo UserId. Backend kiểm tra quyền sở hữu và RowVersion; SQL chỉ cho một tài khoản mặc định trên mỗi người dùng.
- Ảnh QR đọc tại thiết bị bằng jsQR được đóng gói nội bộ. Backend kiểm tra cấu trúc VietQR, loại chuyển vào tài khoản, CRC, mã ngân hàng và số tài khoản. Tên chủ tài khoản được điền nếu QR có trường tên; nếu thiếu, người dùng nhập bổ sung.
- Có hai lựa chọn lưu/không lưu tài khoản khi gửi yêu cầu mới. Cả hai đều gửi yêu cầu. Lưu tài khoản và giữ số dư chạy chung giao dịch SQL, có khóa theo người dùng và chống gửi lặp bằng RequestKey.
- Yêu cầu rút lưu bản chụp ngân hàng, số tài khoản, tên chủ tài khoản. Thay đổi hoặc xóa tài khoản đã lưu không sửa lịch sử. Admin thấy tên chủ tài khoản từ bản chụp này.
- Ví chỉ lấy năm yêu cầu gần nhất. Trang /Profile/Withdrawals và API /api/v1/wallet/withdrawal-history tìm kiếm, lọc trạng thái và phân trang mười yêu cầu.
- Thẻ sản phẩm của chính tài khoản bán ẩn hai nút mua. Sản phẩm đang mở bán của chủ tài khoản ghi Chưa bán. Chưa bán: hồng; chờ kiểm định: vàng; đã có người đặt: xanh dương; đã bán: xanh lá; bị từ chối: đỏ; đã xóa: xám.
- Kích thước ảnh và quy tắc tên một dòng có dấu ... được giữ nguyên. Người mua khác vẫn có các nút mua theo thiết kế hiện tại.

## Kiểm thử

- 239 kiểm tra hồi quy đạt với COCO_RUN_SQL_TESTS=1, gồm các bài kiểm thử SQL riêng và kiểm tra MVC/API hiện có.
- Kiểm thử tích hợp mới gửi HTTP thực, rồi đọc lại bằng DbContext mới: ký gửi, sửa thông tin, duyệt sơ bộ, chọn ảnh bìa, kiểm định/đăng bán, đặt đơn, thanh toán, hoàn tất, cộng ví, xóa mềm, quyền/khóa tài khoản và phản hồi hỗ trợ.
- Kiểm thử ngân hàng: CRUD, một mặc định, RowVersion cũ, truy cập chéo người dùng, bản chụp lịch sử, hai lựa chọn lưu, gửi lặp, hai yêu cầu rút đồng thời, hoàn tiền khi từ chối, xác nhận chuyển khoản, tìm kiếm và phân trang.
- Gây lỗi SQL sau khi đã chèn tài khoản nhưng trước khi lưu yêu cầu: kiểm tra toàn bộ giao dịch hoàn tác, không đổi số dư, không tạo yêu cầu hoặc lưu tài khoản ngoài ý muốn.
- Kiểm thử QR hợp lệ, không có tên, tên Unicode, CRC sai, sai loại QR và dữ liệu thiếu/trùng.
- 20 kiểm tra bố cục/trình duyệt ở 320, 375, 390, 430 và 1440px đạt; không tràn ngang hoặc lỗi JavaScript. Thao tác dropdown, sửa/xóa/mặc định, ảnh QR, hai lựa chọn gửi và phân trang đã chạy bằng trình duyệt. Giá và tên trên thẻ Trang chủ vẫn thẳng hàng khi trộn sản phẩm của chủ tài khoản và người bán khác.
- Tất cả dữ liệu thử dùng database mới có tiền tố CoCoAdminSyncTests_ hoặc tiền tố riêng của bộ kiểm thử; không dùng database thật để tạo giao dịch thử. Database và cấu hình tài khoản thử đã được dọn.

## Cập nhật dữ liệu thật

- Đã tạo SQL backup COPY_ONLY có CHECKSUM và RESTORE VERIFYONLY thành công trước khi cập nhật.
- Migration 20261122111757_SavedWalletBankAccounts chỉ thêm SavedBankAccounts, cột BankAccountHolder có thể null và các chỉ mục.
- Đối chiếu hash trước/sau: đơn hàng, số dư, sổ ví, hoa hồng CoCo, sản phẩm, ảnh, yêu cầu rút và thông tin người dùng cũ không đổi.
- SQL không chứa tài khoản ngân hàng hoặc giao dịch mẫu được thêm từ quá trình thử.
- Sau cập nhật: trang chủ/Mua sắm/API sản phẩm và các tài nguyên mới trả 200; API ngân hàng và lịch sử yêu cầu trả 401 khi chưa đăng nhập; các trang cá nhân/Admin yêu cầu đăng nhập.

## Giới hạn của kết quả đồng bộ

Các trang đọc lại cùng dữ liệu SQL sau thao tác hoặc khi tải lại. Không bổ sung cơ chế tự tải lại mọi tab đang mở; luồng realtime tin nhắn/thông báo đang có được giữ nguyên.
