using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal enum ToolTreeBiome1458 : byte
{
    None, Forest, Corrupt, Mushroom, Crimson, Jungle, Snow, Hallowed,
    Palm, PalmCorrupt, PalmCrimson, PalmHallowed, Ash
}

internal readonly record struct ToolTreeBottom1458(int X, int Y, ToolTreeBiome1458 Biome);

/// <summary>Source GetTreeBottom/GetTreeType geometry with a bounded detached read capture.</summary>
internal sealed class ToolTreeGeometry1458(WorldTileStore tiles)
{
    // Runtime work ceiling, not a vanilla tree-height rule. Imported pathological
    // air/trunk columns are refused before any clock, RNG, mark or world adoption.
    internal const int MaximumQueries = 4096;
    private const int MaximumReadSections = 64;
    private const ushort Palm = 323;
    private const ushort MushroomTrunk = 72;
    private const int TreeFrameCell = 22;
    private readonly (WorldSectionId Section, long Version)[] sections = new (WorldSectionId, long)[MaximumReadSections];
    private readonly ToolWorldTileOverlay1458? overlay;
    private int sectionCount;
    private int queries;

    internal ToolTreeGeometry1458(WorldTileStore tiles, ToolWorldTileOverlay1458 overlay) : this(tiles)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (!ReferenceEquals(tiles, overlay.Tiles))
            throw new ArgumentException("A tool tree view must belong to the captured world.", nameof(overlay));
        this.overlay = overlay;
    }

    public bool IsCurrent
    {
        get
        {
            if (overlay is not null && !overlay.IsCurrentOwned) return false;
            for (int index = 0; index < sectionCount; index++)
            {
                var captured = sections[index];
                if ((captured.Version & 1L) != 0 || tiles.GetSectionVersion(captured.Section) != captured.Version)
                    return false;
            }
            return true;
        }
    }

    public bool TryResolveBottom(int x, int y, out ToolTreeBottom1458 result)
    {
        result = default;
        if (!TryRead(x, y, out var tile)) return false;
        bool palm = tile.Type == Palm;
        if (!palm)
        {
            int column = tile.FrameX / TreeFrameCell;
            int row = tile.FrameY / TreeFrameCell;
            bool pointsRight = (column == 3 && row <= 2) || (column == 2 && row >= 6);
            bool pointsLeft = (column == 4 && row is >= 3 and <= 5) ||
                (column == 1 && row is >= 6 and <= 8) || (column == 3 && row >= 9);
            if (pointsRight) x++;
            else if (pointsLeft) x--;
            if (!TryRead(x, y, out tile)) return false;
        }
        while (y < tiles.Dimensions.HeightTiles - 1)
        {
            bool continues = !tile.IsActive;
            if (tile.IsActive)
            {
                if (palm) continues = tile.Type == Palm;
                else
                {
                    if (!VanillaToolTileCatalog1458.TryGet(tile.TileType, out var facts)) return false;
                    continues = facts.IsTreeTrunk || tile.Type == MushroomTrunk;
                }
            }
            if (!continues) break;
            y++;
            if (!TryRead(x, y, out tile)) return false;
        }
        if (!IsCurrent) return false;
        result = new(x, y, tile.IsActive ? ClassifyGround(tile.Type) : ToolTreeBiome1458.None);
        return true;
    }

    public bool TryResolvePalmDropGround(int x, int y, out ushort groundType)
    {
        groundType = 0;
        while (y < tiles.Dimensions.HeightTiles)
        {
            if (!TryRead(x, y, out var tile)) return false;
            if (tile.IsActive)
            {
                if (!VanillaToolTileCatalog1458.TryGet(tile.TileType, out var facts)) return false;
                if (facts.IsSolid)
                {
                    groundType = tile.Type;
                    return IsCurrent;
                }
            }
            y++;
        }
        return false;
    }

    internal bool TryRead(int x, int y, out WorldTile tile)
    {
        tile = default;
        if (queries == MaximumQueries) return false;
        queries++;
        if (overlay is not null) return overlay.TryReadSourceSafely(x, y, out tile);
        // Framing.GetTileSafely returns a detached empty tile outside world bounds.
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
            return true;
        var section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x, y);
        bool found = false;
        for (int index = 0; index < sectionCount; index++)
            if (sections[index].Section == section) { found = true; break; }
        if (!found)
        {
            if (sectionCount == MaximumReadSections) return false;
            sections[sectionCount++] = (section, tiles.GetSectionVersion(section));
        }
        tile = tiles.Get(x, y);
        return IsCurrent;
    }

    private static ToolTreeBiome1458 ClassifyGround(ushort type)
    {
        const ushort Grass = 2, GolfGrass = 477, CorruptGrass = 23, CorruptJungleGrass = 661;
        const ushort MushroomGrass = 70, CrimsonGrass = 199, CrimsonJungleGrass = 662;
        const ushort JungleGrass = 60, Snow = 147, HallowedGrass = 109, HallowedGolfGrass = 492;
        const ushort Sand = 53, Ebonsand = 112, Crimsand = 234, Pearlsand = 116, AshGrass = 633;
        return type switch
        {
            Grass or GolfGrass => ToolTreeBiome1458.Forest,
            CorruptGrass or CorruptJungleGrass => ToolTreeBiome1458.Corrupt,
            MushroomGrass => ToolTreeBiome1458.Mushroom,
            CrimsonGrass or CrimsonJungleGrass => ToolTreeBiome1458.Crimson,
            JungleGrass => ToolTreeBiome1458.Jungle,
            Snow => ToolTreeBiome1458.Snow,
            HallowedGrass or HallowedGolfGrass => ToolTreeBiome1458.Hallowed,
            Sand => ToolTreeBiome1458.Palm,
            Ebonsand => ToolTreeBiome1458.PalmCorrupt,
            Crimsand => ToolTreeBiome1458.PalmCrimson,
            Pearlsand => ToolTreeBiome1458.PalmHallowed,
            AshGrass => ToolTreeBiome1458.Ash,
            _ => ToolTreeBiome1458.None
        };
    }
}
