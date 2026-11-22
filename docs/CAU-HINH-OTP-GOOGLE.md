# Bật OTP email và đăng nhập Google cho CoCo

Mã nguồn đã có luồng OTP và Google. Dịch vụ thật chỉ hoạt động sau khi chủ website cấu hình tài khoản gửi email và OAuth. Không gửi App Password hay Client Secret qua chat, không ghi vào `appsettings.json` hoặc Git.

Trên Windows có thể nhập khóa trong cửa sổ riêng bằng `powershell.exe -NoProfile -STA -File .\tools\ConfigureCoCoAuthWindow.ps1` từ thư mục dự án. Gmail mặc định là `cocoretradekygui@gmail.com`. Hai nút lưu OTP và Google hoạt động riêng, mật khẩu được che, mục để trống không xóa khóa cũ. Cửa sổ chỉ lưu khóa vào User Secrets trên máy, không tạo khóa Google, không mở trình duyệt, không gửi email hoặc khởi động lại website. Sau khi lưu cần khởi động lại CoCo và kiểm tra dịch vụ thật.

## 1. Gmail gửi mã OTP

1. Dùng Gmail dành cho cửa hàng, bật xác minh hai bước tại https://myaccount.google.com/security.
2. Mở https://myaccount.google.com/apppasswords và tạo App Password cho CoCo. Nếu không có mục này, kiểm tra chính sách tài khoản Google Workspace hoặc dùng nhà cung cấp SMTP khác.
3. Chạy PowerShell trong thư mục dự án: `& .\tools\ConfigureCoCoAuth.ps1 -EmailOnly`. Nhập Gmail và App Password tại lời nhắc trên máy. Mật khẩu được che khi nhập.
4. Khởi động lại CoCo. SMTP dùng `smtp.gmail.com`, cổng `587`, STARTTLS, địa chỉ gửi trùng tài khoản Gmail. Các khóa là `Smtp:Host`, `Smtp:Port`, `Smtp:From`, `Smtp:User`, `Smtp:Password`.
5. Tự đăng ký bằng email bạn kiểm soát. Kiểm tra Hộp thư đến và Spam. Mã gồm 6 số, hiệu lực 5 phút, chờ 60 giây trước khi gửi lại, tối đa 5 lần nhập sai. Không có mã mặc định hoặc mã bỏ qua xác thực.

Nếu SMTP chưa sẵn sàng, đăng ký báo rõ dịch vụ chưa bật và không tạo tài khoản mới dở dang. Tài khoản đã tạo nhưng gửi email thất bại có thể dùng **Gửi lại mã**. Lỗi SMTP không tự kích hoạt tài khoản.

Cấu hình SMTP cho OTP không tự bật email thông báo mua bán, ký gửi hoặc ví. Kênh email marketplace mặc định tắt qua `Notifications:MarketplaceEmailEnabled=false`; thông báo trong website vẫn hoạt động. Hàng đợi email hiện có được giữ nguyên, chưa gửi khi kênh này tắt. Chỉ khi chủ cửa hàng muốn bật email marketplace mới đặt `Notifications:MarketplaceEmailEnabled=true` (biến môi trường `Notifications__MarketplaceEmailEnabled`) và khởi động lại. Khi bật, hệ thống có thể gửi cả những thông báo cũ đang chờ đến hạn, nên kiểm tra hàng đợi trước.

## 2. Google OAuth

