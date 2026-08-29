using System;
using System.Buffers.Binary;
using System.Threading;
using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

/// <summary>
/// Direction of a Terraria wire frame relative to the server process.
/// </summary>
public enum TerrariaMessageDirection : byte
{
    Inbound = 0,
    Outbound = 1
}

/// <summary>
/// Process-lifetime per-message traffic counters plus a bounded rolling window used for
/// operational top-message diagnostics. The packet hot path performs only fixed-array
/// counter updates; formatting and detail projection happen when a snapshot is requested.
/// </summary>
public sealed class TerrariaMessageTrafficTelemetry
{
    private const int DirectionStride = byte.MaxValue + 1;
    private const int CounterCount = DirectionStride * 2;
    private const int DefaultBucketCount = 6;
    private static readonly TimeSpan DefaultBucketDuration = TimeSpan.FromSeconds(10);
    private static readonly bool[] KnownMessageIds = CreateKnownMessageIds();

    private readonly TimeProvider timeProvider;
    private readonly long bucketTicks;
    private readonly Bucket?[] buckets;
    private readonly object bucketSync = new();
    private readonly long[] lifetimeFrames = new long[CounterCount];
    private readonly long[] lifetimeBytes = new long[CounterCount];

    private long inboundFrames;
    private long inboundBytes;
    private long outboundFrames;
    private long outboundBytes;
    private long unknownInboundFrames;
    private long unknownOutboundFrames;
    private long malformedInboundFrames;
    private long malformedOutboundFrames;

    public static TerrariaMessageTrafficTelemetry Shared { get; } = new();

