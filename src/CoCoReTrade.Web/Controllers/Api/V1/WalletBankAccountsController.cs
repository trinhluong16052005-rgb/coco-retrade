using System.Security.Claims;
using CoCoReTrade.Application;
using CoCoReTrade.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Controllers.Api.V1;

[ApiController, Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/wallet/bank-accounts")]
public sealed class WalletBankAccountsController(IWalletBankingService banking) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => Ok(await banking.AccountsAsync(UserId, ct));
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] BankAccountInput input, CancellationToken ct)
    {
        try { return Ok(await banking.SaveAccountAsync(UserId, input, ct)); }
        catch (KeyNotFoundException) { return NotFound(new { error = "Không tìm thấy tài khoản ngân hàng." }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] BankAccountVersion input, CancellationToken ct)
    {
        try { await banking.DeleteAccountAsync(UserId, id, input.RowVersion, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(new { error = "Không tìm thấy tài khoản ngân hàng." }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
    [HttpPost("{id:guid}/default")]
    public async Task<IActionResult> SetDefault(Guid id, [FromBody] BankAccountVersion input, CancellationToken ct)
    {
        try { await banking.SetDefaultAsync(UserId, id, input.RowVersion, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(new { error = "Không tìm thấy tài khoản ngân hàng." }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
    [HttpPost("read-qr"), RequestSizeLimit(16_384)]
    public IActionResult ReadQr([FromBody] BankQrPayload input)
    {
        try { return Ok(VietQrReader.Read(input.Payload)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
public sealed record BankAccountVersion(string RowVersion);
public sealed record BankQrPayload(string Payload);
