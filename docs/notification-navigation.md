# Điều hướng thông báo CoCo

Trang: `/Profile/Notifications`. Nút Xem chi tiết đi qua `/Profile/OpenNotification/{id}`; thông báo phải thuộc tài khoản đang đăng nhập.

| Nghiệp vụ | Trang đích |
| --- | --- |
| Đơn hàng | Admin có quyền quản lý mở `/Admin/Orders/Details/{id}`; người mua/người ký gửi mở `/Orders/Details/{id}` của mình. |
| Thương lượng | `/Offers/Details/{id}`, chỉ dành cho hai bên thương lượng. Đơn hàng có cả OrderId và OfferId vẫn ưu tiên chi tiết đơn. |
| Tin nhắn | `/Messages/Details/{id}`, kiểm tra thành viên hội thoại; không mở trang sản phẩm thay hội thoại. |
| Ký gửi cần cập nhật | Người sở hữu mở `/Listing/Edit/{id}` khi trạng thái hiện tại vẫn cho phép sửa. |
| Các cập nhật ký gửi khác | Chủ sản phẩm mở Tủ đồ và đến đúng món; món đã gỡ mở Tủ đồ không kèm neo không còn tồn tại. |
| Phiếu kiểm định | Nhân viên có quyền mở Review khi còn chờ duyệt; sau khi kết thúc chỉ Admin có quyền quản lý mở Details. |
| Hỗ trợ | Chủ yêu cầu mở `/Support/Details/{id}`; nhân viên có quyền mở `/Admin/HelpDesk/Details/{id}`. |
| Rút tiền | Người sở hữu mở lịch sử yêu cầu, lọc đúng request ID. Admin mở đúng thẻ xét duyệt trong Ví và rút tiền. |
| Tương tác | Mở sản phẩm nếu còn quyền xem; thông báo theo dõi tủ đồ mở Tủ đồ. |
| Giỏ hàng | `/Cart`, dù thông báo kèm ProductId. |

Điều hướng kiểm tra cả quyền trong phiên đăng nhập và quyền hiện tại trong SQL trước khi gửi đến trang nhân viên. Trạng thái sản phẩm và quyền sở hữu được đọc lại từ SQL mỗi lần mở. Hồ sơ không còn tồn tại hoặc tài khoản không còn quyền được đưa về danh sách nghiệp vụ phù hợp, không mở hồ sơ người khác.

Thông báo hỗ trợ và rút tiền mới lưu SupportTicketId/WithdrawalRequestId trong SQL bằng khóa ngoại có thể rỗng. Migration NotificationWorkflowReferences chỉ thêm cột, index và khóa ngoại; giữ nguyên lịch sử thông báo và dữ liệu giao dịch. Với thông báo cũ, chỉ dùng GUID được xác minh hoặc mốc sự kiện khớp duy nhất để mở trực tiếp; không đoán hồ sơ từ câu chữ hay tên sản phẩm. Nếu không xác định được duy nhất, mở danh sách đúng nghiệp vụ.

Mở thông báo chỉ cập nhật IsRead. Thay tên sản phẩm/mã đơn dùng lúc hiển thị không được ghi đè lên Message gốc. Đánh dấu tất cả đã đọc, lịch sử và bộ đếm chuông giữ nguyên cơ chế hiện có.

Kiểm thử dùng SQL database riêng: đủ vai trò Admin, kiểm định viên, người mua, người ký gửi; kiểm tra URL đích, trạng thái đã đọc, quyền truy cập chéo, thông báo cũ và dữ liệu giao dịch không thay đổi khi mở liên kết. Trình duyệt kiểm tra bấm Xem chi tiết trên desktop và điện thoại.
