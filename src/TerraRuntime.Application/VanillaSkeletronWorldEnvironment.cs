using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Source AI_011 summon search; unlike a grounded spawn, an empty bottom boundary is accepted.</summary>
internal sealed class VanillaSkeletronWorldEnvironment(WorldTileStore tiles) : IVanillaSkeletronEnvironment
{
    public bool TryFindCasterSpawn(float centerX, float centerY, IVanillaNpcRandom random, out int bottomX, out int bottomY)
    {
        bottomX = bottomY = 0;
        float cellX = centerX / 16f, cellY = centerY / 16f;
        // Reject unrepresentable host coordinates before converting or adding the random offset.
        if (!float.IsFinite(cellX) || !float.IsFinite(cellY) || cellX < int.MinValue + 128d ||
            cellX > int.MaxValue - 128d || cellY < int.MinValue + 128d || cellY > int.MaxValue - 128d) return false;
        int ownX = (int)cellX, ownY = (int)cellY;
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            int x = ownX + random.NextInt32(-50, 51), y = ownY + random.NextInt32(-50, 51);
            if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles) continue;
            while (y < tiles.Dimensions.HeightTiles - 10 && !SolidTile(x, y)) y++;
            y--;
            if (SolidTile(x, y)) continue;
            bottomX = x * 16 + 8;
            bottomY = y * 16;
            return true;
        }
        return false;
    }

    private bool SolidTile(int x, int y)
    {
        // WorldGen.SolidTile returns false outside the world, including y=-1 after the source decrement.
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles) return false;
        var tile = tiles.Get(x, y);
        return tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
            VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }
}
