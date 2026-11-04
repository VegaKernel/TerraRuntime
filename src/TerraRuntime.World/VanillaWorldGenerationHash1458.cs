using System.Numerics;

namespace TerraRuntime.World;

/// <summary>Terraria 1.4.5.8 WorldGenerator.HashWorld over TileSnapshot's normalized twelve-byte cells.</summary>
public static class VanillaWorldGenerationHash1458
{
    public static uint Calculate(WorldTileStore tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        uint result = 0;
        for (int x = 0; x < tiles.Dimensions.WidthTiles; x++)
        {
            uint column = 0;
            for (int y = 0; y < tiles.Dimensions.HeightTiles; y++)
                column = Mix(column ^ TileHash(tiles.Get(x, y)));
            result = Mix(result ^ column);
        }
        return result;
    }

    private static uint Mix(uint value) => unchecked(BitOperations.RotateLeft(value, 13) * 5 + 0xE6546B64u);

    internal static uint TileHash(in WorldTile tile)
    {
        uint tileType = tile.IsActive ? tile.Type : 0u;
        uint wall = tile.Wall;
        uint header = 0;
        if ((tile.Flags & WorldTileFlags.Inactive) != 0) header |= 0x40;
        if ((tile.Flags & WorldTileFlags.WireRed) != 0) header |= 0x80;
        if ((tile.Flags & WorldTileFlags.WireBlue) != 0) header |= 0x100;
        if ((tile.Flags & WorldTileFlags.WireGreen) != 0) header |= 0x200;
        if ((tile.Flags & WorldTileFlags.Actuator) != 0) header |= 0x800;
        if ((tile.Flags & WorldTileFlags.FullbrightWall) != 0) header |= 0x8000;
        uint frameX = 0, frameY = 0;
        if (tile.IsActive)
        {
            if (tile.Type >= VanillaWorldFrameImportance326.Count)
                throw new InvalidDataException("Generation hash requires a known vanilla tile identity.");
            header |= 0x20u | tile.TileColor;
            // TileID.Sets.SaveSlopes is fixed during content initialization, not the mutable generation phase.
            if (VanillaTileCollisionCatalog.IsSolid(tile.TileType) || tile.Type is 131 or 336 or 340 or 341 or 342 or 343 or 344 or 351)
            {
                if (tile.IsHalfBrick) header |= 0x400;
                header |= (uint)tile.SourceSlope << 12;
            }
            if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
            {
                frameX = unchecked((ushort)tile.FrameX);
                frameY = unchecked((ushort)tile.FrameY);
            }
        }
        // TileSnapshot retains coatings even when the underlying block or wall is absent.
        if ((tile.Flags & WorldTileFlags.InvisibleBlock) != 0) wall |= 0x2000;
        if ((tile.Flags & WorldTileFlags.InvisibleWall) != 0) wall |= 0x4000;
        if ((tile.Flags & WorldTileFlags.FullbrightBlock) != 0) wall |= 0x8000;
        uint extra = tile.Wall != 0 ? tile.WallColor : 0u;
        if (tile.LiquidAmount != 0) extra |= (uint)tile.LiquidKind << 5;
        if ((tile.Flags & WorldTileFlags.WireYellow) != 0) extra |= 0x80;
        return (tileType | wall << 16) ^ (header | frameX << 16) ^
            (frameY | (uint)tile.LiquidAmount << 16 | extra << 24);
    }
}
