# OAuth discovery timeout investigation

## Classification

The observed duplicate-authorization assertion is reproducible on exact upstream, without the
streaming patch. It is an existing **client discovery/OAuth deadline interaction**, exposed by a
slow initial token exchange. It is not a fork-only serializer/SSE regression and is not limited
to .NET 9. The exact reason the original CI token exchange became slow is not established.
No production code, package contents, timeout, test assertion or supported scope is changed.

## Original failure

- Run [37632018833](https://github.com/2Elemental-HQ/mcp/actions/runs/37632018833), Linux Release
  job `112828443099`, net9.0.
- Review head `3b73488db97e034ab9c9b3bacf37235a44e28e07`; actual PR merge checkout
  `cf5edf9ec07372029d0d1276f35f48f7c6d7f40f` into upstream
  `3338e88e15c42cfc27465143c140d7d10f1a2707`.
- Test `ModelContextProtocol.AspNetCore.Tests.OAuth.AuthTests.AuthorizationFlow_ConcurrentStepUps_ReuseSteppedUpToken_WhenChallengeAddsNoNewScope`.
- `AuthTests.cs:1099`: `Assert.Single() Failure: The collection contained 2 items`,
  collection `["mcp:tools", "mcp:tools"]`. This is the **initial connection assertion**, before either
  concurrent tool call and before the scope step-up under test.
- Ubuntu 24.04.5 x64, `ubuntu-latest`, runner image `20261004.327.1`, runner 2.337.0,
  Release, net9.0 (reflection serialization disabled by the unchanged test project).
  Setup installed SDKs 10.0.401 and 9.0.318; the original log does not print the selected compiler.
  The repository's `global.json` requests 10.0.101 with minor roll-forward. An installed SDK version
  must not be mistaken for the selected SDK.

The original log records the first `/token` beginning at 13:56:35, `initialize` fallback and a second
401/authorization at 13:56:40, then the first token request finishing as HTTP 499 after **6778.3126 ms**.
The second token exchange succeeds and initialization completes, after which the single-flow
assertion fails. These are observed events, not an inference that the scope-step-up lock failed.

## Matched, targeted reproduction

[Run 37639914811](https://github.com/2Elemental-HQ/mcp/actions/runs/37639914811) runs only that test
on candidate source `c04230cd11f8e73986c426da8c7c4e043348d371` and exact upstream
`3338e88e15c42cfc27465143c140d7d10f1a2707`, in the same Ubuntu 24.04.5 x64 job, Release, net9.0 and
net10.0. Both retain original global.json and select SDK **10.0.112**; installed runtime patches
are .NET 9.0.20 and 10.0.12. This runner image is `20260927.320.1`, not the original failing image.
The two sources share the same environment; this does not recreate the original full-suite load,
coverage instrumentation or unexplained scheduling delay.

| Per source/framework | Repetitions | Outcome |
|---|---:|---|
| Unmodified original test | 1 | Pass |
| First token delayed 1,000 ms, default protocol | 2 | Pass |
| First token delayed 6,500 ms, default protocol | 2 | Same initial `Assert.Single` failure, two scopes |
| First token delayed 6,500 ms, explicit 2025-11-25 protocol | 2 | Pass, including both concurrent scope-step-up calls |

All four source/framework combinations have the same results: **20 passes and 8 deliberately
reproduced original assertion failures**. The diagnostic job's success means all expected outcomes
matched; it does not relabel the eight failed tests or the full SDK suite as green. The original
assertions and five-second probe timeout are unchanged. Delay and protocol selection exist only
in disposable test checkouts; the retained fixture patches show the exact instrumentation.
The legacy-version case is a causal control, not a production configuration change or a general fix.

An earlier supplemental [run 37638211926](https://github.com/2Elemental-HQ/mcp/actions/runs/37638211926)
explicitly selected compiler 10.0.401 and obtained the same 20-pass/8-failure results. It is preserved
separately; the later run corrects the assumption that setup-dotnet's installed version selects the
compiler despite global.json. The original test, `ClientOAuthProvider`, client options and global.json
have identical Git blobs across upstream, candidate and failing review head.

Reproduce with the checked-in `probe-oauth.py` from a full clone with SDKs/runtimes installed:

```sh
python3 .github/probes/probe-oauth.py /absolute/new/evidence-directory
```

## Proven mechanism and remaining uncertainty

`McpClientImpl.ConnectAsync` applies `DiscoverProbeTimeout` (five seconds by default) to the entire
`server/discover` request, including HTTP authentication. `ClientOAuthProvider` performs interactive
authorization and token exchange with that cancellation token. If exchange has not completed,
cancellation prevents caching a completed token. The existing client fallback catches the probe's
cancellation and tries `initialize`; another 401 can therefore start authorization again. The long-delay
reproduction and legacy-protocol control establish that causal boundary on both sources/frameworks.

This is not evidence that the server accepted invalid credentials. It is not evidence of a failure
in the concurrent scope-step-up cache lock: the original test never reaches those calls on its failing
path. The uncontrolled CI delay's underlying CPU, signing, JIT or scheduling cause is still unknown;
none is asserted as proven. The full performance matrix is neither needed nor rerun.

## Release consequence

Candidate `2.2.0-elemental.1.gc04230cd11f8` and all package hashes remain unchanged. No new package
version is needed because no package content changed. A restricted .NET 10 HTTP/SSE **server** adoption
can be reviewed independently of this existing client defect, but slow OAuth interoperability must be
reported: an external client using this discovery flow can cancel and repeat authorization even when
the server correctly validates tokens. Do not claim all authenticated clients are unaffected.
Pre-acquired bearer credentials do not require the interactive token exchange inside discovery.

This investigation does not authorize general client adoption, a general SDK support claim or a
published release. Windows limitations, missing debug symbols, full-result client memory and the
other [known boundaries](../../docs/experiments/bounded-streaming/known-upstream-boundaries.md) remain.
Full SDK CI is not green. Automatically queued broad SDK reruns for diagnostic-only changes were
cancelled, not passed. No upstream issue/PR, package publication, merge or deployment was performed.
