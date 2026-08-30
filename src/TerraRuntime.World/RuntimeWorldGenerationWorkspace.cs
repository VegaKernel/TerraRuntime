using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Isolated mutable tile workspace for a candidate generated world. Writes bypass live-world dirty tracking because
/// the store is not authoritative or network-visible until the caller explicitly accepts the completed candidate.
/// </summary>
public sealed class RuntimeWorldGenerationWorkspace : IWorldGenerationWorkspace, IWorldGenerationMetadataWorkspace
{
    private const WorldGenerationTileFlags KnownFlags =
        WorldGenerationTileFlags.Active |
        WorldGenerationTileFlags.WireRed |
        WorldGenerationTileFlags.WireBlue |
        WorldGenerationTileFlags.WireGreen |
        WorldGenerationTileFlags.WireYellow |
        WorldGenerationTileFlags.Actuator |
        WorldGenerationTileFlags.Inactive |
        WorldGenerationTileFlags.InvisibleBlock |
        WorldGenerationTileFlags.InvisibleWall |
        WorldGenerationTileFlags.FullbrightBlock |
        WorldGenerationTileFlags.FullbrightWall;

    private WorldGenerationPoint? spawn;
    private WorldGenerationPoint? dungeon;
    private WorldGenerationLayers? layers;
    private VanillaWorldSeedProfile1458 vanillaSeedProfile;

    public RuntimeWorldGenerationWorkspace(int widthTiles, int heightTiles)
    {
        Dimensions = new WorldDimensions(widthTiles, heightTiles);
        TileStore = new WorldTileStore(Dimensions);
    }

    public WorldDimensions Dimensions { get; }
    public WorldTileStore TileStore { get; }
    public int WidthTiles => Dimensions.WidthTiles;
    public int HeightTiles => Dimensions.HeightTiles;

    internal VanillaWorldSeedProfile1458 VanillaSeedProfile => vanillaSeedProfile;

    internal void SetVanillaSeedProfile(VanillaWorldSeedProfile1458 value) => vanillaSeedProfile = value;

    public bool TryGetTile(int x, int y, out WorldGenerationTile tile)
    {
        if (!Contains(x, y))
        {
            tile = default;
            return false;
        }

        WorldTile source = TileStore.Get(x, y);
        tile = new WorldGenerationTile(
            source.Type,
            source.Wall,
            source.FrameX,
            source.FrameY,
            (WorldGenerationTileFlags)source.Flags,
            source.LiquidAmount,
            source.TileColor,
            source.WallColor,
            source.Shape,
            (WorldGenerationLiquidKind)source.LiquidKind);
        return true;
    }

    public bool TrySetTile(int x, int y, in WorldGenerationTile tile)
    {
        if (!Contains(x, y) || !IsValid(tile))
            return false;

        var target = new WorldTile
        {
            Type = tile.Type,
            Wall = tile.Wall,
            FrameX = tile.FrameX,
            FrameY = tile.FrameY,
            Flags = (WorldTileFlags)tile.Flags,
            LiquidAmount = tile.LiquidAmount,
            TileColor = tile.TileColor,
            WallColor = tile.WallColor,
            Shape = tile.Shape,
            LiquidKind = (WorldLiquidKind)tile.LiquidKind
        };

        // Generation owns this unpublished store exclusively. Initial-population writes deliberately avoid
        // manufacturing network/persistence dirty work for a world that no consumer can observe yet.
        TileStore.SetInitialPopulationTile(x, y, target);
        return true;
    }

    public bool TryGetSpawn(out WorldGenerationPoint value)
    {
        if (spawn is not WorldGenerationPoint current)
        {
            value = default;
            return false;
        }

        value = current;
        return true;
    }

    public bool TrySetSpawn(int x, int y)
    {
        if (!Contains(x, y))
            return false;

        spawn = new WorldGenerationPoint(x, y);
        return true;
    }

    public bool TryGetDungeon(out WorldGenerationPoint value)
    {
        if (dungeon is not WorldGenerationPoint current)
        {
            value = default;
            return false;
        }

        value = current;
        return true;
    }

    public bool TrySetDungeon(int x, int y)
    {
        if (!Contains(x, y))
            return false;

        dungeon = new WorldGenerationPoint(x, y);
        return true;
    }

    public bool TryGetLayers(out WorldGenerationLayers value)
    {
        if (layers is not WorldGenerationLayers current)
        {
            value = default;
            return false;
        }

        value = current;
        return true;
    }

    public bool TrySetLayers(double worldSurface, double rockLayer)
    {
        if (!double.IsFinite(worldSurface) ||
            !double.IsFinite(rockLayer) ||
            worldSurface <= 0d ||
            worldSurface >= HeightTiles ||
            rockLayer <= worldSurface ||
            rockLayer >= HeightTiles)
        {
            return false;
        }

        layers = new WorldGenerationLayers(worldSurface, rockLayer);
        return true;
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)WidthTiles && (uint)y < (uint)HeightTiles;

    private static bool IsValid(in WorldGenerationTile tile)
    {
        if ((tile.Flags & ~KnownFlags) != 0 || tile.Shape > 5 || !Enum.IsDefined(tile.LiquidKind))
            return false;

        if (tile.Type >= VanillaWorldFormat326.TileTypeCount ||
            tile.Wall >= VanillaWorldFormat326.WallTypeCount)
        {
            return false;
        }

        return true;
    }
}
