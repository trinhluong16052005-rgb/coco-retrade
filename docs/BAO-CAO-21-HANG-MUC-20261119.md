# CoCo — báo cáo 21 hạng mục, ngày 19/11/2026

Bản chính đang chạy tại http://localhost:5001. Bản demo dùng SQL riêng tại http://localhost:5002.

Đã cập nhật mã nguồn theo 21 yêu cầu và ảnh trong PDF `COCO RETRADE (1).pdf` (9 trang). Build Release đạt, 0 lỗi và 0 cảnh báo. **91/91 kiểm thử đạt**, gồm kiểm thử HTTP và SQL trên database tạm riêng. OTP email và đăng nhập Google còn chờ chủ website cấu hình dịch vụ và xác nhận hoạt động thật.

## Đối chiếu 21 hạng mục

| Mục | Kết quả trong mã nguồn | Kiểm tra đã thực hiện |
| --- | --- | --- |
| 1. Mua sắm, bộ lọc | Thu khoảng cách tiêu đề và lưới, tăng chữ thẻ. Bộ lọc là bảng trượt bên phải, giữ danh mục, thương hiệu, kích cỡ, tình trạng, giá, nhận hàng và sắp xếp. Có đóng bằng nút, Escape, nền ngoài và khôi phục focus. | HTTP trang và HTML dialog đạt. CSS/JS hợp lệ. Chưa kiểm bằng thao tác trình duyệt. |
| 2. Tab thông báo | Bốn tab căn giữa hai chiều, chiều cao đồng nhất. Mobile chia hai cột. | Rà soát CSS responsive. Chưa đối chiếu ảnh hiển thị. |
| 3. Nhãn quản trị | Đổi tên hiển thị thành Quản lý CoCo. Giữ role Admin, route và tên biến. | HTTP tài khoản quản trị trong SQL thử đạt. |
| 4. Trái tim | Cùng góc dưới phải ảnh, bỏ nền và khung. Trạng thái lưu tô kín màu hồng, bỏ lưu trở về nét. Sửa SVG để nhận màu tô. | Rà soát template, CSS, SVG và luồng AJAX hiện có. Chưa bấm thử trực quan. |
| 5. Ghi chú ký gửi | Chữ nhỏ, nghiêng, nhẹ hơn, giữ trên nút gửi duyệt sơ bộ. | Rà soát template và CSS. |
| 6. Chi tiết sản phẩm | Mở rộng ảnh, bỏ giới hạn bó ảnh, giảm khoảng cách, hai cột cân bằng trên desktop và một cột trên mobile. Áp dụng cả sản phẩm chờ kiểm định. | HTTP các chi tiết công khai đạt; rà soát CSS dùng chung. Chưa xem trạng thái ký gửi trên thiết bị. |
| 7. Thông báo thanh toán | Bỏ đúng đoạn Đơn mới chưa được coi là đã thanh toán trong màn hình đơn quản trị được chụp ở PDF. Các số tiền và thao tác thanh toán giữ nguyên. | Rà soát Razor và build. |
| 8. Xóa sản phẩm | Nút Xóa trong quản lý sản phẩm, xác nhận trước gửi. Chỉ Admin có quyền quản lý sản phẩm được thực hiện. Xóa khỏi danh mục bằng trạng thái Removed, giữ lịch sử ảnh, đơn và tài chính; chặn sản phẩm đang có đơn chưa hoàn tất. | Unit test quyền, phiên bản và trạng thái. HTTP chặn người dùng thường, chặn thiếu CSRF, xóa hợp lệ đạt trên SQL thử. Không xóa hàng thật để kiểm thử. |
| 9. Video banner | Dùng đúng video 1118.mp4 do người dùng cung cấp. Tạo bản desktop và mobile H.264, bỏ âm thanh, hỗ trợ faststart. Giữ tự phát, tắt tiếng, lặp, playsinline cùng tùy chọn giảm chuyển động. | Tệp nguồn tồn tại. Kiểm tra metadata tỷ lệ 16:9. Cả hai video trả HTTP 206 cho yêu cầu Range. Chưa xem phát trong trình duyệt. |
| 10. Vừa cập bến | Khung ảnh 3:4, object-fit cover, thẻ đồng nhất. Trang Mua sắm tiếp tục dùng khung ảnh vuông. | Rà soát CSS riêng cho homepage và kiểm tra ảnh HTTP. |
| 11. Tài chính người ký gửi | Chỉ hiện Tiền hàng và Tiền bạn nhận được sau hoa hồng. Tiền thực nhận in đậm. Người mua giữ thông tin thanh toán đầy đủ, thêm dòng giảm giá khi có voucher. | Rà soát điều kiện theo người xem; SQL kiểm tra tiền người bán được bảo toàn. |
| 12. Xem ảnh | Bỏ Cách xem ảnh và hướng dẫn; bỏ chữ Phóng to, khung kính lúp và vòng tròn mũi tên. Giữ chuyển ảnh, thumbnail, phóng to và nhãn hỗ trợ đọc màn hình. | HTTP xác nhận bỏ nội dung, rà soát JS gallery, build đạt. Chưa thao tác ảnh trực quan. |
| 13. Kích cỡ quần áo | Chuẩn XS, S, M, L, XL, 2XL, 3XL ở tạo/sửa ký gửi và kiểm định. Bộ lọc giữ kích cỡ cũ để tìm được hàng đang có. | Unit test chuẩn hóa; kiểm tra server/client và dữ liệu cũ được bảo toàn. |
| 14. Chính sách ký gửi | Bỏ khung ví dụ, nhấn 65% người gửi nhận; không hiện CoCo nhận 35%. Giảm chiều cao ảnh đầu trang. | HTTP trang chính sách và rà soát CSS. Không đổi phép tính hoa hồng trong nghiệp vụ. |
| 15. Footer | Giảm padding, margin và khoảng cách nhóm; giữ các thông tin và đường dẫn. | HTTP các trang liên kết công khai đạt, rà soát responsive. |
| 16. Kích cỡ phụ kiện | Chọn Phụ kiện tự điền Kích cỡ tự do, không bắt chọn thủ công. Đồng bộ cả server; Túi giữ cùng cách xử lý phù hợp. | Unit test và rà soát biểu mẫu/server. |
| 17. Bo góc | Chuẩn bo nhẹ 8px cho các thành phần phù hợp, 6px cho vùng ảnh. Giữ các nút icon thuần theo yêu cầu. | CSS parser đạt; rà soát phạm vi selector. |
| 18. Giỏ hàng và thanh toán | Cùng bố cục thông tin nhận hàng, địa chỉ đã lưu, giao/nhận tại tiệm, chọn từng món/tất cả, mã giảm giá, danh sách và tổng kết. Đặt trực tiếp từ giỏ. Mua ngay dùng cùng giao diện. | SQL và HTTP kiểm tra tính tiền, chọn món, phí giao một lần, áp mã, đặt hàng, chống tạo đơn trùng, tranh chấp tồn kho và transaction không tạo dở dang. Chưa thao tác browser/mobile. |
| 19. OTP và Google | Hoàn thiện trạng thái lỗi cấu hình, luồng OTP và Google, liên kết tài khoản an toàn, không tự gộp email trùng. Chưa cấu hình thì báo rõ và không tạo tài khoản đăng ký dở dang. Có hướng dẫn và script nhập khóa kín trên máy. | SQL kiểm tra OTP hash, hết hạn, dùng một lần, giới hạn thử/gửi lại; kiểm tra Google chưa xác minh và email trùng bị chặn. **Chưa gửi email thật hoặc đăng nhập Google thật vì chưa có SMTP/OAuth.** |
| 20. Gợi ý sản phẩm | Chi tiết có Sản phẩm tương tự. Thanh toán có Có thể bạn cũng thích cùng nút thêm giỏ. Chỉ đề xuất hàng đang mở bán, loại hàng của người xem và hàng đã chọn. | HTTP render hai khu vực, rà soát điều kiện khả dụng. Không tạo đơn thật. |
| 21. Thẻ Mũ Spencer's | Chuẩn chiều cao tên, vùng thông tin và giá, cuối thẻ dành cho hai nút mua. Chủ món đồ thấy hai nút vô hiệu hóa để không tự mua; khách và người dùng khác có nút mua bình thường. | HTTP bản chính xác nhận thẻ Mũ Spencer's hồng trắng phối lưới có giá và đủ hai nút. CSS giới hạn tên hai dòng, giá dùng phần trống linh hoạt. Chưa đối chiếu vị trí bằng ảnh màn hình. |

