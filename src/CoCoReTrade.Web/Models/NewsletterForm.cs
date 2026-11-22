using System.ComponentModel.DataAnnotations;

namespace CoCoReTrade.Web.Models;

public sealed class NewsletterForm
{
    [Required(ErrorMessage = "Vui lòng nhập email của bạn."),
     StringLength(254, ErrorMessage = "Email không được vượt quá 254 ký tự."),
     EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = "";

    public string? ReturnUrl { get; set; }
    public string? Website { get; set; }
}
