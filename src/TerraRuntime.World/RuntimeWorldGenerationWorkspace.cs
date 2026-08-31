using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Isolated mutable tile workspace for a candidate generated world. Writes bypass live-world dirty tracking because
/// the store is not authoritative or network-visible until the caller explicitly accepts the completed candidate.
/// Generated object/NPC metadata is accumulated beside the tile store so fresh .wld composition can publish tiles
/// and their side tables atomically instead of manufacturing orphan frame-important objects or transient NPCs.
/// </summary>
public sealed class RuntimeWorldGenerationWorkspace :
    IWorldGenerationWorkspace,
    IWorldGenerationMetadataWorkspace,
    IWorldGenerationChestWorkspace
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
    private readonly List<WorldTownNpc> generatedTownNpcs = [];
    private WorldGenerationPoint? spawn;
    private WorldGenerationPoint? dungeon;
    private WorldGenerationLayers? layers;
    private VanillaWorldSeedProfile1458 vanillaSeedProfile;
    private VanillaWorldGenerationBootstrapState1458? vanillaBootstrapState;
    private VanillaTerrainGenerationState1458? vanillaTerrainState;

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
    public int GeneratedTownNpcCount => generatedTownNpcs.Count;

    internal VanillaWorldSeedProfile1458 VanillaSeedProfile => vanillaSeedProfile;
    internal VanillaWorldGenerationBootstrapState1458? VanillaBootstrapState => vanillaBootstrapState;
    internal VanillaTerrainGenerationState1458? VanillaTerrainState => vanillaTerrainState;

    internal void SetVanillaSeedProfile(VanillaWorldSeedProfile1458 value) => vanillaSeedProfile = value;
    internal void SetVanillaBootstrapState(VanillaWorldGenerationBootstrapState1458 value) =>
        vanillaBootstrapState = value ?? throw new ArgumentNullException(nameof(value));
    internal void SetVanillaTerrainState(VanillaTerrainGenerationState1458 value) =>
        vanillaTerrainState = value;

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

    /// <summary>
    /// Adapts the public generator chest capability to the runtime-owned generated-object side table. The tile anchor
    /// must already exist, so all generators share the same persistence validation and dense slot assignment.
    /// </summary>
    public bool TryAddChest(
        int x,
        int y,
        string name,
        ReadOnlySpan<WorldGenerationChestItem> items)
    {
        if (name is null ||
            name.Length > WorldGenerationChestRules.MaximumNameLength ||
            name.Any(char.IsControl) ||
            items.Length > WorldGenerationChestRules.VanillaItemSlotCount)
        {
            return false;
        }

        var persistedItems = new WorldChestItem[WorldGenerationChestRules.VanillaItemSlotCount];
        for (int index = 0; index < items.Length; index++)
        {
            WorldGenerationChestItem item = items[index];
            if (item.Stack < 0 || item.Stack > short.MaxValue || item.Prefix.Value > byte.MaxValue)
                return false;

            if (item.Stack == 0)
            {
                if (!item.ItemType.IsNone || item.Prefix.Value != 0)
                    return false;
                continue;
            }

            if (!VanillaItemIds.TryCreate(item.ItemType.Value, out ItemTypeId validated) || validated.IsNone)
                return false;

            persistedItems[index] = new WorldChestItem(
                item.Stack,
                item.ItemType.Value,
                checked((byte)item.Prefix.Value));
        }

        return TryAddGeneratedChest(x, y, name, persistedItems);
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

    /// <summary>
    /// Registers one generated town NPC in the candidate-world side table. Coordinates are persisted in Terraria's
    /// pixel coordinate space while home coordinates remain tile coordinates. Duplicate net identities are rejected
    /// for generation-owned starting NPCs so retries cannot silently create duplicate Guide-like records.
    /// </summary>
    internal bool TryAddGeneratedTownNpc(
        int netId,
        string givenName,
        float x,
        float y,
        bool homeless,
        int homeTileX,
        int homeTileY,
        int? townNpcVariationIndex = null,
        bool homelessDespawn = false)
    {
        ArgumentNullException.ThrowIfNull(givenName);
        if (netId == 0 ||
            !float.IsFinite(x) ||
            !float.IsFinite(y) ||
            x < 0f || y < 0f ||
            x >= WidthTiles * 16f ||
            y >= HeightTiles * 16f ||
            (uint)homeTileX >= (uint)WidthTiles ||
            (uint)homeTileY >= (uint)HeightTiles ||
            townNpcVariationIndex is < 0)
        {
            return false;
        }

        foreach (WorldTownNpc npc in generatedTownNpcs)
        {
            if (npc.NetId == netId)
                return false;
        }

        generatedTownNpcs.Add(new WorldTownNpc(
            netId,
            givenName,
            x,
            y,
            homeless,
            homeTileX,
            homeTileY,
            townNpcVariationIndex,
            homelessDespawn));
        return true;
    }

    /// <summary>Returns a detached NPC persistence snapshot suitable for fresh-world composition.</summary>
    public WorldNpcPersistence CaptureGeneratedNpcs()
    {
        WorldTownNpc[] townNpcs = generatedTownNpcs.ToArray();
        return new WorldNpcPersistence([], townNpcs, []);
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
