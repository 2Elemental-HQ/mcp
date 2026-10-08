using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Synthetic text only. Both APIs expose exactly the same single text content block.
var mode = args[0];
var fixture = args[1];
var route = args[2];
if (mode == "fixture")
{
    Directory.CreateDirectory(fixture);
    foreach (var ending in new[] { "lf", "crlf" })
    {
        var line = string.Concat(new string('x', 96), " é 😀 <>&\\\"", ending == "lf" ? "\n" : "\r\n");
        var body = new StringBuilder();
        while (body.Length < 3145728) body.Append(line);
        // Nested JSON mirrors a text tool result containing Markdown and Body without any private schema.
        var text = JsonSerializer.Serialize(new { Markdown = body.ToString(), Body = body.ToString() });
        await File.WriteAllTextAsync(Path.Combine(fixture, $"{ending}.txt"), text, new UTF8Encoding(false));
        Console.WriteLine(JsonSerializer.Serialize(new { ending, characters = text.Length, bytes = Encoding.UTF8.GetByteCount(text), sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))) }));
    }
    return;
}
if (mode == "server")
{
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
    builder.WebHost.UseUrls("http://0.0.0.0:8080");
    builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)
        .WithTools([McpServerTool.Create(async (CancellationToken token) =>
        {
            ContentBlock block;
#if STREAMING_API
            if (route == "stream") block = new StreamingTextContentBlock(ReadText);
            else
#endif
            block = new TextContentBlock { Text = await File.ReadAllTextAsync(fixture, token) };
            return new CallToolResult { Content = [block] };
        }, new() { Name = "synthetic" })]);
    var app = builder.Build();
    app.MapMcp();
    app.MapGet("/metrics", () => Snapshot());
    await app.RunAsync();
    return;
}
var count = int.Parse(args[3]);
var concurrency = int.Parse(args[4]);
var expected = (await File.ReadAllTextAsync(fixture)).Trim();
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
await using var client = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = new Uri(args[5]), TransportMode = HttpTransportMode.StreamableHttp }, http), new McpClientOptions { ProtocolVersion = "2025-11-25" });
var before = Snapshot();
var timer = Stopwatch.StartNew();
var results = new List<object>();
for (int offset = 0; offset < count; offset += concurrency)
{
    var batch = await Task.WhenAll(Enumerable.Range(offset, Math.Min(concurrency, count - offset)).Select(async index =>
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await client.CallToolAsync("synthetic");
            var text = ((TextContentBlock)result.Content.Single()).Text;
            // Hash UTF-8 incrementally; do not add a whole result-sized validation byte buffer.
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var encoder = Encoding.UTF8.GetEncoder();
            var bytes = new byte[16384];
            for (int position = 0; position < text.Length;)
            {
                int length = Math.Min(4096, text.Length - position);
                int written = encoder.GetBytes(text.AsSpan(position, length), bytes, position + length == text.Length);
                hash.AppendData(bytes.AsSpan(0, written));
                position += length;
            }
            var sha256 = Convert.ToHexString(hash.GetHashAndReset());
            return (object)new { index, success = sha256 == expected, sha256, characters = text.Length, milliseconds = watch.Elapsed.TotalMilliseconds };
        }
        catch (Exception error) { return (object)new { index, success = false, error = error.GetType().FullName, detail = error.Message, milliseconds = watch.Elapsed.TotalMilliseconds }; }
    }));
    results.AddRange(batch);
}
Console.WriteLine(JsonSerializer.Serialize(new { route, count, concurrency, milliseconds = timer.Elapsed.TotalMilliseconds, before, after = Snapshot(), results }));

#if STREAMING_API
async IAsyncEnumerable<ReadOnlyMemory<char>> ReadText([EnumeratorCancellation] CancellationToken token)
{
    using var reader = new StreamReader(fixture, Encoding.UTF8, true, 4096);
    var buffer = new char[4096];
    int read;
    while ((read = await reader.ReadAsync(buffer, token)) != 0) yield return buffer.AsMemory(0, read);
}
#endif

static object Snapshot()
{
    using var process = Process.GetCurrentProcess();
    var info = GC.GetGCMemoryInfo();
    return new { availableHeap = info.TotalAvailableMemoryBytes, allocated = GC.GetTotalAllocatedBytes(false), occupied = GC.GetTotalMemory(false), heapAfterLastGc = info.HeapSizeBytes, fragmentedAfterLastGc = info.FragmentedBytes, lohAfterLastGc = info.GenerationInfo.Length > 3 ? info.GenerationInfo[3].SizeAfterBytes : 0, rss = process.WorkingSet64, peakRss = process.PeakWorkingSet64, gen2 = GC.CollectionCount(2) };
}
