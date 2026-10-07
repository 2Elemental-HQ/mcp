#if NET10_0_OR_GREATER
namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides text incrementally while preserving the ordinary MCP text-content wire representation.
/// </summary>
/// <remarks>
/// The source is opened for each serialization and must produce identical, complete text on each
/// enumeration. A segment remains valid until the next MoveNextAsync call. The source must observe
/// cancellation and release its resources when enumeration ends. HTTP/SSE serialization awaits
/// transport backpressure before requesting more data. Clients receive a normal TextContentBlock.
/// Explicit synchronous serialization or access to JsonRpcResponse.Result may materialize the result.
/// </remarks>
public sealed class StreamingTextContentBlock : ContentBlock
{
    private readonly Func<CancellationToken, IAsyncEnumerable<ReadOnlyMemory<char>>> _readText;

    /// <summary>
    /// Creates a repeatable text source. The delegate is not invoked until serialization begins.
    /// </summary>
    /// <param name="readText">A factory for cancellable, resource-owning text enumerations.</param>
    public StreamingTextContentBlock(Func<CancellationToken, IAsyncEnumerable<ReadOnlyMemory<char>>> readText)
    {
        Throw.IfNull(readText);
        _readText = readText;
    }

    /// <inheritdoc/>
    public override string Type => "text";

    /// <summary>
    /// Opens an enumeration whose segments form one complete text value.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels source reads.</param>
    /// <returns>The text source; the consumer owns and disposes its enumerator.</returns>
    public IAsyncEnumerable<ReadOnlyMemory<char>> ReadAsync(CancellationToken cancellationToken = default) => _readText(cancellationToken);
}
#endif
