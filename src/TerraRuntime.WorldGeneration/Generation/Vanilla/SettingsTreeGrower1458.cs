using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// One TerrariaServer 1.4.5.8 <c>WorldGen.GrowTreeSettings</c> profile. Only the fields the generation-time
/// grower actually reads are modelled; item-driven planting stays outside this slice.
/// </summary>
internal sealed record SettingsTreeProfile1458(
    ushort TreeTileType,
    ushort SaplingTileType,
    int HeightMinimum,
    int HeightMaximumInclusive,
    int TopPadding,
    Func<TileTypeId, bool> IsGround,
    Func<WallTypeId, bool> AllowsWall);

/// <summary>
/// Ordinary generation-time <c>WorldGen.GrowTreeWithSettings</c>, TerrariaServer 1.4.5.8. This shares the verified
/// trunk/branch/root frame atlas with the ordinary grower but keeps the settings variant's own clearance, root and
/// finishing algorithm, which differ from <c>WorldGen.GrowTree</c>.
/// </summary>
internal static class SettingsTreeGrower1458
{
    // TileID.Sets.Conversion.Stone and .Moss, which together are WorldGen.GemTreeGroundTest.
    private static ReadOnlySpan<ushort> StoneConversion => [1, 25, 117, 203];

    private static ReadOnlySpan<ushort> MossConversion =>
    [182, 180, 179, 381, 183, 181, 534, 536, 539, 625, 627];

    // WorldGen.GemTreeWallTest adds these to the shared plant-growth wall set.
    private static ReadOnlySpan<ushort> GemTreeExtraWalls =>
    [2, 54, 55, 56, 57, 58, 59, 61, 185, 196, 197, 198, 199, 208, 209, 210, 211, 212, 213, 214, 215];

    /// <summary>Gem-tree trunk identities in <c>WorldGen.TryGrowingTreeByType</c> order: 583..589.</summary>
    internal static SettingsTreeProfile1458 GemTree(ushort treeTileType)
    {
        if (treeTileType is < 583 or > 589)
            throw new ArgumentOutOfRangeException(nameof(treeTileType));
        return new SettingsTreeProfile1458(
            treeTileType,
            SaplingTileType: 590,
            HeightMinimum: 7,
            HeightMaximumInclusive: 12,
            TopPadding: 4,
            IsGround: IsGemTreeGround,
            AllowsWall: AllowsGemTreeWall);
    }

    internal static bool IsGemTreeGround(TileTypeId type) =>
        Contains(StoneConversion, type) || Contains(MossConversion, type);

    private static bool AllowsGemTreeWall(WallTypeId wall) =>
        TreeGrowthCatalog1458.AllowsPlantGrowth(wall) ||
        GemTreeExtraWalls.Contains(checked((ushort)wall.Value));

    private static bool Contains(ReadOnlySpan<ushort> values, TileTypeId type) =>
        type.Value >= 0 && type.Value < VanillaTileIds.Count && values.Contains(checked((ushort)type.Value));

    public static bool TryGrow(
        WorldTileStore store,
        SettingsTreeProfile1458 profile,
        int x,
        int checkedY,
        IWorldGenerationVanillaRandom random)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(random);

        int height = store.Dimensions.HeightTiles;
        if (x < 2 || x >= store.Dimensions.WidthTiles - 2 || checkedY < 1 || checkedY >= height) return false;
        int floor = checkedY;
        while (floor < height && At(store, x, checkedY).IsActive && At(store, x, floor).Type == profile.SaplingTileType)
            floor++;
        if (floor >= height) return false;
        for (int dx = -1; dx <= 1; dx++) if (At(store, x + dx, floor - 1).LiquidAmount != 0) return false;
        WorldTile ground = At(store, x, floor);
        if (!ground.IsActive || ground.IsActuated || ground.Shape != 0 || !profile.IsGround(ground.TileType) ||
            !profile.AllowsWall(At(store, x, floor - 1).WallType) ||
            (!IsProfileGround(profile, At(store, x - 1, floor)) &&
             !IsProfileGround(profile, At(store, x + 1, floor)))) return false;
        int treeHeight = random.Next(profile.HeightMinimum, profile.HeightMaximumInclusive + 1);
        int top = floor - treeHeight;
        if (top - profile.TopPadding < 0) return false;
        for (int column = x - 2; column <= x + 2; column++)
        for (int y = top - profile.TopPadding; y < floor; y++)
            if (At(store, column, y) is { IsActive: true } tile &&
                !TreeGrowthCatalog1458.IsReplaceableGrowthTile(tile.TileType)) return false;