## Voucher đã lưu trên SQL

| Mã | Mức giảm | Tiền hàng tối thiểu | Hết hạn |
| --- | --- | --- | --- |
| GIAM5 | 5% | 300.000đ | Hết ngày 30/12/2026, giờ Việt Nam |
| GIAM10 | 10% | 500.000đ | Hết ngày 30/12/2026, giờ Việt Nam |

Mỗi lượt đặt dùng một mã. Điều kiện tính trên tiền hàng, không gồm phí giao. Giảm giá do CoCo chịu, không giảm phần tiền ký gửi được nhận. Tổng tiền được tính lại phía server; mã hết hạn hoặc chưa đủ điều kiện bị từ chối. SQL lưu thời điểm hết hạn loại trừ là 2026-12-30 17:00:00 UTC, tức 00:00 ngày 31/12 tại Việt Nam.

Giỏ nhiều món vẫn tạo một đơn cho từng món để giữ quy trình kiểm định, giao nhận và tài chính hiện có. Phí giao chỉ tính một lần cho giỏ rồi phân bổ cùng giảm giá về từng đơn; tổng phần phân bổ luôn khớp hóa đơn.

## SQL và dữ liệu

Đã backup và RESTORE VERIFYONLY WITH CHECKSUM cho cả hai DB trước migration. Migration bổ sung `20261119103158_CheckoutBasketVouchers` đã chạy thành công ở cả hai DB, không thay thế dữ liệu hiện có.

