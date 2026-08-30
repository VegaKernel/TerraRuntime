using TerraRuntime.World;

namespace TerraRuntime;

internal readonly record struct RuntimeWorldClockSaveState(
    double Time,
    bool DayTime,
    VanillaMoonPhase MoonPhase,
    double SlimeRainTime);

/// <summary>
/// Deliberately partial persistence snapshot containing the authoritative subsystems currently supported by the
/// lossless world rewriter: world tiles, world chests, canonical world signs, runtime clock fields and detached
/// progression mutations completed since the canonical .wld was loaded.
/// </summary>
internal sealed record RuntimeWorldTileChestSaveSnapshot(
    WorldTileSaveImage Tiles,
    WorldChest[] Chests,
    RuntimeWorldClockSaveState? Clock = null,
    WorldSign[]? Signs = null,
    RuntimeWorldProgressionMutationSnapshot? ProgressionMutations = null);

/// <summary>
/// Game-thread-owned snapshot source for the current persistence slice. Tile copying is spread across bounded section
/// captures; requesting a snapshot then copies only section references plus detached chest/clock/sign/progression state.
/// </summary>
internal sealed class RuntimeWorldTileChestSaveSnapshotSource
{
    private readonly WorldTileSaveShadowSynchronizer tileSynchronizer;
    private readonly RuntimeChestStore chestStore;
    private readonly RuntimeWorldClock? worldClock;
    private readonly RuntimeSignStore? signStore;
    private readonly RuntimeWorldProgressionMutations progressionMutations;

    public RuntimeWorldTileChestSaveSnapshotSource(
        WorldTileStore tiles,
        RuntimeChestStore chestStore,
        int dirtyBatchCapacity,
        RuntimeWorldClock? worldClock = null,
        RuntimeSignStore? signStore = null)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(dirtyBatchCapacity, 1);
        ArgumentNullException.ThrowIfNull(chestStore);
        this.chestStore = chestStore;
        this.worldClock = worldClock;
        this.signStore = signStore;
        progressionMutations = RuntimeWorldProgressionRegistry.GetOrCreate(tiles);
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
    /// Captures one detached persistence image. The caller must invoke this on the authoritative owner so mutable
    /// chest, clock, sign and progression state cannot interleave with the tile-image reference capture.
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

        WorldSign[]? signs = null;
        if (signStore is not null && signStore.TryCaptureCanonicalSnapshot(out WorldSign[] signSnapshot))
            signs = signSnapshot;

        snapshot = new RuntimeWorldTileChestSaveSnapshot(
            tiles,
            chestStore.CaptureSnapshot(),
            clock,
            signs,
            progressionMutations.CaptureSnapshot());
        return true;
    }
}
