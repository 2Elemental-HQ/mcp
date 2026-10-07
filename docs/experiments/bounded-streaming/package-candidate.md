# Local pinned package candidate

This is an unpublished review candidate, not a released or adopted dependency.

- Source: `c04230cd11f8e73986c426da8c7c4e043348d371`.
- Version: `2.2.0-elemental.1.gc04230cd11f8` for Core, ModelContextProtocol and AspNetCore.
- Upstream base: `3338e88e15c42cfc27465143c140d7d10f1a2707`.
- Runtime `src` tree: unchanged from the measured `2142bb821b142dd31d9ceaac77e3a1304d259a4f`.
- Build SDK: `10.0.301`; original license, authors and strong-name identity retained.
- Candidate CI: [37617002141](https://github.com/2Elemental-HQ/mcp/actions/runs/37617002141). Follow its platform, AOT, documentation, packaging and coverage conclusions separately from local results.

Check out that exact source in a clean checkout, then run `python3 docs/experiments/bounded-streaming/build-candidate.py --output <new-directory>`. Use a second new directory for an independent build. [Manifest A](candidate-manifest-a.json) and [manifest B](candidate-manifest-b.json) contain identical SHA-256 values for all three packages. The script only builds locally; it never publishes. Pin the exact version with NuGet range brackets and source mapping, retain lockfiles and verify package hashes before use.

The script normalizes unsigned NuGet ZIP/OPC metadata, which otherwise contains random identifiers and timestamps. It never modifies assembly or license content and refuses signed packages. Initial builds also exposed nondeterministic generated logging-source ordering in portable PDBs. Disabling compiler parallelism did not fix that difference. This candidate therefore explicitly omits debug symbols; two complete no-symbol builds are byte-identical. Source-line debugging is unavailable. This is a diagnostic limitation, not a runtime behavior change or a claim that every platform/toolchain produces the same ZIP bytes.

## Workflow activation

Workflow YAML was present on the default branch while the API reported zero workflows and dispatch/enable returned HTTP 404; repository Actions permissions already said enabled. Explicit repository activation immediately registered seven workflows. The observable fault was uninitialized workflow registration; the API does not prove GitHub's internal cause. No merge was used.

Before activation, fork workflow approval was changed to `all_external_contributors`. Effective repository settings were then read back: Actions enabled, `allowed_actions=all` unchanged, default token read-only, Actions PR-review approval disabled, artifact/log retention seven days. Only Build and Test and reusable Code Coverage are active; release, docs publication, CodeQL, Copilot setup and link checking are disabled. See [effective API evidence](actions-review.json).

The enabled paths use only standard `ubuntu-latest`, `windows-latest` and `macos-latest` runners on this public repository. There are no larger/self-hosted labels, `pull_request_target` triggers or alternate external-code approval bypasses. Coverage no longer inherits unused secrets; upload steps explicitly retain artifacts seven days. No repository membership, organization setting, cache limit or paid storage limit changed. Standard public-repository hosted minutes are free under [GitHub's billing policy](https://docs.github.com/en/billing/concepts/product-billing/github-actions).

The first CI attempt [37614257897](https://github.com/2Elemental-HQ/mcp/actions/runs/37614257897) failed on a new net472 test-stream override and unregistered DocFX evidence resources. Both are fixed without skips or warning suppression. Superseded run 37615785404 was canceled to free capacity; cancellation is not a pass. Existing local skips remain explicitly uncredited. The full local test result and runtime boundaries are in [verification](verification.md) and [production review](production-review.md).

## Review decision

A temporary exact-source dependency is defensible for the measured .NET 10 HTTP/SSE path after owner review and successful candidate CI. The client still owns complete results. Synchronous serialization, mutable result inspection, persistent event stores, arbitrary custom converters and other transports do not inherit this memory acceptance. Distribution and production adoption need separate owner authorization. The generic upstream proposal remains unsent.
