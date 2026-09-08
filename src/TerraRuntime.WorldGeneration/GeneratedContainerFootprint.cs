using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Runtime;

/// <summary>Generated chest metadata uses the existing source-backed container geometry, including 3x2 dressers.</summary>
internal static class GeneratedContainerFootprint
{
    internal static bool IsValid(WorldTileStore store, int left, int top)
    {
        if ((uint)left >= (uint)store.Dimensions.WidthTiles || (uint)top >= (uint)store.Dimensions.HeightTiles) return false;
        WorldTile anchor = store.Get(left, top);
        if (anchor.FrameX < 0 || anchor.FrameY < 0 ||
            !VanillaMultiTileObjectCatalog.TryGet(anchor.TileType, out VanillaMultiTileObjectDefinition definition) ||
            definition.MetadataKind != VanillaTileObjectMetadataKind.Chest || !definition.MatchesAnchor(anchor) ||
            left + definition.Width > store.Dimensions.WidthTiles || top + definition.Height > store.Dimensions.HeightTiles) return false;
        for (int dy = 0; dy < definition.Height; dy++)
        for (int dx = 0; dx < definition.Width; dx++)
        {
            WorldTile cell = store.Get(left + dx, top + dy);
            if (!cell.IsActive || cell.Type != anchor.Type ||
                cell.FrameX != anchor.FrameX + dx * 18 || cell.FrameY != anchor.FrameY + dy * 18) return false;
        }
        return true;
    }
}
