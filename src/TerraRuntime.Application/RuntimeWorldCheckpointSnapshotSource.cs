using TerraRuntime.World;

namespace TerraRuntime;

internal readonly record struct RuntimeWorldClockSaveState(
    double Time,
    bool DayTime,
    VanillaMoonPhase MoonPhase,
    double SlimeRainTime);

/// <summary>
/// Deliberately partial persistence snapshot containing the authoritative subsystems currently supported by the
/// lossless world rewriter: world tiles, world chests, canonical world signs, town NPC/room state, runtime clock fields and detached
/// progression mutations completed since the canonical .wld was loaded.
/// </summary>
internal sealed record RuntimeWorldCheckpointSnapshot(
    WorldTileSaveImage Tiles,
    WorldChest[] Chests,
    RuntimeWorldClockSaveState? Clock = null,
    WorldSign[]? Signs = null,
    RuntimeWorldProgressionMutationSnapshot? ProgressionMutations = null,
    WorldNpcPersistence? Npcs = null,
    WorldTownRoom[]? TownRooms = null);

/// <summary>
/// Game-thread-owned snapshot source for the current persistence slice. Tile copying is spread across bounded section
/// captures; requesting a snapshot then copies only section references plus detached chest/clock/sign/progression state.
/// </summary>
internal sealed class RuntimeWorldCheckpointSnapshotSource
{
    private readonly WorldTileSaveShadowSynchronizer tileSynchronizer;
    private readonly RuntimeChestStore chestStore;
    private readonly RuntimeWorldClock? worldClock;
    private readonly RuntimeSignStore? signStore;
    private readonly RuntimeTownNpcStateStore? townNpcStore;
    private readonly RuntimeWorldProgressionMutations progressionMutations;

    public RuntimeWorldCheckpointSnapshotSource(
        WorldTileStore tiles,
        RuntimeChestStore chestStore,
        int dirtyBatchCapacity,
        RuntimeWorldClock? worldClock = null,
        RuntimeSignStore? signStore = null,
        RuntimeTownNpcStateStore? townNpcStore = null,
        RuntimeWorldProgressionMutations? progressionMutations = null)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(dirtyBatchCapacity, 1);
        ArgumentNullException.ThrowIfNull(chestStore);
        this.chestStore = chestStore;
        this.worldClock = worldClock;
        this.signStore = signStore;
        this.townNpcStore = townNpcStore;
        this.progressionMutations = progressionMutations ?? new RuntimeWorldProgressionMutations();
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
    /// chest, clock, sign, town-NPC and progression state cannot interleave with the tile-image reference capture.
    /// </summary>
    public bool TryCapture(out RuntimeWorldCheckpointSnapshot? snapshot)
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

        snapshot = new RuntimeWorldCheckpointSnapshot(
            tiles,
            chestStore.CaptureSnapshot(),
            clock,
            signs,
            progressionMutations.CaptureSnapshot(),
            townNpcStore?.CaptureNpcPersistence(),
            townNpcStore?.CaptureTownRooms());
        return true;
    }
}
