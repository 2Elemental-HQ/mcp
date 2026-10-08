# Final synthetic comparison

Measured source: `2142bb821b142dd31d9ceaac77e3a1304d259a4f`; unchanged upstream: `3338e88e15c42cfc27465143c140d7d10f1a2707`. Build SDK 10.0.301. Exact binary hashes and every response, including failures, are in [final-synthetic-results.json](final-synthetic-results.json). Earlier preliminary results remain preserved.

The LF/CRLF fixtures contain synthetic nested Markdown/Body text (about 8.45 million UTF-16 characters). Each route returns the same ordinary single text block. Each process has its own 512 MiB managed heap, 1 GiB container and two CPUs. There is no forced collection. The client validates every complete result with SHA-256; process exit status is not the acceptance criterion.

Both patched routes pass **302/302** answers. Upstream passes **2/302**: the first response for each fixture succeeds, but sequential and concurrent phases fail. Each fixture runs one first response, twenty sequential, twenty at concurrency ten, a fresh ten, and a fresh hundred at concurrency ten.

MiB means 1,048,576 bytes. S/C means independent server/client. Allocations cover a phase; occupied heap includes uncollected garbage. RSS peak is process lifetime, while endpoint heap/LOH can miss transients or reflect an earlier GC. Failed-phase latency is not comparable to successful latency. The small public harness does not provide allocation stack attribution or sampled transient heap peaks.

| Variant | Fixture/phase | SHA pass | Allocation S/C MiB | End occupied S/C MiB | Peak RSS S/C MiB | Median ms |
|---|---|---:|---:|---:|---:|---:|
| patched-stream | crlf/fresh-ten/0 | 10/10 | 10.0/334.7 | 3.3/136.4 | 93.2/316.4 | 557.0 |
| patched-stream | crlf/history/0 | 1/1 | 1.5/34.1 | 2.7/34.2 | 83.2/102.2 | 110.5 |
| patched-stream | crlf/history/1 | 20/20 | 14.7/672.1 | 9.0/77.8 | 100.2/282.1 | 81.6 |
| patched-stream | crlf/history/2 | 20/20 | 17.5/671.3 | 26.5/96.3 | 117.0/409.3 | 225.3 |
| patched-stream | crlf/repeat-ten/0 | 100/100 | 83.2/3358.9 | 77.0/356.4 | 170.3/570.4 | 310.8 |
| patched-stream | lf/fresh-ten/0 | 10/10 | 9.9/335.1 | 3.4/226.3 | 94.2/418.5 | 532.5 |
| patched-stream | lf/history/0 | 1/1 | 1.6/33.9 | 2.8/34.0 | 80.1/104.1 | 107.9 |
| patched-stream | lf/history/1 | 20/20 | 14.9/668.1 | 9.4/55.5 | 96.1/286.7 | 87.3 |
| patched-stream | lf/history/2 | 20/20 | 17.5/667.1 | 27.3/97.3 | 115.4/508.1 | 235.6 |
| patched-stream | lf/repeat-ten/0 | 100/100 | 83.2/3338.5 | 4.2/149.8 | 170.7/571.9 | 341.2 |
| patched-string | crlf/fresh-ten/0 | 10/10 | 376.8/337.0 | 261.2/148.0 | 388.4/406.9 | 941.4 |
| patched-string | crlf/history/0 | 1/1 | 33.6/34.0 | 34.0/34.3 | 116.9/101.9 | 112.6 |
| patched-string | crlf/history/1 | 20/20 | 657.7/669.7 | 34.4/86.0 | 290.2/312.5 | 71.1 |
| patched-string | crlf/history/2 | 20/20 | 693.8/668.8 | 265.7/253.2 | 579.3/426.9 | 511.8 |
| patched-string | crlf/repeat-ten/0 | 100/100 | 3539.1/3356.0 | 372.5/133.6 | 599.5/544.5 | 352.0 |
| patched-string | lf/fresh-ten/0 | 10/10 | 368.9/334.3 | 253.8/252.8 | 371.5/387.4 | 1226.8 |
| patched-string | lf/history/0 | 1/1 | 33.5/33.8 | 33.9/34.0 | 119.0/103.5 | 110.9 |
| patched-string | lf/history/1 | 20/20 | 655.1/664.0 | 95.2/72.5 | 297.1/290.0 | 78.1 |
| patched-string | lf/history/2 | 20/20 | 755.8/668.4 | 252.6/340.3 | 547.9/455.5 | 472.8 |
| patched-string | lf/repeat-ten/0 | 100/100 | 3506.1/3327.9 | 394.9/276.4 | 599.5/568.5 | 333.4 |
| upstream-string | crlf/fresh-ten/0 | 0/10 | 1118.6/0.4 | 385.9/1.0 | 415.7/60.1 | failed |
| upstream-string | crlf/history/0 | 1/1 | 307.4/186.3 | 307.7/186.7 | 193.5/161.8 | 206.9 |
| upstream-string | crlf/history/1 | 0/20 | 950.4/0.6 | 467.6/1.1 | 260.5/61.2 | failed |
| upstream-string | crlf/history/2 | 0/20 | 314.6/0.8 | 459.2/1.3 | 261.7/62.6 | failed |
| upstream-string | crlf/repeat-ten/0 | 0/100 | 3775.3/2.2 | 418.9/2.8 | 316.5/76.1 | failed |
| upstream-string | lf/fresh-ten/0 | 0/10 | 748.4/0.5 | 394.5/1.0 | 310.6/64.2 | failed |
| upstream-string | lf/history/0 | 1/1 | 307.2/186.0 | 307.4/186.4 | 195.5/161.6 | 214.0 |
| upstream-string | lf/history/1 | 0/20 | 1270.4/0.5 | 394.7/1.1 | 259.9/65.3 | failed |
| upstream-string | lf/history/2 | 0/20 | 656.6/0.4 | 427.4/1.0 | 313.3/57.5 | failed |
| upstream-string | lf/repeat-ten/0 | 0/100 | 4034.7/2.2 | 386.1/2.8 | 427.3/76.0 | failed |

