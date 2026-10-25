using System.Net;
using System.Text;

namespace DevPulse.Tests.Unit.Infrastructure.Digest;

/// <summary>
/// Fake HttpMessageHandler: zwraca przygotowaną odpowiedź i zapamiętuje żądanie (nagłówki i body) do asercji.
/// Nic nie wychodzi do sieci.
/// </summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastRequestBody { get; private set; }

    public int CallCount { get; private set; }

    public static StubHttpMessageHandler Returning(HttpStatusCode status, string? body = null, Action<HttpResponseMessage>? configure = null) =>
        new((_, _) =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json"),
            };
            configure?.Invoke(response);
            return Task.FromResult(response);
        });

    public static string Fixture(string provider, string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", provider, name));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await respond(request, cancellationToken);
    }
}
