using System.Security.Claims;
using CoCoReTrade.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoCoReTrade.Web.Controllers.Api.V1;

[ApiController]
[Authorize]
[Route("api/v1/wallet")]
public sealed class WalletController(IWalletService wallets, IWalletBankingService banking) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<ActionResult<WalletBalanceDto>> Balance(CancellationToken ct) =>
        Ok(await wallets.BalanceAsync(UserId, ct));
    [HttpGet("withdrawals")]
    public async Task<ActionResult<IReadOnlyList<WithdrawalDto>>> Withdrawals(CancellationToken ct) =>
        Ok(await wallets.WithdrawalsAsync(UserId, ct));
    [HttpGet("withdrawal-history"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> History(string? search, string? status, int page = 1, CancellationToken ct = default)
    {
        try { return Ok(await banking.HistoryAsync(UserId, search, status, page, 10, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
    [HttpPost("withdrawal-requests")]
    public async Task<IActionResult> Submit([FromBody] WalletWithdrawalInput input, CancellationToken ct)
    {
        try { return Ok(await banking.SubmitAsync(UserId, input, ct)); }
        catch (KeyNotFoundException) { return NotFound(new { error = "Không tìm thấy tài khoản ngân hàng." }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
    [HttpPost("withdrawals")]
    public async Task<ActionResult<WithdrawalDto>> RequestWithdrawal(
        [FromBody] CreateWithdrawalRequest request, CancellationToken ct)
    {
        try
        {
            var result = await wallets.RequestWithdrawalAsync(UserId, request.RequestKey,
                request.Amount, request.BankAccountNumber, request.BankName, ct);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

public sealed record CreateWithdrawalRequest(Guid RequestKey, decimal Amount,
    string BankAccountNumber, string BankName);
