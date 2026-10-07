# Bounded streaming experiment

This work is experimental. It is not a released package or a production adoption recommendation.

## Source identity

The repository `2Elemental-HQ/mcp` is a GitHub fork of
`modelcontextprotocol/csharp-sdk`. The feature branch is
`codex/bounded-streaming-content` and its upstream base is
`3338e88e15c42cfc27465143c140d7d10f1a2707`.

The initial serialization prototype was developed against SDK 1.1.0 at
`ca040d77ec885fc7ac7649487722adaf33a4fd3f`. That prototype is preserved on
`codex/sdk-1.1-buffer-probe`; its changes were ported onto the newer upstream
base. Measurements against 1.1.0 do not establish improvement against the new base.

## Separately testable changes

1. Defer typed result serialization, escape text in bounded segments, and avoid
   full-event SSE output buffering. Parse client SSE input with fixed-size
   segments instead of exponentially growing pooled arrays.
2. On .NET 10, `StreamingTextContentBlock` accepts a repeatable asynchronous
   sequence of borrowed UTF-16 segments. It has the ordinary `type: text` wire
   representation. A client receives one complete `TextContentBlock`, not partial
   tool results. Existing string content remains supported.

A source owns its resources until enumeration is disposed. It must observe
cancellation and produce identical content when reopened. A yielded memory region
must remain valid until the next enumeration step. HTTP serialization must await
transport backpressure before advancing the source.

Explicit synchronous serialization and mutable `JsonRpcResponse.Result`
inspection can materialize content. Client APIs still return complete strings;
client memory is measured separately from server memory. Other transports and
persistent event stores need separate acceptance evidence.

## Verification and review

The final measured runtime source is `2142bb821b142dd31d9ceaac77e3a1304d259a4f`.
Both routes pass 302/302 complete synthetic responses under separately enforced
limits; unchanged upstream passes only the first response for each fixture.
See [final results and reproduction](final-results.md), [compatibility and limits](verification.md),
[buffer attribution](buffer-profiles.md), and the [unsent upstream proposal](upstream-proposal.md). Earlier preliminary
measurements remain available with their original source identity.

This is a draft for review, not a released package. The fork's cloud workflow
registration remains unavailable; no cloud CI pass or production adoption is claimed.
Only synthetic fixtures and SDK-relevant evidence belong in this public folder.

## Example

On .NET 10, return a normal `CallToolResult` whose content includes:

```csharp
new StreamingTextContentBlock(ReadText)
```

A bounded, repeatable source can open its file inside the iterator:

```csharp
async IAsyncEnumerable<ReadOnlyMemory<char>> ReadText(
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    using var reader = File.OpenText(path);
    var buffer = new char[4096];
    while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) is int count && count != 0)
    {
        yield return buffer.AsMemory(0, count);
    }
}
```

Use `System.Runtime.CompilerServices` for `EnumeratorCancellation`. Keep the file
immutable across enumerations, or provide another stable snapshot. The HTTP writer
awaits downstream backpressure before the iterator can reuse its buffer.
