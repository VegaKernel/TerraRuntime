using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Clean-room ordinary-tree growth port pinned to TerrariaServer 1.4.5.8 <c>WorldGen.GrowTree</c>.
/// The caller owns placement density; this type owns the source growth gate, RNG ordering and tile frames.
/// </summary>
internal static class VanillaTreeGrower1458
{
    private const int MinimumTreeHeightTiles = 5;
    private const int MaximumTreeHeightTilesExclusive = 17;
    private const int OrdinaryTopPaddingTiles = 4;
    private const int JungleExtraTopPaddingTiles = 5;
    private const int SegmentVariantCount = 3;
    private const int SegmentFeatureCount = 10;
    private const int LeafyBranchRollExclusive = 3;
    private const int LeafyBranchSuccessCount = 2;
    private const int LeafyTopRollExclusive = 13;
    private const int RootShapeRollExclusive = 3;
    private const int CanopyClearanceRadiusTiles = 2;
    private const int TrunkClearanceRadiusTiles = 1;
    private const int CanopyClearanceBottomOffsetTiles = 3;
    private const int TrunkClearanceBottomOffsetTiles = 1;

    public static bool TryGrow(
        WorldTileStore store,
        int x,
        int checkedY,
        IWorldGenerationVanillaRandom random)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random);

        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        if (x < CanopyClearanceRadiusTiles || x >= width - CanopyClearanceRadiusTiles ||
            checkedY < TrunkClearanceRadiusTiles || checkedY >= height)
            return false;

        Span<WorldTile> tiles = store.Tiles;
        int stride = height;
        int groundY = checkedY;
        while (groundY < height &&
               tiles[Index(stride, x, groundY)].IsActive &&
               VanillaTreeGrowthCatalog1458.IsCommonSapling(tiles[Index(stride, x, groundY)].TileType))
            groundY++;

        if (groundY < TrunkClearanceRadiusTiles || groundY >= height ||
            tiles[Index(stride, x - 1, groundY - 1)].LiquidAmount != 0 ||
            tiles[Index(stride, x, groundY - 1)].LiquidAmount != 0 ||
            tiles[Index(stride, x + 1, groundY - 1)].LiquidAmount != 0)
        {
            return false;
        }

        ref WorldTile ground = ref tiles[Index(stride, x, groundY)];
        if (!IsFlatActiveTreeGround(in ground) ||
            !VanillaTreeGrowthCatalog1458.AllowsPlantGrowth(tiles[Index(stride, x, groundY - 1)].WallType) ||
            (!IsTreeGround(tiles[Index(stride, x - 1, groundY)]) &&
             !IsTreeGround(tiles[Index(stride, x + 1, groundY)])))
        {
            return false;
        }

        int treeHeight = random.Next(MinimumTreeHeightTiles, MaximumTreeHeightTilesExclusive);
        int topPadding = treeHeight + OrdinaryTopPaddingTiles +
            (ground.TileType == VanillaTileIds.JungleGrass ? JungleExtraTopPaddingTiles : 0);
        bool hasClearance = ground.TileType == VanillaTileIds.MushroomGrass &&
            IsReplaceableRectangle(
                tiles,
                width,
                height,
                x - CanopyClearanceRadiusTiles,
                x + CanopyClearanceRadiusTiles,
                groundY - topPadding,
                groundY - CanopyClearanceBottomOffsetTiles) &&
            IsReplaceableRectangle(
                tiles,
                width,
                height,
                x - TrunkClearanceRadiusTiles,
                x + TrunkClearanceRadiusTiles,
                groundY - CanopyClearanceRadiusTiles,
                groundY - TrunkClearanceBottomOffsetTiles);
        hasClearance |= IsReplaceableRectangle(
            tiles,
            width,
            height,
            x - CanopyClearanceRadiusTiles,
            x + CanopyClearanceRadiusTiles,
            groundY - topPadding,
            groundY - TrunkClearanceBottomOffsetTiles);
        if (!hasClearance)
            return false;

        byte color = ground.TileColor;
        WorldTileFlags coating = ground.Flags &
            (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        bool previousLeftBranch = false;
        bool previousRightBranch = false;

        for (int y = groundY - treeHeight; y < groundY; y++)
        {
            int variant = random.Next(SegmentVariantCount);
            var feature = (VanillaTreeSegmentFeature1458)random.Next(SegmentFeatureCount);
            if (y == groundY - 1 || y == groundY - treeHeight)
                feature = VanillaTreeSegmentFeature1458.Straight;

            while ((HasLeftBranch(feature) && previousLeftBranch) ||
                   (HasRightBranch(feature) && previousRightBranch))
            {
                feature = (VanillaTreeSegmentFeature1458)random.Next(SegmentFeatureCount);
            }

            previousLeftBranch = HasLeftBranch(feature);
            previousRightBranch = HasRightBranch(feature);

            ref WorldTile trunk = ref tiles[Index(stride, x, y)];
            SetTreeTile(ref trunk, color, coating, VanillaTreeFrameCatalog1458.Trunk(feature, variant));

            if (previousLeftBranch)
            {
                variant = random.Next(SegmentVariantCount);
                bool leafy = random.Next(LeafyBranchRollExclusive) < LeafyBranchSuccessCount;
                ref WorldTile branch = ref tiles[Index(stride, x - 1, y)];
                SetTreeTile(ref branch, color, coating, VanillaTreeFrameCatalog1458.LeftBranch(leafy, variant));
            }

            if (previousRightBranch)
            {
                variant = random.Next(SegmentVariantCount);
                bool leafy = random.Next(LeafyBranchRollExclusive) < LeafyBranchSuccessCount;
                ref WorldTile branch = ref tiles[Index(stride, x + 1, y)];
                SetTreeTile(ref branch, color, coating, VanillaTreeFrameCatalog1458.RightBranch(leafy, variant));
            }
        }

        var rootShape = (VanillaTreeRootShape1458)random.Next(RootShapeRollExclusive);
        bool hasLeftGround = IsFlatActiveTreeGround(in tiles[Index(stride, x - 1, groundY)]);
        bool hasRightGround = IsFlatActiveTreeGround(in tiles[Index(stride, x + 1, groundY)]);
        NormalizeRootShape(ref rootShape, hasLeftGround, hasRightGround);

        if (rootShape is VanillaTreeRootShape1458.Both or VanillaTreeRootShape1458.Right)
        {
            int variant = random.Next(SegmentVariantCount);
            ref WorldTile rightRoot = ref tiles[Index(stride, x + 1, groundY - 1)];
            SetTreeTile(ref rightRoot, color, coating, VanillaTreeFrameCatalog1458.RightRoot(variant));
        }

        if (rootShape is VanillaTreeRootShape1458.Both or VanillaTreeRootShape1458.Left)
        {
            int variant = random.Next(SegmentVariantCount);
            ref WorldTile leftRoot = ref tiles[Index(stride, x - 1, groundY - 1)];
            SetTreeTile(ref leftRoot, color, coating, VanillaTreeFrameCatalog1458.LeftRoot(variant));
        }

        int baseVariant = random.Next(SegmentVariantCount);
        ref WorldTile trunkBase = ref tiles[Index(stride, x, groundY - 1)];
        if (VanillaTreeFrameCatalog1458.TryGetTrunkBase(rootShape, baseVariant, out VanillaTreeFrame1458 baseFrame))
            SetTreeFrame(ref trunkBase, baseFrame);

        bool leafyTop = random.Next(LeafyTopRollExclusive) != 0;
        int topVariant = random.Next(SegmentVariantCount);
        ref WorldTile top = ref tiles[Index(stride, x, groundY - treeHeight)];
        SetTreeFrame(ref top, VanillaTreeFrameCatalog1458.Top(leafyTop, topVariant));
        return true;
    }

    private static bool HasLeftBranch(VanillaTreeSegmentFeature1458 feature) =>
        feature is VanillaTreeSegmentFeature1458.LeftBranch or VanillaTreeSegmentFeature1458.BothBranches;

    private static bool HasRightBranch(VanillaTreeSegmentFeature1458 feature) =>
        feature is VanillaTreeSegmentFeature1458.RightBranch or VanillaTreeSegmentFeature1458.BothBranches;

    private static void NormalizeRootShape(
        ref VanillaTreeRootShape1458 rootShape,
        bool hasLeftGround,
        bool hasRightGround)
    {
        if (!hasLeftGround)
        {
            if (rootShape == VanillaTreeRootShape1458.Both)
                rootShape = VanillaTreeRootShape1458.Left;
            else if (rootShape == VanillaTreeRootShape1458.Right)
                rootShape = VanillaTreeRootShape1458.None;
        }

        if (!hasRightGround)
        {
            if (rootShape == VanillaTreeRootShape1458.Both)
                rootShape = VanillaTreeRootShape1458.Right;
            else if (rootShape == VanillaTreeRootShape1458.Left)
                rootShape = VanillaTreeRootShape1458.None;
        }

        if (hasLeftGround && !hasRightGround)
            rootShape = VanillaTreeRootShape1458.Left;
        if (hasRightGround && !hasLeftGround)
            rootShape = VanillaTreeRootShape1458.Right;
    }

    private static bool IsReplaceableRectangle(
        ReadOnlySpan<WorldTile> tiles,
        int width,
        int height,
        int startX,
        int endX,
        int startY,
        int endY)
    {
        if (startX < 0 || endX >= width || startY < 0 || endY >= height)
            return false;

        for (int x = startX; x <= endX; x++)
            for (int y = startY; y <= endY; y++)
            {
                WorldTile tile = tiles[Index(height, x, y)];
                if (tile.IsActive && !VanillaTreeGrowthCatalog1458.IsReplaceableGrowthTile(tile.TileType))
                    return false;
            }

        return true;
    }

    private static bool IsFlatActiveTreeGround(in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && tile.Shape == 0 && IsTreeGround(tile);

    private static bool IsTreeGround(in WorldTile tile) =>
        tile.IsActive && VanillaTreeGrowthCatalog1458.IsTreeGround(tile.TileType);

    private static void SetTreeTile(
        ref WorldTile tile,
        byte color,
        WorldTileFlags coating,
        VanillaTreeFrame1458 frame)
    {
        tile.Flags = (tile.Flags & ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock)) |
            WorldTileFlags.Active |
            coating;
        tile.Type = checked((ushort)VanillaTileIds.Trees.Value);
        tile.TileColor = color;
        SetTreeFrame(ref tile, frame);
    }

    private static void SetTreeFrame(ref WorldTile tile, VanillaTreeFrame1458 frame)
    {
        tile.FrameX = checked((short)frame.X);
        tile.FrameY = checked((short)frame.Y);
    }

    private static int Index(int height, int x, int y) => (x * height) + y;
}
