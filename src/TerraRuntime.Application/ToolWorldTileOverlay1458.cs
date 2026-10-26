using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// One bounded detached tool transaction's terrain image. Reads always retain the
/// concrete before-world section version even when a prior source step staged an
/// afterimage. This is not a live world owner, queue or persistent history.
/// </summary>
internal sealed class ToolWorldTileOverlay1458(WorldTileStore tiles)
{
    internal const int MaximumQueries = 4096;
    internal const int MaximumChangedCells = 4096;
    private const int MaximumSections = 64;
    private readonly Dictionary<int, WorldTile> staged = [];
    private readonly (WorldSectionId Section, long Version)[] sections = new (WorldSectionId, long)[MaximumSections];
    private readonly long[] acceptedVersions = new long[MaximumSections];
    private int sectionCount;
    private int queries;
    private bool failed;
    private bool adopted;

    internal WorldTileStore Tiles => tiles;
    public int QueryCount => queries;
    public int ChangedCellCount => staged.Count;
    public bool IsCurrentOwned
    {
        get
        {
            if (failed || adopted) return false;
            for (int index = 0; index < sectionCount; index++)
            {
                var captured = sections[index];
                if ((captured.Version & 1L) != 0 || tiles.GetSectionVersion(captured.Section) != captured.Version)
                    return false;
            }
            return true;
        }
    }

    public bool IsAcceptedCurrent
    {
        get
        {
            if (!adopted || failed) return false;
            for (int index = 0; index < sectionCount; index++)
                if (tiles.GetSectionVersion(sections[index].Section) != acceptedVersions[index]) return false;
            return true;
        }
    }

    internal bool HasSameAfterimage(ToolWorldTileOverlay1458 other)
    {
        if (!ReferenceEquals(tiles, other.Tiles) || failed || adopted || other.failed || other.adopted ||
            staged.Count != other.staged.Count) return false;
        foreach (var cell in staged)
            if (!other.staged.TryGetValue(cell.Key, out var value) || !cell.Value.Equals(value)) return false;
        return true;
    }

    public bool TryCaptureMutationBefore(int x, int y,
        Span<(int X, int Y, WorldTile Tile)> cells, out int count)
    {
        count = 0;
        if (cells.Length < 9 || !Contains(x, y)) return Fail();
        for (int cellX = Math.Max(0, x - 1); cellX <= Math.Min(tiles.Dimensions.WidthTiles - 1, x + 1); cellX++)
            for (int cellY = Math.Max(0, y - 1); cellY <= Math.Min(tiles.Dimensions.HeightTiles - 1, y + 1); cellY++)
            {
                if (!TryRead(cellX, cellY, out var tile)) return false;
                cells[count++] = (cellX, cellY, tile);
            }
        return true;
    }

    public bool TryStageChanges(ReadOnlySpan<(int X, int Y, WorldTile After)> changes)
    {
        foreach (var cell in changes)
            if (!TryStage(cell.X, cell.Y, cell.After)) return false;
        return IsCurrentOwned;
    }

    public bool TryRead(int x, int y, out WorldTile tile)
    {
        tile = default;
        if (!RecordRead(x, y)) return false;
        int index = GetIndex(x, y);
        tile = staged.TryGetValue(index, out var after) ? after : tiles.Get(x, y);
        return IsCurrentOwned;
    }

    /// <summary>
    /// Pure virtual lookup. A caller with its own before-world witness records the
    /// concrete read there first, then uses this override instead of reading stale
    /// terrain. Scattered HitTile.Prune reads do not expand the cascade's read cap.
    /// </summary>
    public bool TryGetStagedAfterimage(int x, int y, out WorldTile tile)
    {
        tile = default;
        return !failed && !adopted && Contains(x, y) && staged.TryGetValue(GetIndex(x, y), out tile);
    }

    public bool TryReadBefore(int x, int y, out WorldTile tile)
    {
        tile = default;
        if (!RecordRead(x, y)) return false;
        tile = tiles.Get(x, y);
        return IsCurrentOwned;
    }

    public bool RecordRead(int x, int y)
    {
        if (failed || adopted || queries == MaximumQueries || !Contains(x, y)) return Fail();
        queries++;
        var section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x, y);
        bool found = false;
        for (int index = 0; index < sectionCount; index++)
            if (sections[index].Section == section) { found = true; break; }
        if (!found)
        {
            if (sectionCount == MaximumSections) return Fail();
            sections[sectionCount++] = (section, tiles.GetSectionVersion(section));
        }
        return IsCurrentOwned || Fail();
    }

    public bool TryStage(int x, int y, in WorldTile after)
    {
        if (!RecordRead(x, y)) return false;
        int index = GetIndex(x, y);
        if (!staged.ContainsKey(index) && staged.Count == MaximumChangedCells) return Fail();
        staged[index] = after;
        return true;
    }

    /// <summary>For genuinely source-safe reads such as Framing.GetTileSafely, not for packet bounds admission.</summary>
    public bool TryReadSourceSafely(int x, int y, out WorldTile tile)
    {
        tile = default;
        if (Contains(x, y)) return TryRead(x, y, out tile);
        if (failed || adopted || queries == MaximumQueries) return Fail();
        queries++;
        return IsCurrentOwned;
    }

    public bool TryAdoptUnpublished()
    {
        if (!IsCurrentOwned) return false;
        adopted = true;
        foreach (var cell in staged)
        {
            int x = cell.Key / tiles.Dimensions.HeightTiles;
            int y = cell.Key % tiles.Dimensions.HeightTiles;
            var after = cell.Value;
            tiles.Set(x, y, in after);
        }
        for (int index = 0; index < sectionCount; index++)
            acceptedVersions[index] = tiles.GetSectionVersion(sections[index].Section);
        return true;
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)tiles.Dimensions.WidthTiles && (uint)y < (uint)tiles.Dimensions.HeightTiles;
    private int GetIndex(int x, int y) => checked(x * tiles.Dimensions.HeightTiles + y);
    private bool Fail() { failed = true; return false; }
}
