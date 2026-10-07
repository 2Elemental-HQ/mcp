using ModelContextProtocol.Client;
using System.Net.ServerSentEvents;
using System.Buffers;
using System.Text;

namespace ModelContextProtocol.Tests.Transport;

/// <summary>
/// Compares segmented parsing with the framework parser across framing and read boundaries.
/// </summary>
public sealed class SegmentedSseParserTests
{
    /// <summary>
    /// SSE fields, incomplete frames, Unicode and line endings retain framework semantics.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8191)]
    public async Task FramingMatchesFramework(int fragmentSize)
    {
        string[] inputs = [
            "\uFEFF:comment\rdata: alpha\r\ndata: béta😀\nid: event-1\nretry: 1000\n\ndata\n\n",
            "event: custom\n\ndata: a\n\nid:\nretry: +2\ndata: b\n\n",
            "id: ignored\0value\nretry: 922337203685477\ndata: value\n\n",
            "retry: 9223372036854775807\nunknown: x\ndata:  leading space\n\ndata: incomplete",
            string.Concat("data: ", new string('x', 100000), "😀\r\ndata: tail\r\n\r\n"),
            "data: incomplete\r", "data: complete\r\r", "data: empty-next\n\ndata:\n\n"
        ];
        foreach (var input in inputs)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            using var expectedStream = new FragmentedStream(bytes, fragmentSize);
            using var actualStream = new FragmentedStream(bytes, fragmentSize);
            var expected = new List<SseItem<string>>();
            await foreach (var item in SseParser.Create(expectedStream).EnumerateAsync(TestContext.Current.CancellationToken)) expected.Add(item);
            var actual = new List<SseItem<string>>();
            var parser = new SegmentedSseParser<string>(actualStream, (_, sequence) => Encoding.UTF8.GetString(sequence.ToArray()));
            await foreach (var item in parser.EnumerateAsync(TestContext.Current.CancellationToken)) actual.Add(item);
            Assert.Equal(expected.Count, actual.Count);
            for (int index = 0; index < expected.Count; index++)
            {
                Assert.Equal(expected[index].Data, actual[index].Data);
                Assert.Equal(expected[index].EventType, actual[index].EventType);
                Assert.Equal(expected[index].EventId, actual[index].EventId);
                Assert.Equal(expected[index].ReconnectionInterval, actual[index].ReconnectionInterval);
            }
        }
    }

    /// <summary>
    /// Fragments reads independently of UTF-8 and SSE field boundaries.
    /// </summary>
    private sealed class FragmentedStream(byte[] bytes, int fragmentSize) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer.Slice(0, Math.Min(fragmentSize, buffer.Length)), cancellationToken);
    }
}