1. Mở https://console.cloud.google.com/, tạo hoặc chọn dự án của cửa hàng. Trong **Google Auth Platform**, cấu hình Branding, Audience và Data Access cho `openid`, `email`, `profile`.
2. Nếu đang ở chế độ Testing, thêm email kiểm thử trong Audience. Muốn khách hàng dùng rộng rãi, hoàn tất thông tin ứng dụng, tên miền, chính sách riêng tư và chuyển sang Production theo yêu cầu Google.
3. Tạo OAuth Client loại **Web application**. Thêm redirect URI chính xác `http://localhost:5001/signin-google`. Nếu kiểm tra demo thêm `http://localhost:5002/signin-google`. Khi triển khai phải thêm `https://TEN-MIEN-THAT/signin-google`; thay bằng tên miền thật, không giữ chuỗi ví dụ.
4. Chạy `& .\tools\ConfigureCoCoAuth.ps1 -GoogleOnly`. Nhập Client ID và Client Secret trực tiếp trên máy. Khóa tương ứng: `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`.
5. Khởi động lại CoCo, bấm **Tiếp tục với Google** ở Đăng nhập hoặc Đăng ký. Dùng Google có email đã xác minh. Lần đầu tạo tài khoản quyền Người dùng, không tự cấp quyền quản lý.
6. Nếu email đã có tài khoản CoCo, đăng nhập bằng mật khẩu, mở **Tài khoản của tôi → Liên kết Google**, xác nhận mật khẩu rồi chọn Google cùng email. Không tự ghép tài khoản chỉ dựa trên email trùng. Tài khoản bị tạm khóa không được đăng nhập bằng Google.

Google kiểm tra callback bằng correlation cookie và OAuth state; token Google không được lưu. Khi hủy đăng nhập hoặc callback sai, website trở lại trang đăng nhập với thông báo, không báo thành công giả.

CoCo lấy trạng thái xác minh email từ trường `email_verified` của Google UserInfo v3. Không dùng tên trường cũ `verified_email`, không suy ra trạng thái xác minh từ đuôi Gmail hoặc chỉ từ việc Google trả về địa chỉ email. Khi trường này thiếu hoặc có giá trị false, đăng nhập vẫn bị từ chối. Sau khi sửa cách ánh xạ, cần bắt đầu lại từ nút Google trên trang đăng nhập để nhận phiên mới.

### Đăng nhập qua Cloudflare Tunnel

Trong **Google Auth Platform → Clients → CoCo ReTrade Web → Authorized redirect URIs**, thêm đúng địa chỉ callback công khai:

```text
https://atom-whenever-hugo-stability.trycloudflare.com/signin-google
```

Đường dẫn `/Account/Login` là trang bắt đầu đăng nhập, không phải callback Google. Giữ hai callback localhost nếu vẫn dùng bản chạy trên máy. Lưu thay đổi trên Google Cloud, chờ cấu hình cập nhật rồi bắt đầu lại từ trang đăng nhập CoCo; không tải lại trang lỗi Google cũ. Không cần tạo lại Client ID hoặc Client Secret khi chỉ sửa redirect URI.

CoCo xử lý `X-Forwarded-Proto` trước HTTPS và xác thực, chỉ tin proxy kết nối từ `127.0.0.1` hoặc `::1`. Vì vậy truy cập HTTPS qua tunnel tạo callback HTTPS, còn localhost HTTP vẫn dùng callback HTTP. Không bật chế độ tin mọi proxy. Nếu chuyển tunnel sang máy khác, cần xác định chính xác IP proxy trước khi mở rộng cấu hình.

Địa chỉ Quick Tunnel có thể đổi khi khởi động lại. Mỗi lần đổi phải cập nhật redirect URI trên Google Cloud; triển khai lâu dài nên dùng tên miền cố định. Không đăng ký callback HTTP cho tên miền công khai để xử lý lỗi `redirect_uri_mismatch`.

## 3. Lưu cấu hình và triển khai

Script lưu trong ASP.NET User Secrets của người dùng Windows hiện tại, ngoài repository. User Secrets phục vụ phát triển, không phải kho mã hóa dùng cho máy chủ sản xuất. Trên máy chủ dùng secret store của nền tảng hoặc biến môi trường `Smtp__Password`, `Authentication__Google__ClientSecret` cùng các khóa còn lại; chỉ cấp quyền cho tài khoản chạy ứng dụng.

Không chạy `dotnet user-secrets list` khi chia sẻ màn hình hoặc gửi log vì lệnh hiển thị giá trị. Nếu cần sửa cấu hình, chạy lại script. Không cần tạo lại SQL hay tài khoản.

Sau cấu hình cần xác nhận thực tế: email đến được, mã hết hạn bị từ chối, gửi lại có giới hạn, Google tạo tài khoản mới đúng quyền, email trùng yêu cầu liên kết, tài khoản bị khóa không vượt được qua Google. Kiểm thử nội bộ không thay thế lần kiểm tra này.