| Database | Sản phẩm trước/sau | Đơn trước/sau | Tài khoản trước/sau | Ảnh trước/sau |
| --- | --- | --- | --- | --- |
| CoCoReTrade | 22 / 22 | 4 / 4 | 17 / 17 | 80 / 80 |
| CoCoReTradeDemo | 25 / 25 | 0 / 0 | 4 / 4 | 75 / 75 |

Kiểm thử ghi dữ liệu chỉ dùng database tạm có tiền tố CoCoReviewTests_ hoặc CoCoReviewWebTests_ và dọn sau khi chạy. Không đặt hàng hoặc xóa sản phẩm trên kho thật để thử.

## Bằng chứng kiểm tra

- Build: `.artifacts/coco21-20261119/build-final.log`, 0 warning, 0 error.
- 91/91 test: `.artifacts/coco21-20261119/tests/coco21-final.trx`, có bật COCO_RUN_SQL_TESTS=1.
- HTTP bản chạy: `.artifacts/coco21-20261119/http-smoke.json`: 30 lượt trang cổng 5001, 36 lượt cổng 5002, 189 lượt tài nguyên mỗi cổng, không lỗi; video desktop/mobile đều trả 206 và đúng 1.024 byte yêu cầu.
- JavaScript syntax `site.js`, `checkout.js` đạt; parser Lightning CSS đọc được `refinements.css` và `checkout.css`.
- Backup/migration: `sql-backup.log`, `migration-main.log`, `migration-demo.log` cùng thư mục bằng chứng.
- PDF đã render và xem 9 trang ở `.artifacts/coco21-20261119/pdf/`.
- HTML tham khảo đã lấy được từ Thrift+ (bộ lọc, chi tiết) và Coolmate (giỏ hàng), lưu `reference-1.html` đến `reference-3.html`. Chưa xác nhận mức giống bằng so sánh trực quan trong browser.

