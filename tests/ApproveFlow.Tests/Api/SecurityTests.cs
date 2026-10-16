using System.Net;
using System.Text.RegularExpressions;
using ApproveFlow.Tests.TestInfrastructure;

namespace ApproveFlow.Tests.Api;

/// <summary>HTTP-level security controls: headers, anti-forgery, authorization redirects, lockout.</summary>
public sealed partial class SecurityTests : IClassFixture<ApproveFlowApiFactory>
{
    private readonly ApproveFlowApiFactory _factory;

    public SecurityTests(ApproveFlowApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/api/v1/loan-applications")]
    public async Task Responses_carry_security_headers(string path)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(path);

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Ui_pages_redirect_anonymous_users_to_login()
    {
        using var client = HttpsClient(allowAutoRedirect: false);
        var response = await client.GetAsync("/LoanApplications");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_post_without_antiforgery_token_is_rejected()
    {
        using var client = HttpsClient();
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@demo.lk",
            ["Password"] = SqliteTestHost.DemoPassword,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Five_failed_sign_ins_lock_the_account()
    {
        using var client = HttpsClient();
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var page = await client.GetStringAsync("/Account/Login");
            var token = AntiforgeryField().Match(page).Groups[1].Value;
            last = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "maker2@demo.lk",
                ["Password"] = "Definitely-Wrong#1",
                ["__RequestVerificationToken"] = token,
            }));
        }

        var html = await last!.Content.ReadAsStringAsync();
        Assert.Contains("temporarily locked", html, StringComparison.Ordinal);
    }

    /// <summary>Cookies are Secure-only outside Development, so UI flows are exercised over HTTPS.</summary>
    private HttpClient HttpsClient(bool allowAutoRedirect = true) =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = allowAutoRedirect,
        });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
