# Diagnostic buffer attribution

[Sanitized SDK-only profile statistics and allocation stacks](buffer-profiles.json) complement the public reproduction. They come from a separate diagnostic host using synthetic complete text of approximately 8.45 million UTF-16 characters, with the same source revisions and resource limits. They are not presented as the public harness's timing measurements. Profiling and heap collection perturb execution.

The upstream server allocates five large pooled payloads for one result: 128 MiB character escaping, 32 MiB UTF-8 `SerializeToNode`, 16 MiB JSON document metadata, 64 MiB node re-escaping and 16 MiB complete SSE buffering. Their combined payload is 256 MiB, before object headers, application text and runtime overhead. Allocation stacks in the JSON identify these mechanisms. The client has independent SSE growth and result conversion costs.

An intermediate patch removed server and SSE growth buffers but still rented 16 MiB through `Utf8JsonReader.CopyString`/`CopyValue`. The final source also bounds that unescape scratch by decoding complete JSON escape/scalar chunks through the framework reader.

Final single-response and 100-request/concurrency-ten profiles show no ArrayPool rental or allocation of 1 MiB or greater in either process, for either server content route. Smaller buffers remain: 4,096-character source segments, 32 KiB streaming JSON storage, bounded 64/32 KiB pipe thresholds and 16 KiB writes; the client SSE reader uses 8 KiB segments. The recorder captures allocations >=16 KiB and other rental/return events >=1 MiB, so it does not establish absence of smaller rentals.

The client still owns full-event segments, raw result bytes and the final decoded string. Removing reusable scratch pools does not remove those payload-sized allocations. Dead large objects can remain in an uncollected heap snapshot.

A separate diagnostic explicitly collects at phase boundaries after the load. Upstream after one response retains about 267.4 MiB server / 169.9 MiB client occupied managed memory; the final string route after 100 responses retains about 6.5 / 27.0 MiB, and the final streaming route about 2.7 / 26.3 MiB. These intrusive post-GC observations are not normal-load peaks or a per-request GC strategy. Pool trimming may be affected. SOS `dumpheap -live` omits some thread-static roots in this environment, so the JSON's reported live size must not be treated as exact retained memory. Normal unforced-GC acceptance remains in the separate result report.
