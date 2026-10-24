using System.Net;
using System.Text;

namespace DevPulse.Tests.Integration.TestSupport;

/// <summary>
/// Backchannel handlera GitHub OAuth: wymiana kodu na token, profil i adresy email z fixture'ów JSON.
/// Każde inne żądanie kończy test – nic nie wychodzi do prawdziwego GitHuba.
/// </summary>
public sealed class FakeGitHubOAuthHandler : HttpMessageHandler
{
    private readonly List<string> _requests = [];

    public IReadOnlyList<string> Requests => _requests;

    public string? LastTokenRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Żądanie bez adresu.");
        _requests.Add($"{request.Method} {uri.Host}{uri.AbsolutePath}");

        var fixture = (request.Method.Method, uri.Host, uri.AbsolutePath) switch
        {
            ("POST", "github.com", "/login/oauth/access_token") => "oauth-access-token.json",
            ("GET", "api.github.com", "/user") => "user.json",
            ("GET", "api.github.com", "/user/emails") => "user-emails.json",
            _ => throw new InvalidOperationException($"Nieoczekiwane żądanie do GitHuba: {request.Method} {uri}"),
        };

        if (request.Content is not null)
        {
            LastTokenRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GitHub", fixture), cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
