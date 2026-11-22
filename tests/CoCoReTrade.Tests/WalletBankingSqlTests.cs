using System.Net;
using System.Net.Http.Json;
using CoCoReTrade.Application;
using Microsoft.EntityFrameworkCore;
using static CoCoReTrade.Tests.AdminSqlAuditFactory;

namespace CoCoReTrade.Tests;

// Opt-in; uses a brand-new SQL database and real HTTP requests, never the live database.
public sealed class WalletBankingSqlTests
{
    [Fact]
    public async Task BankCrudWithdrawalSnapshotsHistoryAuthorizationAndAtomicityPersistInSql()
    {
        if (Environment.GetEnvironmentVariable("COCO_RUN_SQL_TESTS") != "1") return;
        await using var factory = new AdminSqlAuditFactory();
        using var bootstrap = factory.CreateClient();
        try
        {
            await factory.InitializeUsersAsync();
            using var seller = await factory.LoginAsync("seller"); using var buyer = await factory.LoginAsync("buyer");
            using var admin = await factory.LoginAsync("admin");
            const string accountsUrl = "/api/v1/wallet/bank-accounts";
            var first = await Success<SavedBankAccountDto>(await seller.PostAsJsonAsync(accountsUrl,
                new BankAccountInput(null, "Vietcombank", "0123456789", "NGUYEN VAN A")));
            var second = await Success<SavedBankAccountDto>(await seller.PostAsJsonAsync(accountsUrl,
                new BankAccountInput(null, "ACB", "1234567890", "NGUYEN VAN A")));
            Assert.True(first.IsDefault); Assert.False(second.IsDefault);
            Assert.Equal(2, await factory.ReadAsync(db => db.SavedBankAccounts.CountAsync(x => x.UserId == "seller")));
            Assert.Empty((await buyer.GetFromJsonAsync<List<SavedBankAccountDto>>(accountsUrl))!);
            Assert.Equal(HttpStatusCode.NotFound, (await buyer.PostAsJsonAsync(accountsUrl,
                new BankAccountInput(first.Id, "Vietcombank", "0123456789", "OTHER", false, first.RowVersion))).StatusCode);
            using (var deletion = new HttpRequestMessage(HttpMethod.Delete, accountsUrl + "/" + first.Id)
                   { Content = JsonContent.Create(new { first.RowVersion }) })
                Assert.Equal(HttpStatusCode.NotFound, (await buyer.SendAsync(deletion)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await seller.PostAsJsonAsync(accountsUrl + "/" + second.Id + "/default", new { second.RowVersion })).StatusCode);
            var saved = (await seller.GetFromJsonAsync<List<SavedBankAccountDto>>(accountsUrl))!;
            first = saved.Single(x => x.Id == first.Id); second = saved.Single(x => x.Id == second.Id);
            Assert.True(second.IsDefault); Assert.Single(saved, x => x.IsDefault);
            var stale = first.RowVersion;
            first = await Success<SavedBankAccountDto>(await seller.PostAsJsonAsync(accountsUrl,
                new BankAccountInput(first.Id, first.BankName, first.AccountNumber, "NGUYEN VAN B", false, first.RowVersion)));
            Assert.Equal(HttpStatusCode.Conflict, (await seller.PostAsJsonAsync(accountsUrl,
                new BankAccountInput(first.Id, first.BankName, first.AccountNumber, "STALE NAME", false, stale))).StatusCode);
            Assert.Equal("NGUYEN VAN B", await factory.ReadAsync(db => db.SavedBankAccounts.Where(x => x.Id == first.Id).Select(x => x.AccountHolder).SingleAsync()));

            var request = new WalletWithdrawalInput(Guid.NewGuid(), 25_000, first.BankName, first.AccountNumber,
                first.AccountHolder, first.Id, first.RowVersion);
            const string requestUrl = "/api/v1/wallet/withdrawal-requests";
            var withdrawal = await Success<WithdrawalDto>(await seller.PostAsJsonAsync(requestUrl, request));
            var replay = await Success<WithdrawalDto>(await seller.PostAsJsonAsync(requestUrl, request));
            Assert.Equal(withdrawal.Id, replay.Id);
            Assert.Equal(475_000, await factory.ReadAsync(db => db.Wallets.Where(x => x.UserId == "seller").Select(x => x.Balance).SingleAsync()));
            Assert.Equal(1, await factory.ReadAsync(db => db.WalletTransactions.CountAsync(x => x.WithdrawalRequestId == withdrawal.Id)));
            using (var deletion = new HttpRequestMessage(HttpMethod.Delete, accountsUrl + "/" + first.Id)
                   { Content = JsonContent.Create(new { first.RowVersion }) })
                Assert.Equal(HttpStatusCode.NoContent, (await seller.SendAsync(deletion)).StatusCode);
            Assert.Equal("NGUYEN VAN B", await factory.ReadAsync(db => db.WithdrawalRequests.Where(x => x.Id == withdrawal.Id).Select(x => x.BankAccountHolder).SingleAsync()));
            Assert.Equal(withdrawal.Id, (await Success<WithdrawalDto>(await seller.PostAsJsonAsync(requestUrl, request))).Id);
            var current = (await seller.GetFromJsonAsync<List<SavedBankAccountDto>>(accountsUrl))!.Single();
            Assert.True(current.IsDefault);
            Assert.Equal(HttpStatusCode.NotFound, (await buyer.PostAsJsonAsync(requestUrl, request with { RequestKey = Guid.NewGuid(), SavedAccountId = current.Id, SavedAccountVersion = current.RowVersion, BankName = current.BankName, AccountNumber = current.AccountNumber, AccountHolder = current.AccountHolder })).StatusCode);

            // Failure after a saved account is inserted must roll it back together with the reserved funds.
            factory.Failures.AfterSaves = 1;
            var failed = await seller.PostAsJsonAsync(requestUrl, new WalletWithdrawalInput(Guid.NewGuid(), 1_000,
                "BIDV", "9999999999", "ROLLBACK HOLDER", SaveAccount: true));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.DoesNotContain("Sensitive SQL", await failed.Content.ReadAsStringAsync());
            Assert.Equal(1, await factory.ReadAsync(db => db.SavedBankAccounts.CountAsync()));
            Assert.Equal(475_000, await factory.ReadAsync(db => db.Wallets.Where(x => x.UserId == "seller").Select(x => x.Balance).SingleAsync()));
            Assert.Equal(1, await factory.ReadAsync(db => db.WithdrawalRequests.CountAsync()));
            Assert.Equal(HttpStatusCode.BadRequest, (await seller.PostAsJsonAsync(requestUrl, new WalletWithdrawalInput(Guid.NewGuid(), 999_999,
                "BIDV", "9999999999", "ROLLBACK HOLDER", SaveAccount: true))).StatusCode);
            Assert.Equal(1, await factory.ReadAsync(db => db.SavedBankAccounts.CountAsync()));

            var noSave = await Success<WithdrawalDto>(await seller.PostAsJsonAsync(requestUrl,
                new WalletWithdrawalInput(Guid.NewGuid(), 1_000, "BIDV", "9999999999", "NGUYEN VAN C")));
            Assert.Equal(1, await factory.ReadAsync(db => db.SavedBankAccounts.CountAsync()));
            await Success<WithdrawalDto>(await seller.PostAsJsonAsync(requestUrl,
                new WalletWithdrawalInput(Guid.NewGuid(), 1_000, "BIDV", "9999999999", "NGUYEN VAN C", SaveAccount: true)));
            Assert.Equal(2, await factory.ReadAsync(db => db.SavedBankAccounts.CountAsync()));
            for (var i = 0; i < 10; i++) await Success<WithdrawalDto>(await seller.PostAsJsonAsync(requestUrl,
                new WalletWithdrawalInput(Guid.NewGuid(), 1_000, "ACB", "1234567890", "NGUYEN VAN A")));
            var home = await seller.GetStringAsync("/Profile/Wallet");
            Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(home, "data-withdrawal-id=").Count);
            var all = (await seller.GetFromJsonAsync<WithdrawalPage>("/api/v1/wallet/withdrawal-history"))!;
            Assert.Equal(13, all.Total); Assert.Equal(10, all.Items.Count); Assert.Equal(2, all.TotalPages);
            Assert.Equal(3, (await seller.GetFromJsonAsync<WithdrawalPage>("/api/v1/wallet/withdrawal-history?page=2"))!.Items.Count);
            Assert.Equal(2, (await seller.GetFromJsonAsync<WithdrawalPage>("/api/v1/wallet/withdrawal-history?search=BIDV"))!.Total);
            Assert.Empty((await buyer.GetFromJsonAsync<WithdrawalPage>("/api/v1/wallet/withdrawal-history"))!.Items);

            await Success<WithdrawalDto>(await admin.PostAsync($"/api/v1/admin/wallets/withdrawals/{withdrawal.Id}/approve", null));
            Assert.Equal("Approved", await factory.ReadAsync(db => db.WithdrawalRequests.Where(x => x.Id == withdrawal.Id).Select(x => x.Status).SingleAsync()));
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/admin/wallets/withdrawals/{withdrawal.Id}/confirm-transfer", new { Confirmed = false, TransferReference = "TEST-TRANSFER" })).StatusCode);
            Assert.Equal("Approved", await factory.ReadAsync(db => db.WithdrawalRequests.Where(x => x.Id == withdrawal.Id).Select(x => x.Status).SingleAsync()));
            await Success<WithdrawalDto>(await admin.PostAsJsonAsync($"/api/v1/admin/wallets/withdrawals/{withdrawal.Id}/confirm-transfer", new { Confirmed = true, TransferReference = "TEST-TRANSFER" }));
            Assert.Equal("Transferred", await factory.ReadAsync(db => db.WithdrawalRequests.Where(x => x.Id == withdrawal.Id).Select(x => x.Status).SingleAsync()));
            Assert.Equal(1, await factory.ReadAsync(db => db.WalletTransactions.CountAsync(x => x.WithdrawalRequestId == withdrawal.Id && x.Type == "WithdrawalSettled")));
            Assert.Equal(1, (await seller.GetFromJsonAsync<WithdrawalPage>("/api/v1/wallet/withdrawal-history?status=Transferred"))!.Total);
            await Success<WithdrawalDto>(await admin.PostAsJsonAsync($"/api/v1/admin/wallets/withdrawals/{noSave.Id}/reject", new { Reason = "Thông tin cần kiểm tra lại" }));
            Assert.Equal(464_000, await factory.ReadAsync(db => db.Wallets.Where(x => x.UserId == "seller").Select(x => x.Balance).SingleAsync()));
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/admin/wallets/withdrawals/{noSave.Id}/reject", new { Reason = "Không hoàn tiền hai lần" })).StatusCode);
            var qr = await seller.PostAsJsonAsync(accountsUrl + "/read-qr", new { Payload = BankQrReaderTests.Payload() });
            Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await seller.PostAsJsonAsync(accountsUrl + "/read-qr", new { Payload = "not a QR" })).StatusCode);
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => seller.PostAsJsonAsync(requestUrl,
                new WalletWithdrawalInput(Guid.NewGuid(), 300_000, "ACB", "1234567890", "NGUYEN VAN A"))));
            Assert.Single(concurrent, x => x.IsSuccessStatusCode);
            Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.BadRequest);
            Assert.Equal(164_000, await factory.ReadAsync(db => db.Wallets.Where(x => x.UserId == "seller").Select(x => x.Balance).SingleAsync()));
            Assert.Contains("no-store", string.Join(',', (await seller.GetAsync("/Profile/Wallet")).Headers.GetValues("Cache-Control")));
        }
        finally { await factory.DeleteDatabaseAsync(); }
    }
}
