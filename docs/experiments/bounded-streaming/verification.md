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

## Review follow-up and candidate CI

The complete local Release suite now passes **9,241 tests, with 99 existing skips and no failures**: Core 2,420 on each of net8/net9/net10; ASP.NET 639/639/646; analyzers 57. Skips are existing credential/default-extraction and inherited transport/capability-specific cases; they receive no coverage credit. No new skips or relaxed assertions were introduced. The official Everything SSE server pinned by the existing NPM lockfile replaces the incompatible external Docker image. Both fixture tests also pass on exact upstream with test-only fixture changes. net472 test compilation and DocFX resources were fixed; docs still fail on warnings. Supported multiline JSON-node tests pass on all modern targets.

The table above preserves the earlier attempt and is superseded for final local regression status by these results. See [production review](production-review.md) and [package candidate](package-candidate.md) for findings, exact source, build limitations and effective Actions settings. The final package-source CI is [run 37617002141](https://github.com/2Elemental-HQ/mcp/actions/runs/37617002141), source `c04230cd11f8e73986c426da8c7c4e043348d371`. Its live job conclusions, including coverage, are authoritative; local success alone is not a cloud CI claim.

The initial .NET 9 test failure used reflection serialization in a test while the suite disables reflection. The corrected test copies `McpJsonUtilities.DefaultOptions`; production behavior did not change. Full Core is not labeled green because the external-server failures remain. Skipped tests do not receive coverage credit.

Focused behavior tests cover ordinary text and null wire values, public mutable result nodes, custom response converters, metadata/annotations, error content, repeatable sources, escaping and split Unicode, invalid surrogate rejection, SSE framework-equivalent parsing, trace logging, source/destination failures, cancellation, source disposal and backpressure. A blocked source is canceled on HTTP disconnect and a subsequent request can succeed. The source does not advance while the destination is blocked.

Initially GitHub returned zero workflows and HTTP 404 on dispatch despite enabled permissions. Explicit repository activation registered the workflows. Only CI and reusable coverage are enabled, with all external contributor approval, read-only default token and seven-day artifact retention. The linked candidate CI now runs the cross-platform Debug/Release matrix. Earlier failed jobs remain available; net472 compilation and documentation failures were fixed rather than waived.

## Scope and limits

- The new streaming content API and bounded text escaping/unescaping require .NET 10. Older targets retain the string API and pass focused compatibility checks, but have no corresponding memory acceptance claim.
- The measured path is ordinary HTTP/SSE complete tool results. Explicit synchronous serialization, public `Result` node inspection, persistent event stores and other transports can materialize content and need separate memory evidence.
- Client deserialization still returns a complete text string and owns a raw result. Fixed-size SSE segments remove large growth rentals but do not eliminate payload-sized client storage.
- Custom converters and arbitrary non-text values can allocate according to their own implementation. The streaming fast path is not a universal bound on application-provided serialization.
- A source must be repeatable, observe cancellation and release resources on disposal. A yielded memory segment stays valid until the next enumeration step. Stream failures terminate an incomplete response; they cannot retroactively turn already-sent bytes into a successful JSON-RPC error result.
- Full SHA-256 equality proves complete fixture output. One hundred requests demonstrate this finite workload, not unbounded lifetime stability.

## Recommendation

The patch is suitable for upstream design review, particularly the deferred typed-result path, bounded escaping and SSE buffering fixes. The new public API deserves separate API/lifetime review. A temporary exact-commit-pinned dependency is defensible only after the owner reviews the patch, verifies the relevant CI matrix and accepts the documented .NET 10/HTTP and client-memory limits. Do not use a floating branch. No package publication, upstream submission or production adoption has been performed.

## Cross-platform follow-up

The original candidate CI passed Linux/macOS Debug and Release but failed Windows; it is not green. See [additional compatibility findings](known-upstream-boundaries.md) for the corrected net472 HTTP mock, bounded legacy SSE diagnostic, exact-upstream comparison recipe and separately reproduced late-discovery protocol race. No new test skip or tolerance increase was used. These findings qualify the adoption recommendation above.
