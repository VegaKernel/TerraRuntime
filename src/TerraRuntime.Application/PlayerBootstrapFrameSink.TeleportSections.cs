using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

public sealed partial class PlayerBootstrapFrameSink
{
    private sealed record ServerTeleportLanding(float X, float Y);
    private ServerTeleportLanding? serverTeleportLanding;

    // The writer hands off an immutable landing; only the receive owner changes its correction fields.
    internal void CommitServerTeleport(float x, float y)
    {
        if (float.IsFinite(x) && float.IsFinite(y)) Volatile.Write(ref serverTeleportLanding, new(x, y));
    }

    private void ForgetServerTeleportLanding() => Interlocked.Exchange(ref serverTeleportLanding, null);

    private void ObserveServerTeleportLanding()
    {
        if (Interlocked.Exchange(ref serverTeleportLanding, null) is not { } landing) return;
        _correctionPositionX = landing.X; _correctionPositionY = landing.Y;
        _hasCorrectionPosition = true;
        _awaitingWorldTransferLanding = true;
    }

    // Style-0 entity teleport without extra info is a 15-byte packet 65.
    private const int WiringTeleportFrameLength = 15;
    private static readonly byte[][] TeleportSectionStatusFrames = Enumerable.Range(0, PlayerSectionStreamingState.MaximumTeleportWindowSectionCount + 1)
        .Select(PlayerJoinFrameEncoder.EncodeStatus).ToArray();

    // CheckSection visits a 3x3 landing window in X-major order and precedes packet 65.
    // Cold sections request background rebuilds through the ordinary bounded budget. Section
    // delivery retries on the landing movement if the frame or outbound space is unavailable.
    internal void PrepareServerTeleportSections(float x, float y)
    {
        var streaming = _sectionStreaming;
        if (streaming is null) return;

        Span<WorldSectionId> sections = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumTeleportWindowSectionCount];
        Span<WorldSectionId> cold = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumTeleportWindowSectionCount];
        int coldCount = 0;
        lock (streaming.SectionPublicationGate)
        {
            int count = streaming.PlanTeleportUnsent(x, y, sections);
            OutboundFrame[]? batch = null;
            int ready = 0;
            for (int i = 0; i < count; i++)
            {
                var section = sections[i];
                if (!_packets.TryGetCurrentCachedSectionFrame(section, out var frame))
                {
                    cold[coldCount++] = section;
                    continue;
                }
                batch ??= new OutboundFrame[count + 1];
                sections[ready++] = section;
                batch[ready] = new(frame);
            }

            if (batch is not null)
            {
                batch[0] = new(TeleportSectionStatusFrames[ready]);
                if (_outbound.TryEnqueueOpportunisticBatch(batch.AsSpan(0, ready + 1), remainingFrames: 1, remainingBytes: WiringTeleportFrameLength) ==
                    OutboundEnqueueResult.Enqueued)
                    for (int i = 0; i < ready; i++)
                        streaming.MarkSent(sections[i]);
            }
        }

        for (int i = 0; i < coldCount; i++)
            _packets.RequestSectionFrameInBackground(cold[i]);
    }
}
