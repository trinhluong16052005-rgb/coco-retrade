using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoCoReTrade.Tests;

public sealed class GoogleEmailVerificationTests(ForwardedGoogleCallbackTests.PreviewFactory factory)
    : IClassFixture<ForwardedGoogleCallbackTests.PreviewFactory>
{
    private const string VerificationClaim = "coco:google-email-verified";

    [Theory]
    [InlineData("\"email_verified\":true", true)]
    [InlineData("\"email_verified\":false", false)]
    [InlineData("", false)]
    [InlineData("\"verified_email\":true", false)]
    [InlineData("\"EmailConfirmed\":true,\"coco:google-email-verified\":true", false)]
    [InlineData("\"email_verified\":false,\"verified_email\":true,\"EmailConfirmed\":true", false)]
    [InlineData("\"email_verified\":true,\"verified_email\":false", true)]
    [InlineData("\"email_verified\":\"yes\"", false)]
    [InlineData("\"email_verified\":null", false)]
    public void RealGoogleClaimActionsVerifyOnlyAffirmativeV3UserInfo(string verificationMembers, bool expectedVerified)
    {
        var principal = MapUserInfo(verificationMembers);

        // Other normal profile fields must map successfully even for unverified samples.
        Assert.Equal("protocol-test-sub", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("buyer@example.test", principal.FindFirstValue(ClaimTypes.Email));
        var value = principal.FindFirstValue(VerificationClaim);
        var verified = bool.TryParse(value, out var parsed) && parsed;
        Assert.Equal(expectedVerified, verified);
        if (expectedVerified) Assert.NotNull(value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GoogleVerificationSurvivesExternalAuthenticationTicketRoundTrip(bool verified)
    {
        var principal = MapUserInfo("\"email_verified\":" + (verified ? "true" : "false"));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), IdentityConstants.ExternalScheme);

        var restored = TicketSerializer.Default.Deserialize(TicketSerializer.Default.Serialize(ticket));

        Assert.NotNull(restored);
        Assert.Equal(IdentityConstants.ExternalScheme, restored.AuthenticationScheme);
        var claim = Assert.Single(restored.Principal.FindAll(VerificationClaim));
        Assert.True(bool.TryParse(claim.Value, out var parsed));
        Assert.Equal(verified, parsed);
        Assert.Equal("buyer@example.test", restored.Principal.FindFirstValue(ClaimTypes.Email));
    }

    private ClaimsPrincipal MapUserInfo(string verificationMembers)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get("Google");
        var json = "{\"sub\":\"protocol-test-sub\",\"email\":\"buyer@example.test\",\"name\":\"Protocol test\"" +
            (verificationMembers.Length == 0 ? "" : "," + verificationMembers) + "}";
        using var document = JsonDocument.Parse(json);
        var identity = new ClaimsIdentity("Google", ClaimTypes.Name, ClaimTypes.Role);
        foreach (var action in options.ClaimActions)
            action.Run(document.RootElement, identity, "Google");
        return new ClaimsPrincipal(identity);
    }
}
