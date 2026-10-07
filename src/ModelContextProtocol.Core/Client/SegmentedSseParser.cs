// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// SSE field semantics follow System.Net.ServerSentEvents in dotnet/runtime v10.0.0.

using System.Buffers;
using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;

namespace ModelContextProtocol.Client;

/// <summary>
/// Parses complete SSE events with fixed-size rentals rather than growing pooled line buffers.
/// Event data remains owned until the synchronous parser returns; the parser must copy data it retains.
/// </summary>
internal sealed class SegmentedSseParser<T>(Stream stream, Func<string, ReadOnlySequence<byte>, T> parse)
{
    private const int SegmentSize = 8192;
    private static readonly byte[] NewLine = [(byte)'\n'];
    private readonly List<LineBuffer> _dataLines = [];
    private Segment? _dataHead;
    private Segment? _dataTail;
    private string? _eventType;
    private string? _eventId;
    private TimeSpan? _retry;
    private bool _firstLine = true;
    private bool _used;

    /// <summary>
    /// Enumerates complete events and releases every rental on completion, cancellation or failure.
    /// </summary>
    internal async IAsyncEnumerable<SseItem<T>> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_used) throw new InvalidOperationException("The SSE stream has already been enumerated.");
        _used = true;
        byte[] input = ArrayPool<byte>.Shared.Rent(SegmentSize);
        var line = new LineBuffer();
        bool skipLf = false;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(input.AsMemory(0, SegmentSize), cancellationToken).ConfigureAwait(false)) != 0)
            {
                int offset = 0;
                while (offset < read)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (skipLf)
                    {
                        skipLf = false;
                        if (input[offset] == (byte)'\n') { offset++; continue; }
                    }
                    int newline = input.AsSpan(offset, read - offset).IndexOfAny((byte)'\r', (byte)'\n');
                    if (newline < 0)
                    {
                        line.Append(input.AsSpan(offset, read - offset));
                        break;
                    }
                    line.Append(input.AsSpan(offset, newline));
                    offset += newline;
                    skipLf = input[offset++] == (byte)'\r';
                    var completedLine = line;
                    line = new LineBuffer();
                    if (ProcessLine(completedLine, out var item)) yield return item;
                }
            }
            // An event without its terminating empty line is incomplete and must not be delivered.
        }
        finally
        {
            line.Dispose();
            ClearData();
            ArrayPool<byte>.Shared.Return(input, clearArray: true);
        }
    }

    private void AddData(ReadOnlyMemory<byte> memory)
    {
        if (memory.IsEmpty) return;
        var next = new Segment(memory);
        if (_dataTail is null) _dataHead = next;
        else _dataTail.Append(next);
        _dataTail = next;
    }

    private void ClearData()
    {
        foreach (var line in _dataLines) line.Dispose();
        _dataLines.Clear();
        _dataHead = _dataTail = null;
    }

    private bool ProcessLine(LineBuffer line, out SseItem<T> item)
    {
        bool transferred = false;
        item = default;
        try
        {
            var bytes = line.Sequence;
            if (_firstLine)
            {
                _firstLine = false;
                if (bytes.First.Span.StartsWith("\uFEFF"u8)) bytes = bytes.Slice(3);
            }
            if (bytes.IsEmpty)
            {
                if (_dataLines.Count == 0) return false;
                var data = _dataHead is null ? ReadOnlySequence<byte>.Empty : new ReadOnlySequence<byte>(_dataHead, 0, _dataTail!, _dataTail!.Memory.Length);
                try
                {
                    item = new SseItem<T>(parse(_eventType ?? SseParser.EventTypeDefault, data), _eventType) { EventId = _eventId, ReconnectionInterval = _retry };
                }
                finally { ClearData(); }
                _eventType = _eventId = null;
                _retry = null;
                return true;
            }
            var first = bytes.First.Span;
            int colon = first.IndexOf((byte)':');
            // Recognized field names fit in the first segment; longer names are unknown fields.
            var field = colon < 0 ? first : first.Slice(0, colon);
            var value = colon < 0 ? ReadOnlySequence<byte>.Empty : bytes.Slice(colon + 1);
            if (!value.IsEmpty && value.First.Span[0] == (byte)' ') value = value.Slice(1);
            if (field.SequenceEqual("data"u8))
            {
                if (_dataLines.Count != 0) AddData(NewLine);
                foreach (var memory in value) AddData(memory);
                _dataLines.Add(line);
                transferred = true;
            }
            else if (field.SequenceEqual("event"u8)) _eventType = Decode(value);
            else if (field.SequenceEqual("id"u8))
            {
                bool hasNull = false;
                foreach (var memory in value) hasNull |= memory.Span.IndexOf((byte)0) >= 0;
                if (!hasNull) _eventId = Decode(value);
            }
            else if (field.SequenceEqual("retry"u8) && long.TryParse(Decode(value), NumberStyles.None, CultureInfo.InvariantCulture, out long milliseconds))
            {
                long maximum = (long)TimeSpan.MaxValue.TotalMilliseconds;
                if (milliseconds >= 0 && milliseconds <= maximum)
                    _retry = milliseconds == maximum ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(milliseconds);
            }
            return false;
        }
        finally { if (!transferred) line.Dispose(); }
    }

    private static string Decode(ReadOnlySequence<byte> value) => Encoding.UTF8.GetString(value.ToArray());

    /// <summary>
    /// Links borrowed memory without copying an entire line or event.
    /// </summary>
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory = default) { Memory = memory; }
        internal void SetMemory(ReadOnlyMemory<byte> memory) => Memory = memory;
        internal void Append(Segment next)
        {
            next.RunningIndex = RunningIndex + Memory.Length;
            Next = next;
        }
    }

    /// <summary>
    /// Owns fixed-size line segments, which can be transferred to the current event without copying.
    /// </summary>
    private sealed class LineBuffer : IDisposable
    {
        private readonly List<byte[]> _rentals = [];
        private Segment? _head;
        private Segment? _tail;
        private byte[]? _current;
        private int _written;
        internal ReadOnlySequence<byte> Sequence => _head is null ? ReadOnlySequence<byte>.Empty : new(_head, 0, _tail!, _tail!.Memory.Length);
        internal void Append(ReadOnlySpan<byte> source)
        {
            while (!source.IsEmpty)
            {
                if (_current is null || _written == SegmentSize)
                {
                    _current = ArrayPool<byte>.Shared.Rent(SegmentSize);
                    _rentals.Add(_current);
                    var segment = new Segment();
                    if (_tail is null) _head = segment;
                    else _tail.Append(segment);
                    _tail = segment;
                    _written = 0;
                }
                int count = Math.Min(source.Length, SegmentSize - _written);
                source.Slice(0, count).CopyTo(_current.AsSpan(_written));
                _written += count;
                _tail!.SetMemory(_current.AsMemory(0, _written));
                source = source.Slice(count);
            }
        }
        public void Dispose()
        {
            foreach (var rental in _rentals) ArrayPool<byte>.Shared.Return(rental, clearArray: true);
            _rentals.Clear();
            _head = _tail = null;
            _current = null;
        }
    }
}