## Còn cần hoàn tất ngoài mã nguồn

1. Chủ website cấu hình Gmail SMTP và Google OAuth theo [CAU-HINH-OTP-GOOGLE.md](CAU-HINH-OTP-GOOGLE.md), sau đó kiểm tra email nhận thật và Google callback thật. Không gửi mật khẩu hay Client Secret qua chat.
2. Kiểm tra trực quan desktop, tablet, mobile và thao tác drawer, trái tim, gallery, voucher, giỏ hàng trên trình duyệt. Công cụ điều khiển trình duyệt hiện lỗi `Computer Use native pipe is unavailable` (os error 2), nên chưa thực hiện được phần này. Kiểm thử HTTP/CSS không thay thế kiểm tra hiển thị thực tế.
3. Website đang chạy, nhưng cơ chế duyệt tự động đã từ chối lệnh mở URL bằng `Start-Process`, với lý do `blocked by policy`. Chưa mở được tab mới cho người dùng; có thể truy cập trực tiếp hai địa chỉ localhost nêu đầu báo cáo.

## Tệp thay đổi trong lượt sửa này

Danh sách mã nguồn được so với snapshot trước lượt tại `.artifacts/coco21-20261119/before`, tránh gộp những thay đổi đã có từ các yêu cầu trước. Các tài nguyên SVG, JSON, video và tài liệu được ghi riêng vì snapshot không bao gồm các loại này.

### Mã nguồn đã sửa

