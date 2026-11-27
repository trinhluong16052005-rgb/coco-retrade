# COCO RETRADE — Hướng dẫn cài đặt và sử dụng

Tài liệu dành cho người nhận dự án qua GitHub. Bạn có thể đọc và chia sẻ trực tiếp trang này, không cần tải tài liệu riêng.

**GitHub lưu mã nguồn và bản bàn giao. Để sử dụng website, người nhận cần cài và chạy trên máy.** Sau khi chạy, mở `http://localhost:5001` trên chính máy đó. Link GitHub không phải địa chỉ website đã được triển khai trên Internet.

## Chọn phần bạn cần

- [1. Cài đặt website trên Windows](#cai-dat)
- [2. Đăng nhập và các mục dùng chung](#tai-khoan)
- [3. Hướng dẫn cho người mua](#nguoi-mua)
- [4. Hướng dẫn cho người ký gửi](#nguoi-ky-gui)
- [5. Hướng dẫn cho Admin](#admin)
- [6. Xử lý lỗi thường gặp](#loi-thuong-gap)

<a id="cai-dat"></a>
## 1. Cài đặt website trên Windows

Người cài đặt thực hiện phần này một lần. Những lần sử dụng tiếp theo chỉ cần khởi động website theo bước 5.

### Bước 1. Cài phần mềm cần thiết

| Phần mềm | Dùng để làm gì |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Chạy mã nguồn website. Chọn **SDK**, không chỉ cài Runtime. |
| [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads), bản 2022 hoặc mới hơn | Lưu sản phẩm, tài khoản, đơn hàng và giao dịch. Bản Express dùng được. |
| [SQL Server Management Studio — SSMS](https://learn.microsoft.com/en-us/ssms/install/install) | Mở và khôi phục dữ liệu SQL bằng giao diện. |

Nếu máy đã có đủ phần mềm, bỏ qua bước cài lại. Cần Internet trong lần chạy đầu để tải các gói của dự án.

### Bước 2. Tải bản đầy đủ từ GitHub

1. Mở [repo COCO RETRADE](https://github.com/trinhluong16052005-rgb/coco-retrade).
2. Vào **Releases**, mở [bản bàn giao đầy đủ](https://github.com/trinhluong16052005-rgb/coco-retrade/releases/tag/ban-giao-20261122).
3. Trong **Assets**, tải **CoCoReTrade_BanGiao_20261122.zip**, dung lượng khoảng 303 MB.
4. Bấm chuột phải tệp ZIP, chọn **Extract All / Giải nén tất cả**.
5. Mở thư mục có các tệp bên dưới. Mọi lệnh tiếp theo đều chạy tại thư mục này.

```text
CoCoReTrade_BanGiao/
├── CoCoRetrade/             Mã nguồn, ảnh và video
├── Database/
│   └── CoCoReTrade.bak      Bản sao dữ liệu SQL
├── CHAY_COCO.ps1
├── KHOI_PHUC_SQL.ps1
└── KIEM_TRA_GOI.ps1
```

**Muốn có đủ sản phẩm, dữ liệu, ảnh và video thì tải đúng tệp trên.** Nút **Code → Download ZIP** và các tệp **Source code** chỉ tải mã nguồn, không kèm bản dữ liệu đầy đủ.

Gói đầy đủ là bản chụp ngày **22/11/2026**; các thay đổi trên máy chủ sau ngày đóng gói không tự đi vào ZIP này.

### Bước 3. Kiểm tra gói tải về

Trong thư mục vừa giải nén, bấm chuột phải chọn **Open in Terminal / Mở trong Terminal**, dùng PowerShell và chạy:

```powershell
powershell -NoProfile -File .\KIEM_TRA_GOI.ps1
```

Kết quả có dòng **All checksums match** nghĩa là các tệp khớp bản đóng gói. Nếu báo thiếu hoặc thay đổi tệp ngay sau giải nén, tải và giải nén lại ZIP gốc.

### Bước 4. Khôi phục dữ liệu SQL bằng SSMS

1. Mở **SSMS**, đăng nhập SQL Server bằng **Windows Authentication**. Ghi lại **Server name** đang dùng; với SQL Server Express thường là `.\SQLEXPRESS`.
2. Trong danh sách bên trái, bấm chuột phải **Databases → Restore Database**.
3. Chọn **Device → … → Add**, tìm tệp `Database\CoCoReTrade.bak` trong thư mục vừa giải nén.
4. Đặt **Destination database** là **CoCoReTrade_BanGiao**. Nếu tên này đã tồn tại và cần khôi phục một bản khác, chọn tên mới.
5. Trong mục **Files**, kiểm tra đường dẫn tệp Data và Log thuộc SQL Server trên máy nhận; chọn lại nếu còn đường dẫn máy cũ.
6. Không chọn **Overwrite the existing database**. Bấm **OK**, đợi thông báo khôi phục thành công.

**Khôi phục chỉ cần làm một lần.** Không khôi phục đè lại sau mỗi lần dùng vì bạn cần giữ những thay đổi vừa thực hiện.

### Bước 5. Khởi động website

Quay lại PowerShell ở thư mục có `CHAY_COCO.ps1`, chạy:

```powershell
powershell -NoProfile -File .\CHAY_COCO.ps1 -SqlInstance '.\SQLEXPRESS' -DatabaseName 'CoCoReTrade_BanGiao'
```

- Nếu **Server name** ở bước 4 khác `.\SQLEXPRESS`, thay đúng tên đó trong lệnh.
- Nếu đặt tên database khác, thay phần `CoCoReTrade_BanGiao` bằng tên đã khôi phục.
- Đợi cửa sổ báo website đã chạy, rồi mở **http://localhost:5001** bằng Chrome hoặc Edge.
- Giữ cửa sổ PowerShell mở khi dùng website. Nhấn **Ctrl + C** để dừng.

**Lần sau:** mở lại thư mục này và chạy lại lệnh khởi động. Dữ liệu đã lưu trong SQL vẫn được giữ.

### Bước 6. Chuẩn bị tài khoản

- Dùng tài khoản do chủ dự án cấp riêng. Bản SQL giữ tài khoản và quyền, nhưng không cung cấp danh sách mật khẩu.
- Người mua và người ký gửi dùng cùng loại tài khoản **Người dùng**; một tài khoản có thể làm cả hai việc.
- Tài khoản **Admin** cần quyền quản trị. Đăng ký tài khoản thông thường không tự có quyền Admin.
- Không ghi mật khẩu tài khoản, mật khẩu email hoặc khóa dịch vụ vào repo GitHub.

**Nếu cần đăng ký mới hoặc lấy lại mật khẩu:** người cài đặt cần cấu hình email gửi mã OTP trên máy nhận. Có thể dùng Gmail của cửa hàng như sau:

1. Bật xác minh hai bước cho Gmail gửi thư, rồi tạo **App Password / Mật khẩu ứng dụng** tại [trang Google](https://myaccount.google.com/apppasswords).
2. Trong thư mục bàn giao, chạy lệnh dưới đây và nhập Gmail cùng mật khẩu ứng dụng tại lời nhắc. Không dùng mật khẩu đăng nhập Gmail.

   ```powershell
   powershell -NoProfile -File .\CoCoRetrade\tools\ConfigureCoCoAuth.ps1 -EmailOnly
   ```

3. Khởi động lại website, thử đăng ký bằng email bạn kiểm soát và kiểm tra Hộp thư đến hoặc Spam.

Đăng nhập Google cần cấu hình riêng; xem [hướng dẫn OTP và Google](docs/CAU-HINH-OTP-GOOGLE.md). Nếu chưa cấu hình các dịch vụ này, dùng tài khoản đã được cấp để đăng nhập bằng mật khẩu.

<a id="tai-khoan"></a>
## 2. Đăng nhập và các mục dùng chung

1. Mở website đang chạy, chọn **Tài khoản → Đăng nhập**.
2. Nhập email hoặc số điện thoại và mật khẩu. Nếu đăng ký mới, điền thông tin rồi nhập mã xác thực gửi qua email.
3. Sau khi đăng nhập, mở **Tài khoản của tôi** để vào đơn hàng, tủ đồ, ví, địa chỉ và hỗ trợ.

Trên điện thoại, thanh dưới có **Trang chủ, Mua sắm, Ký gửi, Tin nhắn, Tài khoản**.

| Bạn muốn làm gì | Mở ở đâu |
|---|---|
| Xem cập nhật đơn hàng, ký gửi hoặc ví | Biểu tượng chuông hoặc **Tài khoản → Thông báo**. Chọn **Xem chi tiết** trong thông báo. |
| Xóa số thông báo chưa đọc | Bấm **Đánh dấu tất cả đã đọc**. Lịch sử thông báo vẫn còn. |
| Trò chuyện với người mua hoặc người bán | **Tin nhắn**. |
| Hỏi nhân viên CoCo | **Tài khoản → Trợ giúp CoCo**, tạo yêu cầu rồi theo dõi phản hồi. |
| Đổi mật khẩu hoặc ảnh đại diện | **Tài khoản của tôi**. |

<a id="nguoi-mua"></a>
## 3. Hướng dẫn cho người mua

### Tìm và chọn sản phẩm

1. Mở **Mua sắm**, tìm theo tên hoặc dùng bộ lọc.
2. Bấm vào sản phẩm để xem ảnh, giá, kích cỡ, tình trạng và mô tả.
3. Bấm biểu tượng **trái tim** nếu muốn lưu. Xem lại tại **Tài khoản → Đồ đã lưu**.
4. Bấm **Nhắn người bán** nếu cần hỏi thêm.

Trong **Đồ đã lưu**, bấm lại trái tim để bỏ lưu một món; muốn bỏ nhiều món, chọn checkbox và bấm **icon thùng rác**, sau đó xác nhận.

### Mua hàng

1. Bấm **Mua ngay**, hoặc **Thêm vào giỏ** rồi mở **Giỏ hàng** và chọn các món muốn đặt.
2. Nhập tên người nhận, số điện thoại và chọn cách nhận hàng:
   - **Giao tận nơi:** chọn tỉnh/thành phố, nhập phường/xã và địa chỉ cụ thể. Phí giao hàng tự cập nhật: **Đà Nẵng 20.000đ**, các tỉnh/thành khác **35.000đ**.
   - **Nhận tại CoCo:** chọn lịch nhận theo hướng dẫn trên biểu mẫu; không tính phí giao hàng.
3. Nếu có mã giảm giá, chọn mã sẵn có hoặc nhập mã rồi bấm **Áp dụng**. Mỗi lần đặt chỉ áp dụng một mã hợp lệ.
4. Kiểm tra sản phẩm, phí giao hàng, giảm giá và tổng tiền; bấm **Đặt hàng**.
5. Thanh toán khi nhận hàng hoặc tại cửa hàng theo cách nhận đã chọn.

Chỉ thêm vào giỏ hoặc thương lượng chưa giữ sản phẩm cho bạn; cần đặt đơn thành công. Bạn không thể mua sản phẩm của chính tài khoản mình.

### Đề xuất giá và theo dõi đơn

- Nếu sản phẩm nhận thương lượng, bấm **Đề xuất mức giá của bạn**, nhập giá trong khoảng cho phép rồi gửi. Xem phản hồi tại **Tài khoản → Đề xuất giá**.
- Khi hai bên đồng ý, bấm **Tiến hành đặt hàng** trước hạn hiển thị; đồng ý giá chưa thay cho việc đặt đơn.
- Xem đơn tại **Tài khoản → Đơn hàng**; mở đơn để xem mã `ORD-YYYYMMDD-XXX`, trạng thái và các mốc xử lý.
- Nếu nút **Hủy đơn trước khi xử lý** còn hiện, bạn có thể bấm để hủy. Khi đơn đã được xử lý, dùng **Liên hệ hỗ trợ**.

<a id="nguoi-ky-gui"></a>
## 4. Hướng dẫn cho người ký gửi

### Tạo yêu cầu ký gửi

1. Đăng nhập, mở **Ký gửi → Ký gửi ngay**.
2. Chọn hoặc kéo thả **3–10 ảnh thật**, rõ nét; có thể xem trước, thêm hoặc bỏ ảnh.
3. Điền tên sản phẩm, thương hiệu, danh mục, kích cỡ, độ mới, màu sắc, mô tả, giá gốc và giá mong muốn.
4. Chọn có cho phép người mua đề xuất giá hay không; bật xác nhận đã đọc hướng dẫn gửi đồ.
5. Bấm **Gửi duyệt sơ bộ**. Xem tiến độ tại **Tài khoản → Tủ đồ của tôi** hoặc **Thông báo**.

### Sau khi gửi yêu cầu

| Kết quả | Bạn cần làm gì |
|---|---|
| Chờ duyệt sơ bộ | Chờ CoCo xem hồ sơ. Có thể sửa khi hệ thống còn hiện nút sửa. |
| Yêu cầu cập nhật | Đọc lý do, sửa thông tin hoặc ảnh rồi gửi lại. |
| Đã duyệt sơ bộ | Mang hoặc gửi sản phẩm đến CoCo trong **3 ngày**. |
| Bị từ chối | Đọc lý do trong thông báo hoặc hồ sơ. |
| Đang bán / Chưa bán | Sản phẩm đã lên sàn và chưa bán thành công. |
| Đã có người đặt | Sản phẩm đang được giữ cho đơn hàng. |
| Đã bán | Đơn đã hoàn tất; kiểm tra tiền được cộng trong ví khi đã xác nhận thanh toán. |

Nếu gửi qua đơn vị vận chuyển, **trả trước cước gửi**, không yêu cầu CoCo thanh toán khi nhận. Ghi rõ **tên tài khoản trên website COCO RETRADE và số điện thoại** trên kiện hàng.

CoCo còn kiểm định hiện vật trước khi đăng bán. Chỉ xóa món khỏi tủ khi hệ thống cho phép; món đã kiểm định chính thức hoặc đăng bán sẽ bị khóa thao tác xóa.

### Thương lượng và rút tiền

1. Khi nhận đề xuất giá, mở thông báo hoặc **Đề xuất giá** để đồng ý, từ chối hoặc gửi mức giá khác trong thời hạn hiển thị.
2. Vào **Tài khoản → Ví ký gửi CoCo** để xem số dư. Bấm ô **Khả dụng** để xem các khoản tiền bán hàng đã cộng, kèm tên sản phẩm, số tiền và ngày nhận.
3. Trong **Yêu cầu rút tiền**, nhập số tiền không vượt số dư khả dụng, chọn tài khoản ngân hàng đã lưu hoặc bấm **+ Thêm tài khoản ngân hàng**.
4. Nhập thông tin ngân hàng; có thể tải QR để hỗ trợ điền thông tin. Luôn kiểm lại ngân hàng, số tài khoản và tên chủ tài khoản trước khi gửi.
5. Bấm **Gửi yêu cầu**. Nếu dùng tài khoản mới, chọn lưu lại hoặc không lưu; cả hai lựa chọn đều tiếp tục gửi yêu cầu.
6. Theo dõi **Lịch sử yêu cầu**. Bấm **Xem tất cả** để tìm kiếm và lọc các yêu cầu cũ.

**Chờ duyệt** nghĩa là CoCo chưa duyệt; **Đã duyệt, chờ chuyển tiền** chưa có nghĩa đã nhận tiền. Trạng thái **Đã chuyển tiền** chỉ được cập nhật sau khi nhân viên xác nhận chuyển khoản thực tế.

<a id="admin"></a>
## 5. Hướng dẫn cho Admin

Đăng nhập bằng tài khoản được cấp quyền quản trị, mở **Quản trị** trong tài khoản hoặc truy cập `http://localhost:5001/Admin`. Trên điện thoại, mở nút menu để xem danh sách chức năng.

### Tổng quan

- Xem sáu thẻ thống kê và biểu đồ doanh thu, hoa hồng, đơn hàng, sản phẩm.
- Chọn khoảng thời gian rồi bấm **Cập nhật**.
- Bấm thành phần trong biểu đồ để mở danh sách liên quan theo bộ lọc.

### Kiểm định và duyệt sản phẩm

1. Vào **Kiểm định và duyệt**, mở hồ sơ cần xử lý.
2. Ở **Bước 1 – Duyệt sơ bộ qua ảnh**, xem ảnh và thông tin; chọn **Duyệt sơ bộ**, **Yêu cầu cập nhật** hoặc **Từ chối**. Nhập lý do khi cập nhật hoặc từ chối, rồi xác nhận thao tác.
3. Khi sản phẩm đã đến cửa hàng, mở lại hồ sơ và thực hiện **Bước 2 · Kiểm tra hiện vật tại tiệm**.
4. Kiểm tra thông tin, độ mới và giá bán; bổ sung ghi chú hoặc ảnh kiểm định nếu cần. Có thể chọn **Đặt ảnh bìa** ở phần ảnh.
5. Nếu đạt, xác nhận hiện vật đạt kiểm định và bấm **Duyệt và đăng bán** khi tài khoản có quyền. Nếu không đạt, bỏ chọn đạt kiểm định và dùng **Lưu kết quả không đạt**.

Duyệt sơ bộ chưa làm sản phẩm xuất hiện trên sàn. Quyền kiểm định và quyền đăng bán có thể được cấp riêng.

### Xử lý đơn hàng

1. Mở **Đơn hàng**, tìm bằng mã đơn, mã sản phẩm hoặc tên sản phẩm, rồi chọn **Chi tiết**.
2. Kiểm tra người nhận, cách giao nhận, sản phẩm và tổng tiền. Thực hiện các nút đang được phép theo trạng thái:
   - **Giao tận nơi:** nhập mã vận chuyển và bấm **Đã bàn giao vận chuyển** khi đã giao cho đơn vị vận chuyển.
   - **Nhận tại cửa hàng:** bấm **Xác nhận đơn hàng**, chuẩn bị đồ rồi chọn **Sẵn sàng tại cửa hàng**. Xem thêm ở **Tạm giữ chờ lấy**.
3. Sau khi thực tế đã thu tiền, bấm **Xác nhận đã nhận tiền**.
4. Khi khách đã nhận hàng, bấm nút **Xác nhận giao thành công…** hoặc **Xác nhận nhận tại CoCo…** để hoàn tất đơn và cộng tiền vào ví.

Chỉ xác nhận thu tiền và hoàn tất khi sự việc đã diễn ra. Nếu khách không nhận hàng, chỉ dùng chức năng mở bán lại sau khi món đồ thực tế đã quay về CoCo và đáp ứng điều kiện trên trang.

### Duyệt rút tiền

1. Mở **Ví và rút tiền**, kiểm tra yêu cầu và thông tin ngân hàng.
2. Nếu hợp lệ, bấm **Duyệt yêu cầu rút tiền**. Nếu từ chối, nhập lý do theo biểu mẫu.
3. Thực hiện chuyển khoản bằng ngân hàng thực tế.
4. Khi chuyển thành công, nhập **mã giao dịch**, tích xác nhận đã kiểm tra chuyển khoản và bấm **Xác nhận đã chuyển tiền**.

Website ghi nhận và theo dõi yêu cầu; bấm duyệt không tự chuyển tiền từ ngân hàng.

### Các mục quản trị khác

| Mục | Cách dùng chính |
|---|---|
| **Sản phẩm** | Tìm, lọc trạng thái, mở chi tiết hoặc kiểm định. Xóa bằng icon thùng rác khi được phép; lịch sử liên quan vẫn được giữ. |
| **Người dùng** | Tìm tài khoản, bấm **Quản lý** để cấp/thu hồi quyền được phép, cập nhật điểm tín nhiệm, khóa/mở khóa theo biểu mẫu. |
| **Báo cáo doanh thu** | Chọn kỳ cần xem, kiểm tra doanh thu và hoa hồng. |
| **Hỗ trợ khách hàng** | Mở yêu cầu, đọc hội thoại, gửi phản hồi và cập nhật trạng thái. |
| **Lịch sử thao tác** | Tra cứu các thao tác đã được ghi nhận. |

Chỉ thao tác trong quyền được cấp. Với các lệnh xóa, đọc kỹ thông báo xác nhận; xóa tài khoản đủ điều kiện là thao tác không thể hoàn tác.

<a id="loi-thuong-gap"></a>
## 6. Xử lý lỗi thường gặp

| Hiện tượng | Cách xử lý |
|---|---|
| Mở link GitHub nhưng không thấy website chạy | GitHub là nơi nhận dự án. Cài theo phần 1 rồi mở `localhost` trên máy đã chạy website. |
| `dotnet` không được nhận diện hoặc thiếu SDK | Cài **.NET 10 SDK**, đóng rồi mở lại Terminal. |
| Không kết nối được SQL | Kiểm tra SQL Server đã chạy; đối chiếu `-SqlInstance` với **Server name** trong SSMS và đúng tên database đã khôi phục. |
| SQL không đọc được tệp `.bak` | Chép `.bak` vào thư mục Backup của SQL Server hoặc thư mục dịch vụ SQL có quyền đọc, rồi chọn lại trong SSMS. |
| Báo database đã tồn tại | Nếu đã khôi phục đúng bản, chạy website luôn; nếu cần bản khác, dùng tên database mới, không ghi đè bản đang có. |
| Cổng 5001 đang được dùng | Thêm `-Port 5003` vào lệnh `CHAY_COCO.ps1`, rồi mở `http://localhost:5003`. |
| Ảnh hoặc video không hiện | Kiểm tra đã tải ZIP đầy đủ trong Releases và giải nén toàn bộ. Không đổi tên tệp trong `PrivateUploads` hoặc `wwwroot`. |
| Đăng nhập được nhưng không nhận OTP / không dùng được Google | Cấu hình dịch vụ trên máy nhận theo bước 6 phần cài đặt; các khóa riêng không đi kèm bản bàn giao. |
| Không vào được Admin | Dùng tài khoản đã có quyền; tài khoản Người dùng không tự có quyền quản trị. |
| Script bị Windows chặn | Đọc nội dung script, mở **Properties → Unblock / Bỏ chặn** nếu có rồi chạy lại. Không cần tắt bảo vệ toàn máy. |
| Tắt Terminal thì không mở được website | Chạy lại `CHAY_COCO.ps1` và giữ cửa sổ đó mở. |

Khi cần hỗ trợ cài đặt, gửi nội dung lỗi và bước đang làm; che mật khẩu, khóa dịch vụ và thông tin cá nhân trong ảnh chụp.

---

**Kiểm tra nhanh sau cài đặt:** mở được Trang chủ và Mua sắm, ảnh/video hiện đủ, đăng nhập bằng tài khoản được cấp, xem được đơn/tủ đồ/ví theo quyền. Chỉ thử đặt hàng, duyệt đơn hoặc chuyển trạng thái tài chính trên dữ liệu được phép dùng thử.

Tài liệu đối chiếu với mã nguồn và gói bàn giao trên GitHub; cập nhật ngày **27/11/2026**. Các thao tác trên máy nhận được lưu trong SQL của máy nhận, không tự đồng bộ ngược lên GitHub hay máy khác.
