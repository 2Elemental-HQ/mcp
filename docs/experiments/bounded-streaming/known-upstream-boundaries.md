# Additional compatibility findings

These findings do not change the measured runtime patch or the unpublished package source `c04230cd11f8e73986c426da8c7c4e043348d371`. They prevent a blanket claim that the complete cross-platform suite is green.

## Late discovery response

Candidate CI [37617002141](https://github.com/2Elemental-HQ/mcp/actions/runs/37617002141) recorded one Windows net9 stdio failure: the discovery response arrived after the default five-second probe deadline, followed by an `initialize` request rejected because the server had already negotiated `2026-07-28`.

A controlled reproduction on exact upstream `3338e88e15c42cfc27465143c140d7d10f1a2707`, with **no runtime source changes**, gives:

| Probe | Control | Delayed case |
|---|---|---|
| Start a real stdio child six seconds later | Passed | Passed |
| Deliver an already-produced discovery reply six seconds later | Passed | Failed with the same `initialize` rejection |

[Delayed reply reproduction](probes/DelayedDiscoveryResponseProbeTests.cs.txt) uses the real SDK client and server over streams. Copy it into the upstream Core test project as a `.cs` file and run `dotnet test tests/ModelContextProtocol.Tests -f net10.0 -c Release --filter FullyQualifiedName~DelayedDiscoveryResponseProbeTests`. The six-second assertion is expected to fail on that exact upstream base; the zero-delay assertion passes. No timeout or assertion tolerance is widened. The [separate child-start control](probes/DelayedStartupProbeTests.cs.txt) requires Python 3 and demonstrates why startup delay alone is not a proven explanation.

This establishes an upstream negotiation failure mechanism matching the CI sequence. It does not prove that every slow startup fails, nor that the large-text patch caused the observed scheduling delay. The issue can matter to consumers outside the measured fast-handshake HTTP workload. It remains a separate adoption limitation; no upstream report has been submitted.

## Windows .NET Framework external SSE fixture

The initial full Windows run exposed six new malformed HTTP-mock responses: on .NET Framework a synthetic accepted response had null Content, unlike modern HttpResponseMessage behavior. The mock now explicitly supplies empty content. This corrects the test, not production serialization.

The regular external Everything SSE tests also outlived the seven-minute test-host inactivity deadline on net472. They now report connection, response and disposal stages and enforce the existing 60-second operation deadline. Timeout remains a failed test; disposal releases the owned HttpClient during cleanup. No skip was added. The unchanged modern-target fixture and malformed-event tests pass locally (8/8), and net472 compiles with zero warnings/errors.

The [upstream comparison recipe](probe-upstream-sse.py) runs the identical fixture against exact upstream runtime in the existing Windows Release CI job. Its summary and TRX distinguish executed test failures from setup failures; a baseline failure does not waive the separate candidate full-suite gate. The first recipe attempt omitted removal of the superseded Docker test class and therefore did not execute the comparison. That setup attempt receives no compatibility credit. The corrected recipe replaces both original fixture and test class, retains both test cases, and passes 2/2 locally on net10. Windows results are retained by the linked PR CI and review handoff. In corrected candidate run [37620202290](https://github.com/2Elemental-HQ/mcp/actions/runs/37620202290), Windows Debug passes all modern-target tests and net472 passes 2,108 tests with 286 existing skips; only the two external SSE tests fail. Their full response assertions complete, then SDK client disposal exceeds 60 seconds. The six malformed-event tests now pass. This localizes the remaining boundary to legacy-client shutdown rather than response parsing or sampling availability.

## Interpretation

Review the current CI job conclusions, not just the successful .NET 10 resource workload. The original candidate CI passed Linux/macOS Debug and Release, but failed Windows and therefore did not execute its coverage publication gate. A canceled superseded run is not a pass. Existing platform/capability skips remain uncredited. The fork is not a generally green replacement dependency; its exact package and measured .NET 10 HTTP/SSE scope require an explicit owner decision with these limitations visible.
