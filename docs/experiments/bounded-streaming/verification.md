# Compatibility and verification

The runtime patch measured in `final-synthetic-results.json` is commit `2142bb821b142dd31d9ceaac77e3a1304d259a4f`. Later changes correct test configuration and add evidence; they do not change the measured runtime code.

| Check | Result |
|---|---|
| Core build, all four targets | Passed, zero warnings/errors |
| Core full .NET 10 | 2,418 passed, three skipped, two external Docker startup failures |
| External tests, focused rerun | Connection passes; sampling fails because the example server lacks `trigger-sampling-request` |
| Exact upstream external tests | Same missing sampling tool; connection passes |
| Core focused .NET 9 | 198 passed after fixing test JSON options to use the source-generated resolver |
| Core focused .NET 8 | 198 passed |
| ASP.NET .NET 10 | 646 passed, 30 skipped |
| Native AOT publish and executable | Passed on macOS ARM64 |
| Synthetic fixed-limit comparison | 302/302 full SHA-256 matches on each patched route; 2/302 upstream |

The initial .NET 9 test failure used reflection serialization in a test while the suite disables reflection. The corrected test copies `McpJsonUtilities.DefaultOptions`; production behavior did not change. Full Core is not labeled green because the external-server failures remain. Skipped tests do not receive coverage credit.

Focused behavior tests cover ordinary text and null wire values, public mutable result nodes, custom response converters, metadata/annotations, error content, repeatable sources, escaping and split Unicode, invalid surrogate rejection, SSE framework-equivalent parsing, trace logging, source/destination failures, cancellation, source disposal and backpressure. A blocked source is canceled on HTTP disconnect and a subsequent request can succeed. The source does not advance while the destination is blocked.

The public fork has workflow files, but GitHub currently reports zero registered workflows and manual dispatch returns HTTP 404. Actions permissions report enabled. No cloud CI result is claimed. Cross-platform/Debug workflow verification remains open; local Release and native AOT results do not substitute for that matrix.

## Scope and limits

- The new streaming content API and bounded text escaping/unescaping require .NET 10. Older targets retain the string API and pass focused compatibility checks, but have no corresponding memory acceptance claim.
- The measured path is ordinary HTTP/SSE complete tool results. Explicit synchronous serialization, public `Result` node inspection, persistent event stores and other transports can materialize content and need separate memory evidence.
- Client deserialization still returns a complete text string and owns a raw result. Fixed-size SSE segments remove large growth rentals but do not eliminate payload-sized client storage.
- Custom converters and arbitrary non-text values can allocate according to their own implementation. The streaming fast path is not a universal bound on application-provided serialization.
- A source must be repeatable, observe cancellation and release resources on disposal. A yielded memory segment stays valid until the next enumeration step. Stream failures terminate an incomplete response; they cannot retroactively turn already-sent bytes into a successful JSON-RPC error result.
- Full SHA-256 equality proves complete fixture output. One hundred requests demonstrate this finite workload, not unbounded lifetime stability.

## Recommendation

The patch is suitable for upstream design review, particularly the deferred typed-result path, bounded escaping and SSE buffering fixes. The new public API deserves separate API/lifetime review. A temporary exact-commit-pinned dependency is defensible only after the owner reviews the patch, verifies the relevant CI matrix and accepts the documented .NET 10/HTTP and client-memory limits. Do not use a floating branch. No package publication, upstream submission or production adoption has been performed.