## Interpretation

The string route still constructs one complete application string. The incremental route reads the same file in 4,096-character segments. The client still retains a complete segmented SSE event while parsing, copies the raw result and produces a complete string. It is not a constant-memory client API, and a successful hundred-request run is not an indefinite retention guarantee.

The protocol is unchanged: one complete JSON-RPC response in an SSE message. There are no partial tool-result extensions. The .NET 10 fast path avoids repeated result-node materialization, whole-text escaping scratch and whole-event SSE output buffers. Explicit synchronous serialization, mutable result-node inspection, event stores and other transports are outside this HTTP memory claim.

## Reproduce

From the SDK checkout, with .NET SDK 10.0.301 and Docker:

```sh
dotnet publish docs/experiments/bounded-streaming/repro/Repro.csproj -c Release -p:StreamingApi=true -o /tmp/mcp-candidate-host
dotnet /tmp/mcp-candidate-host/Repro.dll fixture /tmp/mcp-fixtures string
python3 docs/experiments/bounded-streaming/repro/run.py --host /tmp/mcp-candidate-host --fixtures /tmp/mcp-fixtures --output /tmp/mcp-stream-results --route stream --image mcr.microsoft.com/dotnet/sdk@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29
```

Repeat with `--route string`. For the unpatched comparison, copy this reproduction to a checkout of the exact upstream base and publish without `StreamingApi=true`. Keep the same fixture files, image digest and limits. Run variants sequentially to avoid competing benchmark load. Do not globally set `TargetFrameworks`; the analyzer targets netstandard2.0.

The original string API exists on all supported targets. The new streaming API and bounded text escaping/decoding require .NET 10. See [verification.md](verification.md) for compatibility tests and outstanding CI limitations.
