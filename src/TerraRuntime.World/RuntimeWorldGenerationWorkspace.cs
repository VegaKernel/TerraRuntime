using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Isolated mutable tile workspace for a candidate generated world. Writes bypass live-world dirty tracking because
/// the store is not authoritative or network-visible until the caller explicitly accepts the completed candidate.
/// Generated object metadata is accumulated beside the tile store so fresh .wld composition can publish tiles and
/// their side tables atomically instead of manufacturing orphan frame-important objects.
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

    private const int VanillaChestItemSlots = 40;
    private readonly List<WorldChest> generatedChests = [];
    private WorldGenerationPoint? spawn;
    private WorldGenerationPoint? dungeon;
    private WorldGenerationLayers? layers;
    private VanillaWorldSeedProfile1458 vanillaSeedProfile;
    private VanillaWorldGenerationBootstrapState1458? vanillaBootstrapState;

    public RuntimeWorldGenerationWorkspace(int widthTiles, int heightTiles)
    {
        Dimensions = new WorldDimensions(widthTiles, heightTiles);
        TileStore = new WorldTileStore(Dimensions);
    }

    public WorldDimensions Dimensions { get; }
    public WorldTileStore TileStore { get; }
    public int WidthTiles => Dimensions.WidthTiles;
    public int HeightTiles => Dimensions.HeightTiles;
    public int GeneratedChestCount => generatedChests.Count;

    internal VanillaWorldSeedProfile1458 VanillaSeedProfile => vanillaSeedProfile;
    internal VanillaWorldGenerationBootstrapState1458? VanillaBootstrapState => vanillaBootstrapState;

    internal void SetVanillaSeedProfile(VanillaWorldSeedProfile1458 value) => vanillaSeedProfile = value;
    internal void SetVanillaBootstrapState(VanillaWorldGenerationBootstrapState1458 value) =>
        vanillaBootstrapState = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>
    /// Registers one generated chest after its 2x2 tile object has been written. Slot identity is assigned densely in
    /// generation order because Terraria persists no chest slot id; file order becomes runtime/network slot identity.
    /// </summary>
    internal bool TryAddGeneratedChest(int x, int y, string name, ReadOnlySpan<WorldChestItem> items)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (generatedChests.Count >= VanillaWorldFormat326.MaximumChestSlots ||
            items.Length > VanillaChestItemSlots ||
            (uint)x >= (uint)(WidthTiles - 1) ||
            (uint)y >= (uint)(HeightTiles - 1))
        {
            return false;
        }

        WorldTile anchor = TileStore.Get(x, y);
        if (!VanillaTileObjectAnchorCatalog.MatchesChestAnchor(in anchor))
            return false;

        foreach (WorldChest chest in generatedChests)
        {
            if (chest.X == x && chest.Y == y)
                return false;
        }

        WorldChestItem[] detachedItems = items.ToArray();
        foreach (WorldChestItem item in detachedItems)
        {
            if (item.Stack < 0 || item.Stack > short.MaxValue ||
                (!item.IsEmpty && !item.HasValidItemType) ||
                (item.IsEmpty && (item.ItemType != 0 || item.Prefix != 0)))
            {
                return false;
            }
        }

        generatedChests.Add(new WorldChest(
            checked((short)generatedChests.Count),
            x,
            y,
            name,
            detachedItems));
        return true;
    }

    /// <summary>Returns a detached dense chest snapshot suitable for persistence.</summary>
    public WorldChest[] CaptureGeneratedChests()
    {
        var snapshot = new WorldChest[generatedChests.Count];
        for (int i = 0; i < generatedChests.Count; i++)
        {
            WorldChest source = generatedChests[i];
            snapshot[i] = source with { Items = source.Items.ToArray() };
        }
        return snapshot;
    }

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
