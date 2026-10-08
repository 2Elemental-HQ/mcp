using ModelContextProtocol.Protocol;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <summary>
/// Bridges synchronous JSON converters to an asynchronous transport through a bounded pipe.
/// </summary>
internal static class BoundedJsonMessageWriter
{
    /// <summary>
    /// Writes one compact JSON message, propagating cancellation and failures in both directions.
    /// </summary>
    internal static async Task WriteAsync(JsonRpcMessage? message, Stream destination, CancellationToken cancellationToken)
    {
        if (message is null) return;
#if NET10_0_OR_GREATER
        if (message is JsonRpcResponse response && response.TypedResult is CallToolResult result &&
            result.Content.Any(block => block is StreamingTextContentBlock))
        {
            await StreamingJsonMessageWriter.WriteAsync(response, result, destination, cancellationToken).ConfigureAwait(false);
            return;
        }
#endif
        await WriteValueAsync(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage, destination, cancellationToken).ConfigureAwait(false);
    }

    internal static Task WriteValueAsync<T>(T value, JsonTypeInfo<T> typeInfo, Stream destination, CancellationToken cancellationToken) =>
        WriteCoreAsync(writer => JsonSerializer.Serialize(writer, value, typeInfo), destination, cancellationToken);

    private static async Task WriteCoreAsync(Action<Utf8JsonWriter> serialize, Stream destination, CancellationToken cancellationToken)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 65536, resumeWriterThreshold: 32768, useSynchronizationContext: false));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = Task.Run(async () =>
        {
            Exception? failure = null;
            try
            {
                using var output = new ProducerStream(pipe.Writer, cancellation.Token);
                using var writer = new Utf8JsonWriter(output);
                serialize(writer);
            }
            catch (Exception error) { failure = error; throw; }
            finally { await pipe.Writer.CompleteAsync(failure).ConfigureAwait(false); }
        }, CancellationToken.None);
        try
        {
            using var input = pipe.Reader.AsStream(leaveOpen: true);
            await input.CopyToAsync(destination, 16384, cancellation.Token).ConfigureAwait(false);
            await producer.ConfigureAwait(false);
        }
        finally
        {
            cancellation.Cancel();
            await pipe.Reader.CompleteAsync().ConfigureAwait(false);
            // Observe the producer even when the consumer failed; never leave a blocked producer behind.
            try { await producer.ConfigureAwait(false); } catch when (cancellation.IsCancellationRequested) { }
        }
    }

    /// <summary>
    /// Applies pipe backpressure and cancellation to synchronous converter writes.
    /// </summary>
    private sealed class ProducerStream(PipeWriter writer, CancellationToken cancellationToken) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => cancellationToken.ThrowIfCancellationRequested();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (count > 0)
            {
                int length = Math.Min(count, 16384);
                var result = writer.WriteAsync(buffer.AsMemory(offset, length), cancellationToken).AsTask().GetAwaiter().GetResult();
                if (result.IsCanceled) throw new OperationCanceledException(cancellationToken);
                if (result.IsCompleted) throw new IOException("The JSON consumer stopped reading.");
                offset += length;
                count -= length;
            }
        }
    }
}
