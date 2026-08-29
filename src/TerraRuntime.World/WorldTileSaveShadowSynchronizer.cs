namespace TerraRuntime.World;

/// <summary>
/// Authoritative-thread bridge that incrementally seeds a persistence tile shadow, then keeps it current from dirty
/// section snapshots. Both bootstrap and steady-state capture are explicitly bounded per call.
/// </summary>
public sealed class WorldTileSaveShadowSynchronizer
{
    private readonly WorldTileStore liveTiles;
    private readonly DirtySectionSnapshotBatcher dirtyBatcher;
    private int nextBootstrapSectionIndex;

    public WorldTileSaveShadowSynchronizer(WorldTileStore liveTiles, int dirtyBatchCapacity)
    {
        ArgumentNullException.ThrowIfNull(liveTiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(dirtyBatchCapacity, 1);

        this.liveTiles = liveTiles;
        dirtyBatcher = new DirtySectionSnapshotBatcher(liveTiles, dirtyBatchCapacity);
        Shadow = new IncrementalWorldTileSaveShadow(liveTiles.Dimensions);
    }

    public IncrementalWorldTileSaveShadow Shadow { get; }

    public bool IsBootstrapped => nextBootstrapSectionIndex == liveTiles.Dimensions.SectionCount;

    public int RemainingBootstrapSections => liveTiles.Dimensions.SectionCount - nextBootstrapSectionIndex;

    /// <summary>
    /// Captures at most <paramref name="maximumSections"/> initial sections in deterministic linear order.
    /// A section that cannot be captured consistently is retried by the next call instead of being skipped.
    /// Dirty tracking is not drained until bootstrap completes, so mutations to sections captured early remain queued.
    /// </summary>
    public int CaptureBootstrap(int maximumSections)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSections);
        int captured = 0;
        while (captured < maximumSections && !IsBootstrapped)
        {
            WorldSectionId section = TerrariaSectionGeometry.FromLinearIndex(
                liveTiles.Dimensions,
                nextBootstrapSectionIndex);
            if (!liveTiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? snapshot) || snapshot is null)
                break;

            if (!Shadow.TryApply(snapshot))
            {
                throw new InvalidOperationException(
                    "Initial save-shadow section capture was not newer than an uninitialized shadow section.");
            }

            nextBootstrapSectionIndex++;
            captured++;
        }

        return captured;
    }

    /// <summary>
    /// Applies up to <paramref name="maximumSections"/> dirty sections after bootstrap. Calling this before bootstrap
    /// is complete is a no-op so dirty markers remain intact for the first steady-state synchronization pass.
    /// </summary>
    public int CaptureDirty(int maximumSections)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSections);
        if (!IsBootstrapped || maximumSections == 0)
            return 0;

        int maximum = Math.Min(maximumSections, dirtyBatcher.Capacity);
        _ = dirtyBatcher.Capture(maximum);
        return Shadow.Apply(dirtyBatcher.Captured);
    }

    public bool TryCaptureImage(out WorldTileSaveImage? image) => Shadow.TryCaptureImage(out image);
}