- `src/CoCoReTrade.Application/AdminProducts.cs`
- `src/CoCoReTrade.Application/Orders.cs`
- `src/CoCoReTrade.Application/Products.cs`
- `src/CoCoReTrade.Domain/Marketplace.cs`
- `src/CoCoReTrade.Domain/PlatformCommissionEntry.cs`
- `src/CoCoReTrade.Infrastructure/AdminFinancialReportRepository.cs`
- `src/CoCoReTrade.Infrastructure/AdminProductRepository.cs`
- `src/CoCoReTrade.Infrastructure/AppDbContext.cs`
- `src/CoCoReTrade.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `src/CoCoReTrade.Infrastructure/OrderRepository.cs`
- `src/CoCoReTrade.Infrastructure/ProductRepository.cs`
- `src/CoCoReTrade.Web/Areas/Admin/Controllers/InspectionController.cs`
- `src/CoCoReTrade.Web/Areas/Admin/Controllers/ProductsController.cs`
- `src/CoCoReTrade.Web/Areas/Admin/Views/Inspection/Review.cshtml`
- `src/CoCoReTrade.Web/Areas/Admin/Views/Orders/Details.cshtml`
- `src/CoCoReTrade.Web/Areas/Admin/Views/Products/Index.cshtml`
- `src/CoCoReTrade.Web/Areas/Admin/Views/Wallets/Platform.cshtml`
- `src/CoCoReTrade.Web/CoCoReTrade.Web.csproj`
- `src/CoCoReTrade.Web/Controllers/AccountController.cs`
- `src/CoCoReTrade.Web/Controllers/Api/V1/MobileAccountController.cs`
- `src/CoCoReTrade.Web/Controllers/CartController.cs`
- `src/CoCoReTrade.Web/Controllers/CheckoutController.cs`
- `src/CoCoReTrade.Web/Controllers/ListingController.cs`
- `src/CoCoReTrade.Web/Controllers/ShopController.cs`
- `src/CoCoReTrade.Web/Models/CheckoutForm.cs`
- `src/CoCoReTrade.Web/Models/UiText.cs`
- `src/CoCoReTrade.Web/Program.cs`
- `src/CoCoReTrade.Web/Views/About/Policies.cshtml`
- `src/CoCoReTrade.Web/Views/Account/Login.cshtml`
- `src/CoCoReTrade.Web/Views/Account/Register.cshtml`
- `src/CoCoReTrade.Web/Views/Cart/Index.cshtml`
- `src/CoCoReTrade.Web/Views/Checkout/Index.cshtml`
- `src/CoCoReTrade.Web/Views/Home/Index.cshtml`
- `src/CoCoReTrade.Web/Views/Listing/Create.cshtml`
- `src/CoCoReTrade.Web/Views/Listing/Edit.cshtml`
- `src/CoCoReTrade.Web/Views/Orders/Details.cshtml`
- `src/CoCoReTrade.Web/Views/Profile/Index.cshtml`
- `src/CoCoReTrade.Web/Views/Shared/_Layout.cshtml`
- `src/CoCoReTrade.Web/Views/Shared/_ProductPurchaseActions.cshtml`
- `src/CoCoReTrade.Web/Views/Shared/_ShopFilters.cshtml`
- `src/CoCoReTrade.Web/Views/Shop/Details.cshtml`
- `src/CoCoReTrade.Web/wwwroot/js/site.js`
- `tests/CoCoReTrade.Tests/AdminModuleTests.cs`
- `tests/CoCoReTrade.Tests/CoCoReTrade.Tests.csproj`

### Mã nguồn mới

- `src/CoCoReTrade.Application/BasketCheckout.cs`
- `src/CoCoReTrade.Application/ExternalAccounts.cs`
- `src/CoCoReTrade.Application/ProductRecommendations.cs`
- `src/CoCoReTrade.Domain/CheckoutBasket.cs`
- `src/CoCoReTrade.Domain/ProductSizing.cs`
- `src/CoCoReTrade.Infrastructure/BasketRepository.cs`
- `src/CoCoReTrade.Infrastructure/ExternalAccountService.cs`
- `src/CoCoReTrade.Infrastructure/Migrations/20261119103158_CheckoutBasketVouchers.cs`
- `src/CoCoReTrade.Infrastructure/Migrations/20261119103158_CheckoutBasketVouchers.Designer.cs`
- `src/CoCoReTrade.Web/CheckoutPageService.cs`
- `src/CoCoReTrade.Web/Models/CheckoutPageData.cs`
- `src/CoCoReTrade.Web/Models/LinkGoogleForm.cs`
- `src/CoCoReTrade.Web/Views/Account/LinkGoogle.cshtml`
- `src/CoCoReTrade.Web/Views/Shared/_Checkout.cshtml`
- `src/CoCoReTrade.Web/Views/Shared/_CheckoutItems.cshtml`
- `src/CoCoReTrade.Web/Views/Shared/_GoogleSignIn.cshtml`
- `src/CoCoReTrade.Web/wwwroot/css/checkout.css`
- `src/CoCoReTrade.Web/wwwroot/css/refinements.css`
- `src/CoCoReTrade.Web/wwwroot/js/checkout.js`
- `tests/CoCoReTrade.Tests/CheckoutRulesTests.cs`
- `tests/CoCoReTrade.Tests/SqlCheckoutIntegrationTests.cs`
- `tests/CoCoReTrade.Tests/WebCheckoutIntegrationTests.cs`

### Tài nguyên và hướng dẫn

- `src/CoCoReTrade.Web/wwwroot/images/icons.svg`
- `src/CoCoReTrade.Web/campaign-media.json`
- `src/CoCoReTrade.Web/wwwroot/media/editorial/coco-banner-1118.mp4`
- `src/CoCoReTrade.Web/wwwroot/media/editorial/coco-banner-1118-mobile.mp4`
- `tools/ConfigureCoCoAuth.ps1`
- `docs/CAU-HINH-OTP-GOOGLE.md`
- `docs/BAO-CAO-21-HANG-MUC-20261119.md` (báo cáo này)
