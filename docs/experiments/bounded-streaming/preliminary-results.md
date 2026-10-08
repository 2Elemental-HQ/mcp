# Preliminary synthetic measurements

These measurements precede the final integration candidate. They use synthetic nested Markdown/Body text only. Every successful answer must match the full expected SHA-256. Server and client each have a separate 512 MiB managed heap and 1 GiB container; neither process is charged to the other. No forced GC is used.

Each patched route passes 302/302 answers: for each LF/CRLF fixture, one first request, 20 sequential, 20 in batches of ten, a fresh ten, and a fresh 100 in batches of ten. The unmodified upstream fails repeated and fresh-ten scenarios. A zero process exit is not treated as successful content validation.

## First request

MiB means 1,048,576 bytes. Occupied heap estimates include garbage that has not yet been collected; they are not exact strongly reachable retained size. RSS includes runtime/native memory.

| Variant | Ending | Server allocations MiB | Client allocations MiB | Server end occupied MiB | Client end occupied MiB | Median latency ms |
|---|---|---:|---:|---:|---:|---:|
| patched-stream | crlf | 1.57 | 49.86 | 2.67 | 41.86 | 126.2 |
| patched-stream | lf | 1.51 | 49.84 | 2.61 | 41.72 | 110.8 |
| patched-string | crlf | 33.63 | 49.92 | 34.01 | 41.88 | 102.4 |
| patched-string | lf | 33.37 | 49.66 | 33.81 | 41.72 | 106.3 |
| upstream-string | crlf | 307.42 | 186.34 | 307.62 | 186.75 | 211.8 |
| upstream-string | lf | 307.04 | 185.99 | 307.28 | 186.39 | 251.9 |

## Limits of this evidence

The file-backed string route constructs its complete string before SDK serialization. The streaming route reads the same file incrementally. The separate application integration has its own final-string construction mechanism and requires its own measurements.

The client still accumulates a complete event and returns a complete decoded string. Fixed-size SSE segments remove exponentially growing pool rentals, but do not turn the client API into constant-memory consumption. Post-repetition client occupied heap can approach its fixed limit. This run proves 100 requests, not an indefinite lifetime.

Heap/LOH fields describe the most recent GC and can be stale. This small public harness records endpoints and lifetime RSS, not sampled heap peaks or object roots. Allocation profiles and final-head integration evidence are separate acceptance items. Failed and incomplete responses remain in the JSON evidence.

## Reproduce

Use .NET SDK 10.0.301. Build the same reproduction against the upstream base and feature branch, passing `-p:StreamingApi=true` only for the feature branch. `McpSourceRoot` may select an isolated source checkout. Run from the SDK checkout; do not globally override `TargetFrameworks`, since the analyzer targets netstandard2.0.

```sh
dotnet publish docs/experiments/bounded-streaming/repro/Repro.csproj -c Release -p:StreamingApi=true -o /tmp/mcp-candidate-host
dotnet /tmp/mcp-candidate-host/Repro.dll fixture /tmp/mcp-fixtures string
python3 docs/experiments/bounded-streaming/repro/run.py --host /tmp/mcp-candidate-host --fixtures /tmp/mcp-fixtures --output /tmp/mcp-results --route stream --image mcr.microsoft.com/dotnet/sdk@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29
```

Repeat with `--route string`; build the upstream variant without `StreamingApi=true`. The raw JSON includes exact source and binary identities for the preliminary run.
