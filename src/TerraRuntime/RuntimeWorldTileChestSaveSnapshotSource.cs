using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Deliberately partial persistence snapshot containing only the subsystems that currently have source-backed
/// canonical encoders: world tiles and world chests. It must not be treated as a complete .wld image.
/// </summary>
internal sealed record RuntimeWorldTileChestSaveSnapshot(
    WorldTileSaveImage Tiles,
    WorldChest[] Chests);

/// <summary>
/// Game-thread-owned snapshot source for the current tile/chest persistence slice. Tile copying is spread across
/// bounded section captures; requesting a snapshot then copies only section references plus detached chest state.
/// </summary>
internal sealed class RuntimeWorldTileChestSaveSnapshotSource
{
    private readonly WorldTileSaveShadowSynchronizer tileSynchronizer;
    private readonly RuntimeChestStore chestStore;

    public RuntimeWorldTileChestSaveSnapshotSource(
        WorldTileStore tiles,
        RuntimeChestStore chestStore,
        int dirtyBatchCapacity)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(dirtyBatchCapacity, 1);
        ArgumentNullException.ThrowIfNull(chestStore);
        this.chestStore = chestStore;
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
    /// Captures one detached tile/chest persistence image. The caller must invoke this on the authoritative owner so
    /// no chest mutation can interleave between the tile-image reference capture and chest cloning.
    /// </summary>
    public bool TryCapture(out RuntimeWorldTileChestSaveSnapshot? snapshot)
    {
        if (!tileSynchronizer.TryCaptureImage(out WorldTileSaveImage? tiles) || tiles is null)
        {
            snapshot = null;
            return false;
        }

        snapshot = new RuntimeWorldTileChestSaveSnapshot(
            tiles,
            chestStore.CaptureSnapshot());
        return true;
    }
}
