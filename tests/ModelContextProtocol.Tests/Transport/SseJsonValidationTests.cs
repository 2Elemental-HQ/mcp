using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Transport;

/// <summary>
/// Checks malformed SSE payload handling in the byte-oriented JSON-RPC parser.
/// </summary>
public sealed class SseJsonValidationTests
{
    /// <summary>
    /// Invalid or incomplete JSON events are rejected without preventing the following valid response.
    /// </summary>
    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\"")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{} } true")]
    [InlineData("{\"jsonrpc\":\"9.9\",\"id\":1,\"result\":{} }")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":[1,] }")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task InvalidEventDoesNotHideValidResponse(string malformed)
    {
        using var handler = new MockHttpHandler();
        handler.RequestHandler = async request =>
        {
            if (request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (root.GetProperty("method").GetString() == "initialize")
            {
                var version = root.GetProperty("params").GetProperty("protocolVersion").GetString();
                var json = $"{{\"jsonrpc\":\"2.0\",\"id\":{id.GetRawText()},\"result\":{{\"protocolVersion\":\"{version}\",\"capabilities\":{{\"tools\":{{}}}},\"serverInfo\":{{\"name\":\"test\",\"version\":\"1\"}}}}}}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            var valid = $"{{\"jsonrpc\":\"2.0\",\"id\":{id.GetRawText()},\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":\"complete\"}}]}}}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"data: {malformed}\n\ndata: {valid}\n\n", Encoding.UTF8, "text/event-stream") };
        };
        using var http = new HttpClient(handler);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = new Uri("http://localhost/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http), cancellationToken: TestContext.Current.CancellationToken);
        var result = await client.CallToolAsync("test", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("complete", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }
}
