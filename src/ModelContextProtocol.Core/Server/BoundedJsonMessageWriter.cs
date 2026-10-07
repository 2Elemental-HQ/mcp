using ModelContextProtocol.Protocol;
using System.IO.Pipelines;
using System.Text.Json;

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
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 65536, resumeWriterThreshold: 32768, useSynchronizationContext: false));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = Task.Run(async () =>
        {
            Exception? failure = null;
            try
            {
                using var output = new ProducerStream(pipe.Writer, cancellation.Token);
                using var writer = new Utf8JsonWriter(output);
                JsonSerializer.Serialize(writer, message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);
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
