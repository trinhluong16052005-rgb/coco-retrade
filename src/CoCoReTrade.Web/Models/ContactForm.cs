using System.ComponentModel.DataAnnotations;

namespace CoCoReTrade.Web.Models;

public sealed class ContactForm
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên."),
     StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên cần từ 2 đến 100 ký tự."),
     Display(Name = "Họ và tên")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập email."),
     StringLength(254, ErrorMessage = "Email không được vượt quá 254 ký tự."),
     EmailAddress(ErrorMessage = "Email không hợp lệ."), Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại."),
     StringLength(30, ErrorMessage = "Số điện thoại không được vượt quá 30 ký tự."),
     RegularExpression(@"^\+?(?:[0-9][ .()-]*){6,14}[0-9]$", ErrorMessage = "Nhập số điện thoại gồm 7 đến 15 chữ số, có thể kèm mã quốc gia."),
     Display(Name = "Số điện thoại")]
    public string Phone { get; set; } = "";

    // Metadata plus these limits always fits the support message's 2,000-character limit.
    [Required(ErrorMessage = "Vui lòng nhập nội dung lời nhắn."),
     StringLength(1500, MinimumLength = 3, ErrorMessage = "Lời nhắn cần từ 3 đến 1.500 ký tự."),
     Display(Name = "Lời nhắn")]
    public string Message { get; set; } = "";

    public string? Website { get; set; }
}
