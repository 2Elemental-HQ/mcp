#if NET10_0_OR_GREATER
using ModelContextProtocol.Protocol;
using System.Text;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <summary>
/// Writes streaming tool text as one ordinary JSON-RPC result, awaiting every bounded transport flush.
/// </summary>
internal static class StreamingJsonMessageWriter
{
    internal static async Task WriteAsync(JsonRpcResponse response, CallToolResult result, Stream destination, CancellationToken token)
    {
        await WriteSyntaxAsync(destination, "{\"result\":{\"content\":[", token).ConfigureAwait(false);
        for (int index = 0; index < result.Content.Count; index++)
        {
            if (index != 0) await WriteSyntaxAsync(destination, ",", token).ConfigureAwait(false);
            var block = result.Content[index];
            if (block is StreamingTextContentBlock streaming) await WriteTextAsync(streaming, destination, token).ConfigureAwait(false);
            else await BoundedJsonMessageWriter.WriteValueAsync(block, McpJsonUtilities.JsonContext.Default.ContentBlock, destination, token).ConfigureAwait(false);
        }
        await WriteSyntaxAsync(destination, "]", token).ConfigureAwait(false);
        if (result.StructuredContent is { } structured)
            await WritePropertyAsync("structuredContent", structured, McpJsonUtilities.JsonContext.Default.JsonElement, destination, token).ConfigureAwait(false);
        if (result.IsError is { } error)
            await WritePropertyAsync("isError", error, McpJsonUtilities.JsonContext.Default.Boolean, destination, token).ConfigureAwait(false);
        if (result.Meta is { } meta)
            await WritePropertyAsync("_meta", meta, McpJsonUtilities.JsonContext.Default.JsonObject, destination, token).ConfigureAwait(false);
        if (result.ResultType is { } resultType)
            await WritePropertyAsync("resultType", resultType, McpJsonUtilities.JsonContext.Default.String, destination, token).ConfigureAwait(false);
        await WriteSyntaxAsync(destination, "}", token).ConfigureAwait(false);
        await WritePropertyAsync("id", response.Id, McpJsonUtilities.JsonContext.Default.RequestId, destination, token).ConfigureAwait(false);
        if (response.JsonRpc is { } version)
            await WritePropertyAsync("jsonrpc", version, McpJsonUtilities.JsonContext.Default.String, destination, token).ConfigureAwait(false);
        await WriteSyntaxAsync(destination, "}", token).ConfigureAwait(false);
    }

    private static async Task WritePropertyAsync<T>(string name, T value, JsonTypeInfo<T> typeInfo, Stream destination, CancellationToken token)
    {
        await WriteSyntaxAsync(destination, $",\"{name}\":", token).ConfigureAwait(false);
        await BoundedJsonMessageWriter.WriteValueAsync(value, typeInfo, destination, token).ConfigureAwait(false);
    }

    private static Task WriteSyntaxAsync(Stream destination, string syntax, CancellationToken token) =>
        destination.WriteAsync(Encoding.UTF8.GetBytes(syntax), token).AsTask();

    private static async Task FlushAsync(Utf8JsonWriter writer, ArrayBufferWriter<byte> buffer, Stream destination, CancellationToken token)
    {
        writer.Flush();
        await destination.WriteAsync(buffer.WrittenMemory, token).ConfigureAwait(false);
        buffer.Clear();
    }

    private static async Task WriteTextAsync(StreamingTextContentBlock block, Stream destination, CancellationToken token)
    {
        var buffer = new ArrayBufferWriter<byte>(32768);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", block.Type);
            writer.WritePropertyName("text");
            await foreach (var segment in block.ReadAsync(token).WithCancellation(token).ConfigureAwait(false))
            {
                var remaining = segment;
                while (!remaining.IsEmpty)
                {
                    token.ThrowIfCancellationRequested();
                    int length = Math.Min(remaining.Length, 4096);
                    writer.WriteStringValueSegment(remaining.Span.Slice(0, length), false);
                    await FlushAsync(writer, buffer, destination, token).ConfigureAwait(false);
                    remaining = remaining.Slice(length);
                }
            }
            writer.WriteStringValueSegment(ReadOnlySpan<char>.Empty, true);
            await FlushAsync(writer, buffer, destination, token).ConfigureAwait(false);
        }
        if (block.Annotations is { } annotations)
            await WritePropertyAsync("annotations", annotations, McpJsonUtilities.JsonContext.Default.Annotations, destination, token).ConfigureAwait(false);
        if (block.Meta is { } meta)
            await WritePropertyAsync("_meta", meta, McpJsonUtilities.JsonContext.Default.JsonObject, destination, token).ConfigureAwait(false);
        await WriteSyntaxAsync(destination, "}", token).ConfigureAwait(false);
    }
}
#endif
