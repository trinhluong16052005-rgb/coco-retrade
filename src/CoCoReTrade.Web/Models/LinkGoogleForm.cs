using System.ComponentModel.DataAnnotations;
namespace CoCoReTrade.Web.Models;
public sealed class LinkGoogleForm
{
    [Required(ErrorMessage = "Nhập mật khẩu hiện tại để xác nhận."), DataType(DataType.Password), Display(Name = "Mật khẩu hiện tại")]
    public string CurrentPassword { get; set; } = "";
}
