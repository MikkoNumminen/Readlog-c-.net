using System.Net;
using ReadLog.Tests.Infrastructure;

namespace ReadLog.Tests.Auth;

/// <summary>
/// End-to-end auth tests: they drive the real Login/Register/Logout Razor Pages over
/// HTTP (antiforgery tokens and cookies included) against an isolated temp database.
/// </summary>
public class AuthenticationTests : IClassFixture<ReadLogAppFactory>
{
    private readonly ReadLogAppFactory _factory;

    public AuthenticationTests(ReadLogAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Signin_page_renders_the_login_form()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/signin");

        Assert.Contains("Sign in", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task Register_page_renders_the_form()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/register");

        Assert.Contains("Create account", html);
    }

    [Fact]
    public async Task Registering_signs_the_user_in()
    {
        var client = _factory.CreateClient();

        await RegisterAsync(client, "newuser@example.com", "Password1");

        var home = await client.GetStringAsync("/");
        Assert.Contains("Sign out", home);
        Assert.Contains("newuser@example.com", home);
    }

    [Fact]
    public async Task Register_logout_login_round_trip_restores_the_session()
    {
        var client = _factory.CreateClient();
        const string email = "roundtrip@example.com";
        const string password = "Password1";

        await RegisterAsync(client, email, password);
        await LogoutAsync(client);

        var loggedOut = await client.GetStringAsync("/");
        Assert.DoesNotContain("Sign out", loggedOut);
        Assert.Contains("Sign in", loggedOut);

        await LoginAsync(client, email, password);

        var loggedIn = await client.GetStringAsync("/");
        Assert.Contains("Sign out", loggedIn);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_rejected()
    {
        var client = _factory.CreateClient();
        const string email = "wrongpass@example.com";
        await RegisterAsync(client, email, "Password1");
        await LogoutAsync(client);

        var response = await PostFormAsync(client, "/signin", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = "WrongPassword9",
            ["ReturnUrl"] = "/",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // stayed on the login page
        Assert.Contains("Invalid login attempt", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registering_with_a_mismatched_confirmation_is_rejected()
    {
        var client = _factory.CreateClient();

        var response = await PostFormAsync(client, "/register", new Dictionary<string, string>
        {
            ["Input.Email"] = "mismatch@example.com",
            ["Input.Password"] = "Password1",
            ["Input.ConfirmPassword"] = "Password2",
            ["ReturnUrl"] = "/",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("passwords do not match", await response.Content.ReadAsStringAsync());
    }

    private static async Task RegisterAsync(HttpClient client, string email, string password)
    {
        var response = await PostFormAsync(client, "/register", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
            ["ReturnUrl"] = "/",
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var response = await PostFormAsync(client, "/signin", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["ReturnUrl"] = "/",
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task LogoutAsync(HttpClient client)
    {
        var response = await PostFormAsync(client, "/signout", new Dictionary<string, string>());
        response.EnsureSuccessStatusCode();
    }

    /// <summary>GETs the page to grab the antiforgery token + cookie, then POSTs the form.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string url, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(url);
        fields["__RequestVerificationToken"] = HtmlFormHelper.ExtractAntiforgeryToken(html);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }
}
