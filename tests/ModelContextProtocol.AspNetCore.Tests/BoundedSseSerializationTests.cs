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
    [InlineData(false)]
    [InlineData(true)]
    public async Task DestinationFailureOrCancellationReleasesProducer(bool cancel)
    {
        var text = new string('"', 1048576);
        var blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool inject = false;
        Builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)
            .WithTools([McpServerTool.Create(() => new CallToolResult { Content = [new TextContentBlock { Text = text }] }, new() { Name = "large" })]);
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
        if (cancel) cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => request.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.IsNotType<TimeoutException>(error);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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
