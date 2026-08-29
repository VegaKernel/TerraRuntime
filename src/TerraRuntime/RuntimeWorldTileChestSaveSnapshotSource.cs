using TerraRuntime.World;

namespace TerraRuntime;

internal readonly record struct RuntimeWorldClockSaveState(
    double Time,
    bool DayTime,
    byte MoonPhase,
    double SlimeRainTime);

/// <summary>
/// Deliberately partial persistence snapshot containing the authoritative subsystems currently supported by the
/// lossless world rewriter: world tiles, world chests and the runtime clock fields patched into the opaque header.
/// </summary>
internal sealed record RuntimeWorldTileChestSaveSnapshot(
    WorldTileSaveImage Tiles,
    WorldChest[] Chests,
    RuntimeWorldClockSaveState? Clock = null);

/// <summary>
/// Game-thread-owned snapshot source for the current tile/chest persistence slice. Tile copying is spread across
/// bounded section captures; requesting a snapshot then copies only section references plus detached chest/clock state.
/// </summary>
internal sealed class RuntimeWorldTileChestSaveSnapshotSource
{
    private readonly WorldTileSaveShadowSynchronizer tileSynchronizer;
    private readonly RuntimeChestStore chestStore;
    private readonly RuntimeWorldClock? worldClock;

    public RuntimeWorldTileChestSaveSnapshotSource(
        WorldTileStore tiles,
        RuntimeChestStore chestStore,
        int dirtyBatchCapacity,
        RuntimeWorldClock? worldClock = null)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(dirtyBatchCapacity, 1);
        ArgumentNullException.ThrowIfNull(chestStore);
        this.chestStore = chestStore;
        this.worldClock = worldClock;
        tileSynchronizer = new WorldTileSaveShadowSynchronizer(tiles, dirtyBatchCapacity);
    }

    public bool IsTileShadowReady => tileSynchronizer.IsBootstrapped;

    public int RemainingBootstrapSections => tileSynchronizer.RemainingBootstrapSections;

    public int PendingDirtyTileSections => tileSynchronizer.PendingDirtySections;

    public int CaptureTileBootstrap(int maximumSections) =>
        tileSynchronizer.CaptureBootstrap(maximumSections);

    public int CaptureDirtyTiles(int maximumSections) =>
        tileSynchronizer.CaptureDirty(maximumSections);

    /// <summary>
    /// Captures one detached persistence image. The caller must invoke this on the authoritative owner so chest and
    /// clock state cannot interleave with the tile-image reference capture.
    /// </summary>
    public bool TryCapture(out RuntimeWorldTileChestSaveSnapshot? snapshot)
    {
        if (!tileSynchronizer.TryCaptureImage(out WorldTileSaveImage? tiles) || tiles is null)
        {
            snapshot = null;
            return false;
        }

        RuntimeWorldClockSaveState? clock = worldClock is null
            ? null
            : new RuntimeWorldClockSaveState(
                worldClock.Time,
                worldClock.DayTime,
                worldClock.MoonPhase,
                worldClock.SlimeRainTime);
        snapshot = new RuntimeWorldTileChestSaveSnapshot(
            tiles,
            chestStore.CaptureSnapshot(),
            clock);
        return true;
    }
}
