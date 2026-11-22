using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace CoCoReTrade.Web;

public sealed class CheckoutVoucherSelection(IDataProtectionProvider dataProtection)
{
    private const string CookieName = "CoCo.CheckoutVoucher";

    public string? Read(HttpContext context, string buyerId)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try { return dataProtection.CreateProtector(CookieName, buyerId).Unprotect(value); }
        catch (CryptographicException) { return null; }
    }

    public void Save(HttpContext context, string buyerId, string? code)
    {
        var options = new CookieOptions
        {
            Path = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "/",
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true
        };
        if (code is null) context.Response.Cookies.Delete(CookieName, options);
        else context.Response.Cookies.Append(CookieName, dataProtection.CreateProtector(CookieName, buyerId).Protect(code), options);
    }
}