        bool previousLeft = false, previousRight = false;
        for (int y = top; y < floor; y++)
        {
            int variant = random.Next(3);
            var feature = (TreeSegmentFeature1458)random.Next(10);
            if (y == top || y == floor - 1) feature = TreeSegmentFeature1458.Straight;
            int retries = 0;
            while ((HasLeft(feature) && previousLeft) || (HasRight(feature) && previousRight))
            {
                if (++retries > 100_000) throw new InvalidOperationException("Settings-tree branch search exhausted its safety budget.");
                feature = (TreeSegmentFeature1458)random.Next(10);
            }
            previousLeft = HasLeft(feature); previousRight = HasRight(feature);
            Write(ref At(store, x, y), ground, profile.TreeTileType, TreeFrameCatalog1458.Trunk(feature, variant));
            if (previousLeft)
            {
                variant = random.Next(3);
                Write(ref At(store, x - 1, y), ground, profile.TreeTileType,
                    TreeFrameCatalog1458.LeftBranch(random.Next(3) < 2, variant));
            }
            if (previousRight)
            {
                variant = random.Next(3);
                Write(ref At(store, x + 1, y), ground, profile.TreeTileType,
                    TreeFrameCatalog1458.RightBranch(random.Next(3) < 2, variant));
            }
        }
        // Settings trees test the generic IsTileTypeFitForTree at roots, not just their profile's ground type.
        // Both independent Next(3) draws occur even when a side has no eligible ground.
        bool left = IsRootGround(At(store, x - 1, floor));
        bool right = IsRootGround(At(store, x + 1, floor));
        if (random.Next(3) == 0) left = false;
        if (random.Next(3) == 0) right = false;
        if (right) Write(ref At(store, x + 1, floor - 1), ground, profile.TreeTileType, TreeFrameCatalog1458.RightRoot(random.Next(3)));
        if (left) Write(ref At(store, x - 1, floor - 1), ground, profile.TreeTileType, TreeFrameCatalog1458.LeftRoot(random.Next(3)));
        int baseVariant = random.Next(3);
        if (left || right)
            Write(ref At(store, x, floor - 1), ground, profile.TreeTileType,
                TreeFrameCatalog1458.SettingsTrunkBase(left, right, baseVariant));
        bool leafy = random.Next(13) != 0;
        Write(ref At(store, x, top), ground, profile.TreeTileType, TreeFrameCatalog1458.Top(leafy, random.Next(3)));
        FrameFreshRoots(store, profile, x, floor, random);
        return true;
    }

    private static void FrameFreshRoots(
        WorldTileStore store,
        SettingsTreeProfile1458 profile,
        int x,
        int floor,
        IWorldGenerationVanillaRandom random)
    {
        // GrowTreeWithSettings finishes with RangeFrame -> CheckTreeWithSettings. Generic
        // root eligibility above is NOT the profile's own ground test. This bounded
        // slice frames the newly grown roots only, not arbitrary pre-existing tree destruction.
        int row = floor - 1;
        for (int side = -1; side <= 1; side += 2)
        {
            int column = x + side;
            ref WorldTile root = ref At(store, column, row);
            if (!CanFrame(store, column, row) || !root.IsActive || root.Type != profile.TreeTileType ||
                root.FrameX is not (22 or 44) || root.FrameY is < 132 or > 176 ||
                IsProfileGround(profile, At(store, column, floor))) continue;
            // KillTile still evaluates ten dust choices before generation suppresses particles.
            for (int dust = 0; dust < 10; dust++) { _ = random.Next(10); _ = random.Next(12); }
            bool hasTreeAbove = At(store, column, row - 1).IsActive &&
                At(store, column, row - 1).Type == profile.TreeTileType;
            root.Type = 0; root.FrameX = root.FrameY = -1; root.Shape = root.TileColor = 0;
            root.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive |
                WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            // Generation KillTile's neighbouring inactive-cell TileFrame cleanup.
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                ref WorldTile neighbour = ref At(store, column + dx, row + dy);
                if (neighbour.IsActive) continue;
                neighbour.Shape = neighbour.TileColor = 0;
                neighbour.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            }
            // CheckTreeWithSettings continues after KillTile using captured neighbour types;
            // its final crown branch writes these frames even though the root is now inactive.
            if (!hasTreeAbove)
            {
                root.FrameX = 154;
                root.FrameY = (short)(side < 0 ? 65 : -1);
            }
        }
        ref WorldTile basis = ref At(store, x, row);
        if (!CanFrame(store, x, row) || basis.FrameY is < 132 or > 176 ||
            basis.FrameX is not (0 or 66 or 88)) return;
        bool left = At(store, x - 1, row).IsActive && At(store, x - 1, row).Type == profile.TreeTileType;
        bool right = At(store, x + 1, row).IsActive && At(store, x + 1, row).Type == profile.TreeTileType;
        basis.FrameX = (short)(left ? right ? 88 : 66 : 0);
        if (!left && !right) basis.FrameY %= 66;
    }

    private static bool CanFrame(WorldTileStore store, int x, int y) => x > 5 && y > 5 &&
        x < store.Dimensions.WidthTiles - 5 && y < store.Dimensions.HeightTiles - 5;

    private static bool IsProfileGround(SettingsTreeProfile1458 profile, in WorldTile tile) =>
        tile.IsActive && profile.IsGround(tile.TileType);

    private static bool IsRootGround(in WorldTile tile) => tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
        TreeGrowthCatalog1458.IsTreeGround(tile.TileType);

    private static bool HasLeft(TreeSegmentFeature1458 feature) =>
        feature is TreeSegmentFeature1458.LeftBranch or TreeSegmentFeature1458.BothBranches;

    private static bool HasRight(TreeSegmentFeature1458 feature) =>
        feature is TreeSegmentFeature1458.RightBranch or TreeSegmentFeature1458.BothBranches;

    private static void Write(ref WorldTile tile, in WorldTile ground, ushort treeTileType, TreeFrame1458 frame)
    {
        const WorldTileFlags coating = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
        tile.Flags = (tile.Flags & ~coating) | WorldTileFlags.Active | (ground.Flags & coating);
        tile.Type = treeTileType; tile.TileColor = ground.TileColor; tile.FrameX = frame.X; tile.FrameY = frame.Y;
    }

    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
