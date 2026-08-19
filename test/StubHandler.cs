using System.Net;
using System.Net.Http;

namespace MemMesh.Tests;

/// <summary>An <see cref="HttpMessageHandler"/> that replays a scripted sequence
/// of responses and records the requests it saw — so tests can drive the client's
/// retry / interceptor / pagination behavior without a real server.</summary>
internal sealed class StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps)
    : HttpMessageHandler
{
    private int _i;
    public List<HttpRequestMessage> Requests { get; } = [];
    public int Calls => _i;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body now — the request is disposed once we return.
        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);
        Requests.Add(request);

        var step = steps[Math.Min(_i, steps.Length - 1)];
        _i++;
        return step(request);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
