# Unsent upstream proposal

Status: draft for owner review. This document has not been submitted to the upstream repository.

## Suggested title

Bound large text-result serialization and SSE buffers; add incremental text content on .NET 10

## Problem

At upstream `3338e88e15c42cfc27465143c140d7d10f1a2707`, a synthetic tool result with approximately 8.45 million UTF-16 characters can complete once but fail repeated requests and ten concurrent requests within a 512 MiB managed heap and 1 GiB process container. Client and server run in separate containers. Both variants preserve one ordinary complete text-content result, including nested Markdown and Body fields.

The result path repeatedly materializes JSON nodes and whole-result escaping buffers. The server SSE path constructs a complete event buffer. Client SSE growth and whole-string unescaping add independent scratch costs. Avoiding only one serialization helper leaves the other costs in place.

## Proposed changes

1. Keep typed results deferred through JSON-RPC serialization while preserving the public mutable `Result` node and custom converters.
2. Use bounded .NET 10 text segments for escaping; write a single SSE event incrementally with backpressure.
3. Parse client SSE with fixed-size segments and decode escaped text through bounded framework-validated chunks. Continue returning a complete client result.
4. Add `StreamingTextContentBlock`, accepting a repeatable, cancellable async sequence of borrowed character segments. Its wire representation remains the existing `type: text` content block. No partial-tool-result protocol is introduced.
5. Keep metadata updates and diagnostics from opening or materializing streaming sources; cancel an active source when its HTTP response is canceled.

## Reproduction and evidence

See [final-results.md](final-results.md) and [repro](repro). Build both the exact upstream base and candidate with the same SDK/runtime and fixture. Each patched route validates 302/302 complete answers with SHA-256; the upstream comparison validates only the two first answers. The JSON evidence includes failed responses, binary SHA-256, independent server/client allocation/heap/RSS figures, limits and latency.

See [verification.md](verification.md) for compatibility, cancellation/fault/backpressure tests, older-target checks and the outstanding cloud CI limitation. The source measured is `2142bb821b142dd31d9ceaac77e3a1304d259a4f`.

## Review questions

- Is the borrowed async character-source API the preferred ownership contract, or should the SDK expose a writer-oriented API with equivalent cancellation and replay guarantees?
- Should deferred typed results be generalized across transports before releasing the new API, or should its bounded-memory guarantee remain explicitly HTTP/SSE-only?
- How should persistent event stores advertise materialization/replay costs for incremental content?
- Is the fixed-segment client parser desirable upstream, given that the public client still returns complete strings?

The existing string API remains compatible. The .NET 10 segmented writer is required for the new bounded text path. Synchronous result inspection may materialize content. No package or protocol change is proposed implicitly.

> [!NOTE]
> This proposal and implementation were prepared with AI assistance and require maintainer review.
