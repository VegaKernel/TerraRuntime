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

    // RemoteClient.CheckSection uses a3x3 destination window. Reuse the existing asynchronous
    // cache/rebuild and opportunistic queue budgets; unavailable sections retry on the teleport ack13.
    internal void PrepareServerTeleportSections(float x, float y)
    {
        var streaming = _sectionStreaming;
        if (streaming is null || !float.IsFinite(x) || !float.IsFinite(y)) return;
        int tileX = (int)(x / 16f), tileY = (int)(y / 16f);
        if (tileX < 0 || tileY < 0) return;
        int sectionX = tileX / TerrariaSectionGeometry.WidthTiles, sectionY = tileY / TerrariaSectionGeometry.HeightTiles;
        Span<WorldSectionId> pending = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumWindowSectionCount];
        int count = streaming.PlanUnsent(x, y, pending);
        for (int i = 0; i < count; i++)
        {
            var section = pending[i];
            if (Math.Abs(section.X - sectionX) > 1 || Math.Abs(section.Y - sectionY) > 1) continue;
            if (!_packets.TryGetCurrentCachedSectionFrame(section, out var frame) || !TryQueueOpportunistic(frame)) continue;
            streaming.MarkSent(section);
        }
    }
}
