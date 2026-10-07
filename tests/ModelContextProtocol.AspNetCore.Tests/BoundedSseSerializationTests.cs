using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Exercises full tool results through real SSE framing with a guarded asynchronous response stream.
/// </summary>
public sealed class BoundedSseSerializationTests(ITestOutputHelper output) : KestrelInMemoryTest(output)
{
    /// <summary>
    /// Parsed multiline JSON nodes preserve their content when written as one SSE response.
    /// </summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public async Task MultilineJsonNodesRoundTrip(string newline)
    {
        Builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)
            .WithTools([McpServerTool.Create(() => new CallToolResult { Content = [new TextContentBlock { Text = "complete" }] }, new() { Name = "raw" })])
            .WithMessageFilters(filters => filters.AddOutgoingFilter(next => async (context, token) =>
            {
                if (context.JsonRpcMessage is JsonRpcResponse response && response.Result is System.Text.Json.Nodes.JsonObject result && result.ContainsKey("content"))
                {
                    var meta = result["_meta"] as System.Text.Json.Nodes.JsonObject;
                    if (meta is null) result["_meta"] = meta = new System.Text.Json.Nodes.JsonObject();
                    meta["extension"] = System.Text.Json.Nodes.JsonNode.Parse(string.Concat("{", newline, "\"value\":42", newline, "}"));
                }
                await next(context, token);
            }));
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = HttpClient.BaseAddress! }, HttpClient), cancellationToken: TestContext.Current.CancellationToken);
        var result = await client.CallToolAsync("raw", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("complete", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(42, result.Meta!["extension"]!["value"]!.GetValue<int>());
    }

    /// <summary>
    /// Large text uses bounded asynchronous writes and survives both node observation and ordinary filters.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeTextRoundTrips(bool observeNode)
    {
        var text = string.Concat(new string('"', 1048576), "😀é\r\n<>&\\");
        int maximumWrite = 0;
        Builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)
            .WithTools([McpServerTool.Create(() => new CallToolResult { Content = [new TextContentBlock { Text = text }] }, new() { Name = "large" })])
            .WithMessageFilters(filters => filters.AddOutgoingFilter(next => async (context, token) =>
            {
                if (observeNode && context.JsonRpcMessage is JsonRpcResponse response)
                {
                    // Access must continue to return a mutable object node.
                    _ = response.Result;
                }
                await next(context, token);
            }));
        await using var app = Builder.Build();
        app.Use(async (context, next) =>
        {
            var original = context.Response.Body;
            context.Response.Body = new GuardedStream(original, count => maximumWrite = Math.Max(maximumWrite, count));
            try { await next(); }
            finally { context.Response.Body = original; }
        });
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = HttpClient.BaseAddress! }, HttpClient), cancellationToken: TestContext.Current.CancellationToken);
        var result = await client.CallToolAsync("large", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(text, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.InRange(maximumWrite, 1, 65536);
    }

    /// <summary>
    /// A failed destination is observed, its producer exits, and a subsequent request still succeeds.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
#if NET10_0_OR_GREATER
    [InlineData(false, true)]
    [InlineData(true, true)]
#endif
    public async Task DestinationFailureOrCancellationReleasesProducer(bool cancel, bool streaming)
    {
        var text = new string('"', 1048576);
        var blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool inject = false;
        int disposedSources = 0;
        int yieldedSegments = 0;
        ContentBlock content = new TextContentBlock { Text = text };
#if NET10_0_OR_GREATER
        async IAsyncEnumerable<ReadOnlyMemory<char>> Read([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            try
            {
                for (int offset = 0; offset < text.Length; offset += 4096)
                {
                    token.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref yieldedSegments);
                    yield return text.AsMemory(offset, Math.Min(4096, text.Length - offset));
                    await Task.Yield();
                }
            }
            finally { Interlocked.Increment(ref disposedSources); }
        }
        if (streaming) content = new StreamingTextContentBlock(Read);
#endif
        Builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)
            .WithTools([McpServerTool.Create(() => new CallToolResult { Content = [content] }, new() { Name = "large" })]);
        await using var app = Builder.Build();
        app.Use(async (context, next) =>
        {
            if (!inject) { await next(); return; }
            var original = context.Response.Body;
            context.Response.Body = new GuardedStream(original, _ => { }, async count =>
            {
                if (count < 1024) return;
                blocked.TrySetResult(true);
                if (cancel) await Task.Delay(Timeout.Infinite, context.RequestAborted);
                throw new IOException("Injected destination failure.");
            });
            try { await next(); }
            finally { context.Response.Body = original; completed.TrySetResult(true); }
        });
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = HttpClient.BaseAddress! }, HttpClient), cancellationToken: TestContext.Current.CancellationToken);
        inject = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var request = client.CallToolAsync("large", cancellationToken: cancellation.Token).AsTask();
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        if (streaming) Assert.Equal(1, Volatile.Read(ref yieldedSegments));
        if (cancel) cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => request.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.IsNotType<TimeoutException>(error);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        if (streaming) Assert.Equal(1, Volatile.Read(ref disposedSources));
        inject = false;
        var result = await client.CallToolAsync("large", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(text, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    /// <summary>
    /// Rejects synchronous HTTP writes and records asynchronous write sizes without retaining content.
    /// </summary>
    private sealed class GuardedStream(Stream destination, Action<int> observe, Func<int, Task>? beforeWrite = null) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new InvalidOperationException("Synchronous HTTP flush.");
        public override Task FlushAsync(CancellationToken token) => destination.FlushAsync(token);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous HTTP write.");
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            observe(count);
            if (beforeWrite is not null) await beforeWrite(count);
            await destination.WriteAsync(buffer, offset, count, token);
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            observe(buffer.Length);
            if (beforeWrite is not null) await beforeWrite(buffer.Length);
            await destination.WriteAsync(buffer, token);
        }
    }
}
