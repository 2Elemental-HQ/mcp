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

## Current verification status

This is an in-progress prototype. Initial wire-content tests pass, including
split surrogate pairs, escaping, metadata, mixed content and repeated sources.
The stronger HTTP backpressure test exposed eager materialization in the upstream
server-metadata filter. That filter now updates typed metadata without opening the
source. Nine HTTP tests, including blocked output, cancellation, destination failure
and source disposal, pass. All four Core target frameworks build without warnings.
Resource acceptance and production adoption still require reproducible before/after
measurements.

Only synthetic fixtures and SDK-relevant evidence belong in this public folder.
