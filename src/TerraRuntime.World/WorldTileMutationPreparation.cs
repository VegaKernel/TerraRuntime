namespace TerraRuntime.World;

/// <summary>
/// One detached simple-cell mutation and its existing bounded framing writes.
/// It is single-use, single-writer and deliberately contains no observer callbacks.
/// Wider tree/object plans must capture their own complete source dependency region.
/// </summary>
public sealed class WorldTileMutationPreparation
{
    private readonly WorldTileStore tiles;
    private readonly int x;
    private readonly int y;
    private readonly (WorldSectionId Section, long Version)[] sections;
    private readonly (int X, int Y, WorldTile After)[] writes = new (int, int, WorldTile)[9];
    private readonly (int X, int Y, WorldTile Tile)[] virtualBefore;
    private int writeCount;
    private bool tileFrame;
    private bool wallFrame;
    private bool committed;
    private WorldTileMutationResult result;

    internal WorldTileMutationPreparation(WorldTileStore tiles, int x, int y,
        ReadOnlySpan<(int X, int Y, WorldTile Tile)> virtualBefore, bool sourceFraming = false)
    {
        SourceFraming = sourceFraming;
        this.virtualBefore = virtualBefore.ToArray();
        this.tiles = tiles;
        this.x = x;
        this.y = y;
        var first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, Math.Max(0, x - 2), Math.Max(0, y - 2));
        var last = TerrariaSectionGeometry.FromTile(tiles.Dimensions,
            Math.Min(tiles.Dimensions.WidthTiles - 1, x + 2),
            Math.Min(tiles.Dimensions.HeightTiles - 1, y + 2));
        // Guard the surrounding source framing read stencil as well as staged writes.
        sections = new (WorldSectionId, long)[(last.X - first.X + 1) * (last.Y - first.Y + 1)];
        int index = 0;
        for (int sectionX = first.X; sectionX <= last.X; sectionX++)
            for (int sectionY = first.Y; sectionY <= last.Y; sectionY++)
            {
                var section = new WorldSectionId(sectionX, sectionY);
                sections[index++] = (section, tiles.GetSectionVersion(section));
            }
    }

    internal bool SourceFraming { get; }

    public ReadOnlySpan<(int X, int Y, WorldTile After)> Changes => writes.AsSpan(0, writeCount);

    internal WorldTile ReadBefore(int cellX, int cellY)
    {
        foreach (var cell in virtualBefore)
            if (cell.X == cellX && cell.Y == cellY) return cell.Tile;
        return tiles.Get(cellX, cellY);
    }

    public bool IsCurrent
    {
        get
        {
            if (committed) return false;
            foreach (var section in sections)
                if ((section.Version & 1L) != 0 || tiles.GetSectionVersion(section.Section) != section.Version)
                    return false;
            return true;
        }
    }

    internal void Stage(int cellX, int cellY, in WorldTile after)
    {
        if (writeCount >= writes.Length)
            throw new InvalidOperationException("Simple tile framing exceeded its nine-cell bound.");
        writes[writeCount++] = (cellX, cellY, after);
    }

    internal void SetFraming(bool tileFrame, bool wallFrame)
    {
        this.tileFrame = tileFrame;
        this.wallFrame = wallFrame;
    }

    internal void SetResult(in WorldTileMutationResult result) => this.result = result;

    public bool TryAdoptUnpublished(out WorldTileMutationResult committedResult)
    {
        committedResult = default;
        if (!result.Applied || !IsCurrent) return false;
        committed = true;
        for (int index = 0; index < writeCount; index++)
        {
            var write = writes[index];
            tiles.Set(write.X, write.Y, in write.After);
        }
        if (tileFrame)
            VanillaWorldLiquidWakeup1458.WakeCellAndNeighbours(tiles, x, y);
        if (wallFrame)
        {
            var first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, result.FrameMinX, result.FrameMinY);
            var last = TerrariaSectionGeometry.FromTile(tiles.Dimensions, result.FrameMaxX, result.FrameMaxY);
            for (int sectionX = first.X; sectionX <= last.X; sectionX++)
                for (int sectionY = first.Y; sectionY <= last.Y; sectionY++)
                {
                    var section = new WorldSectionId(sectionX, sectionY);
                    tiles.DirtySections.MarkDirty(section);
                    tiles.PersistenceDirtySections.MarkDirty(section);
                }
        }
        committedResult = result;
        return true;
    }
}
