#if NET10_0_OR_GREATER
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Verifies incremental sources through the ordinary HTTP tool-result contract.
/// </summary>
public sealed class StreamingTextContentTests(ITestOutputHelper output) : KestrelInMemoryTest(output)
{
    /// <summary>
    /// Arbitrary UTF-16 boundaries preserve escaping, metadata, and replayable source ownership.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(100000)]
    public async Task SegmentedTextPreservesCompleteResult(int segmentLength)
    {
        var text = string.Concat(new string('"', 20000), "😀é\r\n<>&\\\ud800x\udc00");
        int opened = 0;
        int disposed = 0;
        async IAsyncEnumerable<ReadOnlyMemory<char>> Read([EnumeratorCancellation] CancellationToken token)
        {
            opened++;
            try
            {
                for (int offset = 0; offset < text.Length; offset += segmentLength)
                {
                    token.ThrowIfCancellationRequested();
                    yield return text.AsMemory(offset, Math.Min(segmentLength, text.Length - offset));
                    await Task.Yield();
                }
            }
            finally { disposed++; }
        }
        var source = new StreamingTextContentBlock(Read) { Meta = new JsonObject { ["source"] = "synthetic" } };
        Assert.Equal(0, opened);
        Builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)
            .WithTools([McpServerTool.Create(() => new CallToolResult
            {
                Content = [new TextContentBlock { Text = "before" }, source, new TextContentBlock { Text = "after" }],
                IsError = false,
                StructuredContent = JsonSerializer.SerializeToElement(new { complete = true }),
                Meta = new JsonObject { ["result"] = "preserved" },
            }, new() { Name = "stream" })]);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = HttpClient.BaseAddress! }, HttpClient), cancellationToken: TestContext.Current.CancellationToken);
        // JSON replaces unpaired surrogates, just as the existing string API does.
        var expected = JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(text));
        for (int iteration = 0; iteration < 3; iteration++)
        {
            var result = await client.CallToolAsync("stream", cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(3, result.Content.Count);
            Assert.Equal("before", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
            var actual = Assert.IsType<TextContentBlock>(result.Content[1]);
            Assert.Equal(expected, actual.Text);
            Assert.Equal("synthetic", actual.Meta!["source"]!.GetValue<string>());
            Assert.Equal("after", Assert.IsType<TextContentBlock>(result.Content[2]).Text);
            Assert.False(result.IsError);
            Assert.True(result.StructuredContent!.Value.GetProperty("complete").GetBoolean());
            Assert.Equal("preserved", result.Meta!["result"]!.GetValue<string>());
        }
        Assert.Equal(3, opened);
        Assert.Equal(opened, disposed);
    }
}
#endif
