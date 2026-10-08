#if NET10_0_OR_GREATER
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    [InlineData(1, false)]
    [InlineData(4096, true)]
    [InlineData(100000, false)]
    public async Task SegmentedTextPreservesCompleteResult(int segmentLength, bool isError)
    {
        Builder.Logging.SetMinimumLevel(LogLevel.Trace);
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
        var source = new StreamingTextContentBlock(Read) { Meta = new JsonObject { ["source"] = "synthetic" }, Annotations = new() { Priority = 0.75f, Audience = [Role.User] } };
        Assert.Equal(0, opened);
        var sharedResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "before" }, source, new TextContentBlock { Text = "after" }],
            IsError = isError,
            StructuredContent = JsonSerializer.SerializeToElement(new { complete = true }),
            Meta = new JsonObject { ["result"] = "preserved" },
        };
        Builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)
            .WithTools([McpServerTool.Create(() => sharedResult, new() { Name = "stream" })]);
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
            Assert.Equal(0.75f, actual.Annotations!.Priority);
            Assert.Equal(Role.User, Assert.Single(actual.Annotations.Audience!));
            Assert.Equal("after", Assert.IsType<TextContentBlock>(result.Content[2]).Text);
            Assert.Equal(isError, result.IsError);
            Assert.True(result.StructuredContent!.Value.GetProperty("complete").GetBoolean());
            Assert.Equal("preserved", result.Meta!["result"]!.GetValue<string>());
        }
        Assert.Single(sharedResult.Meta!);
        Assert.Equal(3, opened);
        Assert.Equal(opened, disposed);
    }
    /// <summary>
    /// A source that faults or is cancelled cannot complete a partial result and releases its resources.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceFailureDisposesEnumerationAndAllowsNextRequest(bool cancel)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int enumerations = 0;
        async IAsyncEnumerable<ReadOnlyMemory<char>> Read([EnumeratorCancellation] CancellationToken token)
        {
            bool first = Interlocked.Increment(ref enumerations) == 1;
            try
            {
                yield return "complete".AsMemory();
                if (first)
                {
                    entered.TrySetResult();
                    if (cancel) await Task.Delay(Timeout.Infinite, token);
                    throw new IOException("Synthetic source failure after partial output.");
                }
            }
            finally { if (first) disposed.TrySetResult(); }
        }
        Builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)
            .WithTools([McpServerTool.Create(() => new CallToolResult { Content = [new StreamingTextContentBlock(Read)] }, new() { Name = "stream" })]);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = HttpClient.BaseAddress! }, HttpClient), cancellationToken: TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var request = client.CallToolAsync("stream", cancellationToken: cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        if (cancel) cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => request.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.IsNotType<TimeoutException>(error);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var result = await client.CallToolAsync("stream", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("complete", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(2, enumerations);
    }

}
#endif
