namespace TerraRuntime.World;

public sealed partial class WorldTileStore
{
    /// <summary>Canonical mutable tile-entity metadata bound to this exact terrain owner.</summary>
    public RuntimeWorldFurnitureStore1458? RuntimeTileEntities { get; private set; }

    // Initial import is explicit: an ad-hoc tile store with no metadata owner is
    // unknown, not a proof that every furniture object is empty.
    public bool TryAttachTileEntities(ReadOnlySpan<WorldTileEntity> source, int maximumEntities)
    {
        if (RuntimeTileEntities is not null) return false;
        if (!RuntimeWorldFurnitureStore1458.TryImport(this, source, maximumEntities, out var owner)) return false;
        RuntimeTileEntities = owner;
        return true;
    }

    /// <summary>Initializes the live ID namespace for a canonical world-file load.
    /// Decoded persisted IDs stay in the immutable file representation; source
    /// WorldFile.LoadTileEntities assigns live IDs in surviving file order.</summary>
    public bool TryAttachLoadedTileEntities(ReadOnlySpan<WorldTileEntity> source, int maximumEntities)
    {
        if (RuntimeTileEntities is not null || source.Length > maximumEntities) return false;
        var loaded = new WorldTileEntity[source.Length];
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] is null) return false;
            loaded[index] = source[index] with { PersistedId = index };
        }
        if (!RuntimeWorldFurnitureStore1458.TryImport(this, loaded, maximumEntities, out var owner)) return false;
        var changed = new HashSet<WorldSectionId>();
        for (int index = 0; index < source.Length; index++)
            if (source[index].PersistedId != index)
                changed.Add(TerrariaSectionGeometry.FromTile(Dimensions, source[index].X, source[index].Y));
        // Bootstrap frames can have been compressed from the decoded file before
        // this live initialization. Reindexing changes packet10 metadata even
        // though no physical tile changed; invalidate those exact sections.
        var sections = changed.ToArray();
        BeginMetadataMutation(sections);
        RuntimeTileEntities = owner;
        EndMetadataMutation(sections);
        return true;
    }

    // These methods are one concrete owner-thread joint-adoption boundary. No
    // callback runs while all participating section versions are odd. Tile and
    // metadata readers therefore either retain the old image or retry.
    internal void BeginMetadataMutation(ReadOnlySpan<WorldSectionId> sections)
    {
        foreach (var section in sections)
            Interlocked.Increment(ref _sectionVersions[TerrariaSectionGeometry.ToLinearIndex(Dimensions, section)]);
    }

    internal void EndMetadataMutation(ReadOnlySpan<WorldSectionId> sections)
    {
        foreach (var section in sections)
        {
            Interlocked.Increment(ref _sectionVersions[TerrariaSectionGeometry.ToLinearIndex(Dimensions, section)]);
            DirtySections.MarkDirty(section);
            PersistenceDirtySections.MarkDirty(section);
        }
    }

    internal void SetWithinMetadataMutation(int x, int y, in WorldTile tile)
    {
        int index = GetIndex(x, y);
        WorldTile before = _tiles[index];
        _tiles[index] = tile;
        InvalidateCobwebFrameNumber(index, in tile);
        if (FallingBlockUpdates is { } falling && (before.Flags != tile.Flags || before.Type != tile.Type || before.Shape != tile.Shape))
            for (int dy = -2; dy <= 1; dy++)
                if (y + dy >= 0 && y + dy < Dimensions.HeightTiles) falling.Wake(index + dy);
    }
}
