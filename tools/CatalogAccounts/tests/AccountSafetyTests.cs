using CoCoReTrade.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace CoCoReTrade.CatalogAccounts.Tests;

public sealed class AccountSafetyTests
{
    [Fact]
    public void DefaultIsReadOnlyAndUsesIntegratedAuthentication()
    {
        var options = AccountOptions.Parse(["--database", "CoCoReTrade"]);
        Assert.False(options.Apply);
        var connection = new SqlConnectionStringBuilder(options.ConnectionString());
        Assert.True(connection.IntegratedSecurity);
        Assert.Equal("CoCoReTrade", connection.InitialCatalog);
        Assert.Empty(connection.UserID);
        Assert.Empty(connection.Password);
    }

    [Theory]
    [InlineData("CoCoReTradeDemo")]
    [InlineData("master")]
    [InlineData("CoCoReTrade;Integrated Security=false")]
    public void RefusesOtherDatabases(string database) =>
        Assert.Throws<AccountToolException>(() => AccountOptions.Parse(["--database", database, "--apply"]));

    [Theory]
    [InlineData("remote.example.com")]
    [InlineData(".\\SQLEXPRESS;User ID=sa")]
    [InlineData("192.168.0.2")]
    public void RefusesRemoteOrInjectedServers(string server) =>
        Assert.Throws<AccountToolException>(() => AccountOptions.Parse(["--database", "CoCoReTrade", "--server", server]));

    [Fact]
    public void RefusesMissingRepeatedOrUnrecognizedOptions()
    {
        Assert.Throws<AccountToolException>(() => AccountOptions.Parse([]));
        Assert.Throws<AccountToolException>(() => AccountOptions.Parse(["--database", "CoCoReTrade", "--apply", "--apply"]));
        Assert.Throws<AccountToolException>(() => AccountOptions.Parse(["--database", "CoCoReTrade", "--output", "anything"]));
    }

    [Fact]
    public void PlanDoesNotGrantStaffRolesToSellerOrBuyer()
    {
        var plan = AccountPlan.Build();
        Assert.Equal(10, plan.Count);
        Assert.Equal(10, plan.Select(account => account.Email).Distinct().Count());
        Assert.Equal(8, plan.Count(account => account.Email.StartsWith("seller", StringComparison.Ordinal)));
        Assert.All(plan.Where(account => !account.Email.StartsWith("admin", StringComparison.Ordinal)),
            account => Assert.Equal(["User"], account.Roles));
        Assert.Equal(["User", "Inspector", "Admin"], plan.Single(account => account.Email.StartsWith("admin", StringComparison.Ordinal)).Roles);
    }

    [Fact]
    public void PasswordsAreUniqueStrongAndCompatibleWithAppIdentityHasher()
    {
        var passwords = Enumerable.Range(0, 100).Select(_ => AccountPlan.NewPassword()).ToArray();
        Assert.Equal(passwords.Length, passwords.Distinct().Count());
        Assert.All(passwords, password =>
        {
            Assert.Equal(28, password.Length);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, character => !char.IsLetterOrDigit(character));
            Assert.DoesNotContain('\t', password);
            Assert.DoesNotContain('\n', password);
        });
        var user = new ApplicationUser();
        var hasher = new PasswordHasher<ApplicationUser>();
        var hash = hasher.HashPassword(user, passwords[0]);
        Assert.NotEqual(passwords[0], hash);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, hash, passwords[0]));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, passwords[1]));
    }

    [Fact]
    public void PrivateOutputRefusesExistingFilesAndGitDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "coco-account-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "accounts.txt");
            File.WriteAllText(output, "no credentials");
            Assert.Throws<AccountToolException>(() => PrivateAccountOutput.ValidateCandidate(output));
            File.Delete(output);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Assert.Throws<AccountToolException>(() => PrivateAccountOutput.ValidateCandidate(output));
        }
        finally
        {
            // Only this test-created, resolved temp directory is removed.
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root, recursive: true);
        }
    }
}
