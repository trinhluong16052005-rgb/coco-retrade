using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using CoCoReTrade.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Controllers;

[Authorize]
public sealed class ProfileController(INotificationCenter notifications, ISocialService social,
    IAddressBookService addresses, IWalletService wallets, IUserProfileService profiles,
    IWalletBankingService banking, INotificationNavigation? navigation = null) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet("/Profile/Not"), HttpGet("/Profile/Notiifications")]
    public IActionResult NotificationAlias() => RedirectToActionPermanent(nameof(Notifications));
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Balance = await wallets.BalanceAsync(UserId, ct);
        ViewBag.UserProfile = await profiles.GetAsync(UserId, ct);
        return View();
    }
    [HttpPost, RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Avatar(IFormFile image, CancellationToken ct)
    {
        if (image is null) { TempData["Error"] = "Chọn ảnh đại diện."; return RedirectToAction(nameof(Index)); }
        try
        {
            await profiles.SetAvatarAsync(UserId,
                new ImageUpload(image.FileName, image.ContentType, image.Length, image.OpenReadStream), ct);
            TempData["Message"] = "Đã cập nhật ảnh đại diện.";
        }
        catch (ArgumentException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
    [HttpGet]
    public async Task<IActionResult> Notifications(string? group, CancellationToken ct)
    {
        ViewBag.Group = group;
        try
        {
            var items = await notifications.ListAsync(UserId, group, ct);
            ViewBag.HasUnreadNotifications = items.Any(item => !item.IsRead) || await notifications.HasUnreadAsync(UserId, ct);
            return View(items);
        }
        catch (ArgumentException) { return BadRequest("Nhóm thông báo không hợp lệ."); }
    }
    [HttpPost]
    public async Task<IActionResult> ReadAllNotifications(string? group, CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(UserId, ct);
        return RedirectToAction(nameof(Notifications), new { group = group is "transactions" or "listings" or "social" ? group : null });
    }
    [HttpPost]
    public async Task<IActionResult> ReadNotification(Guid id, CancellationToken ct)
    {
        try { await notifications.MarkReadAsync(UserId, id, ct); }
        catch (KeyNotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Notifications));
    }
    [HttpGet]
    public async Task<IActionResult> OpenNotification(Guid id, CancellationToken ct)
    {
        NotificationDto item;
        try { item = await notifications.OpenAsync(UserId, id, ct); }
        catch (KeyNotFoundException) { return NotFound(); }

        if (navigation is not null)
        {
            var granted = User.FindAll(StaffPermissions.ClaimType).Select(x => x.Value)
                .Where(permission => StaffPermissions.RequiredRoles(permission).Any(User.IsInRole)).ToHashSet();
            var destination = await navigation.ResolveAsync(UserId, item, granted, ct);
            return RedirectToAction(destination.Action, destination.Controller, new
            {
                area = destination.Area, id = destination.Id, walletId = destination.WalletId,
                requestId = destination.RequestId, search = destination.Search
            }, destination.Fragment);
        }

        if (item.OrderId is Guid orderId)
            return RedirectToAction("Details", "Orders", new { area = User.IsInRole("Admin") ? "Admin" : "", id = orderId });
        if (item.OfferId is Guid offerId)
            return RedirectToAction("Details", "Offers", new { area = "", id = offerId });
        if (item.ConversationId is Guid conversationId)
            return RedirectToAction("Details", "Messages", new { id = conversationId });
        if (item.ProductId is Guid productId)
        {
            if (item.Category.Equals("Listings", StringComparison.OrdinalIgnoreCase) &&
                (User.IsInRole("Admin") || User.IsInRole("Inspector")))
                return RedirectToAction("Review", "Inspection", new { area = "Admin", id = productId });
            return RedirectToAction("Details", "Shop", new { id = productId });
        }
        if (item.Category.Equals("Cart", StringComparison.OrdinalIgnoreCase) ||
            item.Category.Equals("Carts", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Cart");
        if (item.Category.Equals("Messages", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Messages");
        if (item.Category.Equals("Offers", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Offers");
        if (item.Category.Equals("Orders", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Orders", new { area = User.IsInRole("Admin") ? "Admin" : "" });
        if (string.Equals(item.Category, "Wallets", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Wallet", "Profile");
        if (string.Equals(item.Category, "Support", StringComparison.OrdinalIgnoreCase))
            return User.IsInRole("Admin")
                ? RedirectToAction("Index", "HelpDesk", new { area = "Admin" })
                : RedirectToAction("Index", "Support", new { area = "" });
        return RedirectToAction(nameof(Notifications));
    }
    public async Task<IActionResult> Saved(CancellationToken ct) => View(await social.SavedProductsAsync(UserId, ct));
    public async Task<IActionResult> Addresses(CancellationToken ct) => View(await addresses.ListAsync(UserId, ct));
    [HttpGet]
    public async Task<IActionResult> Address(Guid? id, CancellationToken ct)
    {
        if (id is null) return View(new AddressForm());
        var existing = (await addresses.ListAsync(UserId, ct)).FirstOrDefault(x => x.Id == id);
        if (existing is null) return NotFound();
        return View(new AddressForm { Id = existing.Id, RecipientName = existing.RecipientName,
            RecipientPhone = existing.RecipientPhone, Province = VietnamProvinces.Normalize(existing.Province) ?? "",
            Ward = existing.Ward, StreetAddress = existing.StreetAddress });
    }
    [HttpPost]
    public async Task<IActionResult> Address(AddressForm form, CancellationToken ct)
    {
        var province = VietnamProvinces.Normalize(form.Province);
        if (province is null) ModelState.AddModelError(nameof(form.Province), "Hãy chọn tỉnh hoặc thành phố giao hàng.");
        if (!ModelState.IsValid) return View(form);
        try
        {
            form.Province = province!;
            await addresses.SaveAsync(UserId, form.ToInput(), ct);
            TempData["Message"] = "Đã lưu địa chỉ nhận hàng.";
            return RedirectToAction(nameof(Addresses));
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        { ModelState.AddModelError(string.Empty, ex.Message); return View(form); }
    }
    [HttpPost]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken ct)
    {
        try { await addresses.DeleteAsync(UserId, id, ct); TempData["Message"] = "Đã xóa địa chỉ."; }
        catch (KeyNotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Addresses));
    }
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Wallet(CancellationToken ct)
    {
        ViewBag.WithdrawalPage = await banking.HistoryAsync(UserId, null, null, 1, 5, ct);
        ViewBag.BankAccounts = await banking.AccountsAsync(UserId, ct);
        ViewBag.Credits = await wallets.CreditsAsync(UserId, ct);
        return View(await wallets.BalanceAsync(UserId, ct));
    }
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Withdrawals(string? search, string? status, int page = 1, CancellationToken ct = default)
    {
        ViewBag.Search = search; ViewBag.Status = status;
        try { return View(await banking.HistoryAsync(UserId, search, status, page, 10, ct)); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }
    [HttpPost]
    public async Task<IActionResult> Withdraw(WithdrawalForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Kiểm tra số tiền và tài khoản nhận."; return RedirectToAction(nameof(Wallet)); }
        try
        {
            await banking.SubmitAsync(UserId, form.ToInput(), ct);
            TempData["Message"] = "Yêu cầu rút tiền đã được ghi nhận và đang chờ duyệt.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Wallet));
    }
}