    public TerrariaMessageTrafficTelemetry(
        TimeProvider? timeProvider = null,
        TimeSpan? bucketDuration = null,
        int bucketCount = DefaultBucketCount)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        TimeSpan duration = bucketDuration ?? DefaultBucketDuration;
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(bucketDuration));
        if (bucketCount is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(bucketCount));

        bucketTicks = duration.Ticks;
        buckets = new Bucket?[bucketCount];
        Window = TimeSpan.FromTicks(checked(bucketTicks * bucketCount));
    }

    public TimeSpan Window { get; }

    public void Observe(TerrariaMessageDirection direction, byte messageId, int frameBytes)
    {
        if (frameBytes < 3)
            throw new ArgumentOutOfRangeException(nameof(frameBytes));

        int index = GetIndex(direction, messageId);
        Interlocked.Increment(ref lifetimeFrames[index]);
        Interlocked.Add(ref lifetimeBytes[index], frameBytes);

        if (direction == TerrariaMessageDirection.Inbound)
        {
            Interlocked.Increment(ref inboundFrames);
            Interlocked.Add(ref inboundBytes, frameBytes);
            if (!KnownMessageIds[messageId])
                Interlocked.Increment(ref unknownInboundFrames);
        }
        else if (direction == TerrariaMessageDirection.Outbound)
        {
            Interlocked.Increment(ref outboundFrames);
            Interlocked.Add(ref outboundBytes, frameBytes);
            if (!KnownMessageIds[messageId])
                Interlocked.Increment(ref unknownOutboundFrames);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
        }

        Bucket bucket = GetCurrentBucket();
        Interlocked.Increment(ref bucket.Frames[index]);
        Interlocked.Add(ref bucket.Bytes[index], frameBytes);
    }

    public void RecordMalformed(TerrariaMessageDirection direction)
    {
        switch (direction)
        {
            case TerrariaMessageDirection.Inbound:
                Interlocked.Increment(ref malformedInboundFrames);
                return;
            case TerrariaMessageDirection.Outbound:
                Interlocked.Increment(ref malformedOutboundFrames);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
        }
    }

    /// <summary>
    /// Accounts one or more already encoded outbound frames after the socket write succeeds.
    /// A malformed internal frame is counted and terminates inspection of the remaining buffer;
    /// telemetry never indexes beyond the supplied bytes.
    /// </summary>
    public void ObserveEncodedOutbound(ReadOnlySpan<byte> bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int remaining = bytes.Length - offset;
            if (remaining < 3)
            {
                RecordMalformed(TerrariaMessageDirection.Outbound);
                return;
            }

            int packetLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
            if (packetLength < 3 || packetLength > remaining)
            {
                RecordMalformed(TerrariaMessageDirection.Outbound);
                return;
            }

            Observe(TerrariaMessageDirection.Outbound, bytes[offset + 2], packetLength);
            offset += packetLength;
        }
    }

    public TerrariaMessageTrafficTelemetrySnapshot CaptureSnapshot(int maximumTopDetails = 8)
    {
        if (maximumTopDetails < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTopDetails));

        long currentEpoch = GetEpoch();
        long minimumEpoch = currentEpoch - buckets.Length + 1L;
        Bucket?[] activeBuckets = new Bucket?[buckets.Length];
        int activeBucketCount = 0;
        for (int i = 0; i < buckets.Length; i++)
        {
            Bucket? bucket = Volatile.Read(ref buckets[i]);
            if (bucket is not null && bucket.Epoch >= minimumEpoch && bucket.Epoch <= currentEpoch)
                activeBuckets[activeBucketCount++] = bucket;
        }

        var details = new TerrariaMessageTrafficDetail[CounterCount];
        int detailCount = 0;
        TerrariaMessageTrafficDetail[] top = maximumTopDetails == 0
            ? []
            : new TerrariaMessageTrafficDetail[maximumTopDetails];
        int topCount = 0;

        for (int index = 0; index < CounterCount; index++)
        {
            long totalFrames = Interlocked.Read(ref lifetimeFrames[index]);
            if (totalFrames == 0)
                continue;

            long totalBytes = Interlocked.Read(ref lifetimeBytes[index]);
            long windowFrames = 0;
            long windowBytes = 0;
            for (int bucketIndex = 0; bucketIndex < activeBucketCount; bucketIndex++)
            {
                Bucket bucket = activeBuckets[bucketIndex]!;
                windowFrames += Interlocked.Read(ref bucket.Frames[index]);
                windowBytes += Interlocked.Read(ref bucket.Bytes[index]);
            }

            TerrariaMessageDirection direction = index < DirectionStride
                ? TerrariaMessageDirection.Inbound
                : TerrariaMessageDirection.Outbound;
            byte messageId = (byte)(index % DirectionStride);
            var detail = new TerrariaMessageTrafficDetail(
                Direction: direction,
                MessageId: messageId,
                IsKnownMessageId: KnownMessageIds[messageId],
                TotalFrames: totalFrames,
                TotalBytes: totalBytes,
                WindowFrames: windowFrames,
                WindowBytes: windowBytes);
            details[detailCount++] = detail;

            if (maximumTopDetails > 0 && (windowFrames != 0 || windowBytes != 0))
                InsertTop(top, ref topCount, detail);
        }

        return new TerrariaMessageTrafficTelemetrySnapshot(
            InboundFrames: Interlocked.Read(ref inboundFrames),
            InboundBytes: Interlocked.Read(ref inboundBytes),
            OutboundFrames: Interlocked.Read(ref outboundFrames),
            OutboundBytes: Interlocked.Read(ref outboundBytes),
            UnknownInboundFrames: Interlocked.Read(ref unknownInboundFrames),
            UnknownOutboundFrames: Interlocked.Read(ref unknownOutboundFrames),
            MalformedInboundFrames: Interlocked.Read(ref malformedInboundFrames),
            MalformedOutboundFrames: Interlocked.Read(ref malformedOutboundFrames),
            Window: Window,
            Messages: detailCount == 0
                ? ReadOnlyMemory<TerrariaMessageTrafficDetail>.Empty
                : details.AsMemory(0, detailCount),
            TopMessages: topCount == 0
                ? ReadOnlyMemory<TerrariaMessageTrafficDetail>.Empty
                : top.AsMemory(0, topCount));
    }

    private Bucket GetCurrentBucket()
    {
        long epoch = GetEpoch();
        int slot = (int)(epoch % buckets.Length);
        Bucket? bucket = Volatile.Read(ref buckets[slot]);
        if (bucket?.Epoch == epoch)
            return bucket;

        lock (bucketSync)
        {
            bucket = buckets[slot];
            if (bucket?.Epoch == epoch)
                return bucket;

            bucket = new Bucket(epoch);
            Volatile.Write(ref buckets[slot], bucket);
            return bucket;
        }
    }

    private long GetEpoch() => timeProvider.GetUtcNow().Ticks / bucketTicks;

    private static int GetIndex(TerrariaMessageDirection direction, byte messageId) => direction switch
    {
        TerrariaMessageDirection.Inbound => messageId,
        TerrariaMessageDirection.Outbound => DirectionStride + messageId,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
    };

    private static void InsertTop(
        TerrariaMessageTrafficDetail[] top,
        ref int count,
        TerrariaMessageTrafficDetail candidate)
    {
        int insertAt = count;
        for (int i = 0; i < count; i++)
        {
            if (Compare(candidate, top[i]) < 0)
            {
                insertAt = i;
                break;
            }
        }

        if (insertAt >= top.Length)
            return;

        int last = Math.Min(count, top.Length - 1);
        for (int i = last; i > insertAt; i--)
            top[i] = top[i - 1];

        top[insertAt] = candidate;
        if (count < top.Length)
            count++;
    }

    private static int Compare(TerrariaMessageTrafficDetail left, TerrariaMessageTrafficDetail right)
    {
        int bytes = right.WindowBytes.CompareTo(left.WindowBytes);
        if (bytes != 0)
            return bytes;

        int frames = right.WindowFrames.CompareTo(left.WindowFrames);
        if (frames != 0)
            return frames;

        int direction = left.Direction.CompareTo(right.Direction);
        return direction != 0 ? direction : left.MessageId.CompareTo(right.MessageId);
    }

    private static bool[] CreateKnownMessageIds()
    {
        var known = new bool[DirectionStride];
        foreach (TerrariaMessageId messageId in Enum.GetValues<TerrariaMessageId>())
            known[(byte)messageId] = true;
        return known;
    }

    private sealed class Bucket(long epoch)
    {
        public long Epoch { get; } = epoch;
        public long[] Frames { get; } = new long[CounterCount];
        public long[] Bytes { get; } = new long[CounterCount];
    }
}

public readonly record struct TerrariaMessageTrafficTelemetrySnapshot(
    long InboundFrames,
    long InboundBytes,
    long OutboundFrames,
    long OutboundBytes,
    long UnknownInboundFrames,
    long UnknownOutboundFrames,
    long MalformedInboundFrames,
    long MalformedOutboundFrames,
    TimeSpan Window,
    ReadOnlyMemory<TerrariaMessageTrafficDetail> Messages,
    ReadOnlyMemory<TerrariaMessageTrafficDetail> TopMessages);

public readonly record struct TerrariaMessageTrafficDetail(
    TerrariaMessageDirection Direction,
    byte MessageId,
    bool IsKnownMessageId,
    long TotalFrames,
    long TotalBytes,
    long WindowFrames,
    long WindowBytes);
