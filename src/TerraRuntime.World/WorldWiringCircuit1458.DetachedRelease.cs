namespace TerraRuntime.World;

internal delegate bool WorldWiringTileRead(int x, int y, out WorldTile tile);
internal delegate bool WorldWiringFrameWrite(int x, int y, in WorldTile before, in WorldTile after);

public sealed partial class WorldWiringCircuit1458
{
    internal bool CanStageDetachedRelease => !_running && !drainingLogic && !traversingWires &&
        _pending.Count == 0 && _logicLamps.Count == 0 && _gatesCurrent.Count == 0 && _gatesNext.Count == 0;

    // Only wire runs through empty cells and ordinary torches can participate in the
    // detached destruction transaction. Reaching any other device rejects the entire plan.
    internal bool TryStageDetachedRelease(int x, int y, WorldWiringTileRead read, WorldWiringFrameWrite write)
    {
        if (!CanStageDetachedRelease || !Contains(x, y) || !read(x, y, out var origin) ||
            !origin.IsActive || origin.Type is not (423 or 428)) return false;
        if ((origin.Flags & (WorldTileFlags.WireRed | WorldTileFlags.WireBlue |
            WorldTileFlags.WireGreen | WorldTileFlags.WireYellow)) == 0) return true;

        int seed = Index(x, y);
        var release = new DetachedRelease(seed, read, write);
        for (var channel = WorldWireChannel.Red; channel <= WorldWireChannel.Yellow; channel++)
        {
            if (!HasWire(origin, channel)) continue;
            release.RemainingEntrances.Clear();
            release.RemainingEntrances.Add(seed, 4);
            release.Pending.Enqueue(new(x, y, 0));
            if (!Traverse(channel, release)) return false;
        }
        return CanStageDetachedRelease;
    }

    // No live callbacks or mechanism commands ran during preview. An accepted
    // ordinary release ends with the same transient pulse flags as an empty logic pass.
    internal void AdoptDetachedRelease()
    {
        BeginLogicPulse();
        ClearPulseState();
    }

    private sealed class DetachedRelease(int seed, WorldWiringTileRead read, WorldWiringFrameWrite write)
    {
        internal int Seed { get; } = seed;
        internal WorldWiringTileRead Read { get; } = read;
        internal Queue<WireVisit> Pending { get; } = new();
        internal Dictionary<int, byte> RemainingEntrances { get; } = new();

        internal bool TryHit(int x, int y)
        {
            if (!Read(x, y, out var before) || before.HasActuator || before.IsActuated) return false;
            if (!before.IsActive) return true;
            return WorldWiringDeviceExecutor1458.TryTurnTorch(in before, out var after) && write(x, y, in before, in after);
        }
    }
}
