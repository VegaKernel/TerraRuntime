using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Shared generation-time placement and framing for dungeon platform and furniture passes.</summary>
internal sealed class DungeonObjectPlacement1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random)
{
    internal void SetPlatform(int x, int y, int style)
    {
        ref WorldTile cell = ref At(x, y);
        cell.Flags |= WorldTileFlags.Active; cell.Type = 19; cell.Shape = 0; cell.FrameY = (short)(style * 18);
        DungeonWindows1458.FrameFlatPlatform(tiles, x, y);
    }

    internal void Pot(int x, int y)
    {
        int style = random.Next(10, 13);
        bool valid = true;
        for (int tx = x; tx <= x + 1; tx++)
        {
            valid &= !At(tx, y - 1).IsActive && !At(tx, y).IsActive;
            WorldTile floor = At(tx, y + 1);
            valid &= floor.IsActive && !floor.IsActuated && floor.Shape == 0 && DungeonGenerationTiles1458.IsSolidType(floor.TileType);
        }
        if (valid)
        {
            int frame = random.Next(3) * 36;
            for (int dx = 0; dx <= 1; dx++)
            for (int dy = -1; dy <= 0; dy++)
            {
                ref WorldTile cell = ref At(x + dx, y + dy);
                cell.Flags |= WorldTileFlags.Active; cell.Type = 28;
                cell.FrameX = (short)(frame + dx * 18); cell.FrameY = (short)(style * 36 + (dy + 1) * 18);
                if (cell.Shape == 1) cell.Shape = 0;
            }
        }
        FrameSquare(x, y);
    }

    internal void TableObject(int x, int y, ushort type, int style = 0)
    {
        if (type is not (13 or 33 or 49 or 50)) throw new InvalidOperationException("Unsupported dungeon shelf object.");
        ref WorldTile cell = ref At(x, y);
        if (!cell.IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref cell);
        else if (VanillaWorldFrameImportance326.IsFrameImportant(cell.Type) == false)
        {
            if (cell.Shape == 1) cell.Shape = 0;
            cell.FrameX = cell.FrameY = 0;
        }
        WorldTile floor = At(x, y + 1);
        bool table = GenerationFurnitureTiles1458.IsTable(floor.Type);
        if (!cell.IsActive && floor.IsActive && !floor.IsActuated && table)
        {
            cell.Flags |= WorldTileFlags.Active; cell.Type = type;
            cell.FrameX = type == 50 ? (short)(18 * random.Next(5)) : type == 33 ? (short)0 : (short)(style * 18);
            cell.FrameY = type == 33 ? (short)(style * 22) : (short)0;
        }
        FrameSquare(x, y);
    }

    private bool destroyingChandelier;

    internal void FrameSquare(int x, int y) => FrameArea(x - 1, y - 1, x + 1, y + 1);

    private void FrameArea(int left, int top, int right, int bottom)
    {
        for (int tx = left; tx <= right; tx++)
        for (int ty = top; ty <= bottom; ty++)
        {
            ref WorldTile cell = ref At(tx, ty);
            if (!cell.IsActive)
            {
                cell.Shape = cell.TileColor = 0;
                cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            }
            else if (cell.Type == 19) DungeonWindows1458.FrameFlatPlatform(tiles, tx, ty);
            else if (cell.Type == 34 && !destroyingChandelier) FrameChandelier(tx, ty, cell);
            else if (cell.Type is 21 or 467) ValidateChest(tx, ty, cell);
            else if (cell.Type is 14 or 15 or 18 or 79 or 87 or 88 or 89 or 90 or 93 or 100 or 101 or 103 or 104 or 105 or 354 or 355)
                ValidateFurniture(tx, ty, cell);
            else if (cell.Type is 240 or 241 or 242) ValidatePainting(tx, ty, cell);
            else if (cell.Type is 13 or 33 or 49 or 50) FrameTableObject(tx, ty);
            else if (cell.Type == 135)
            {
                WorldTile floor = At(tx, ty + 1);
                if (!floor.IsActive || floor.IsActuated || floor.Shape is 1 or 2 or 3 ||
                    !DungeonGenerationTiles1458.IsSolidType(floor.TileType) || GenerationObjectSupport1458.IsBoulder(floor.Type))
                    throw new InvalidOperationException("Unsupported detached dungeon pressure plate.");
            }
            // TileFrameImportant has no attachment/framing branch for the solid, single-cell dart trap.
            else if (VanillaWorldFrameImportance326.IsFrameImportant(cell.Type) && cell.Type is not (10 or 13 or 28 or 33 or 34 or 42 or 49 or 50 or 91 or 136 or 137 or 215))
                throw new InvalidOperationException($"Unsupported existing object {cell.Type} at {tx},{ty} in dungeon framing area {left},{top}..{right},{bottom}.");
        }
    }

    private void FrameChandelier(int x, int y, WorldTile cell)
    {
        // CheckChand derives its footprint from the inspected part, including the bank changed by
        // Lights_GenerateSwitch. That source switch can leave a partially mismatched chandelier.
        if (cell.FrameX < 0 || cell.FrameY < 0 || cell.FrameX % 18 != 0 || cell.FrameY % 18 != 0)
            throw new InvalidOperationException("Invalid dungeon chandelier frame.");
        int column = cell.FrameX / 18 % 3, row = cell.FrameY / 18 % 3;
        int left = x - column, top = y - row, frameX = cell.FrameX - column * 18, frameY = cell.FrameY - row * 18;
        bool valid = true;
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 3; dy++)
        {
            WorldTile part = At(left + dx, top + dy);
            valid &= part.IsActive && part.Type == 34 && part.FrameX == frameX + dx * 18 && part.FrameY == frameY + dy * 18;
        }
        WorldTile ceiling = At(left + 1, top - 1);
        valid &= ceiling.IsActive && !ceiling.IsActuated && DungeonGenerationTiles1458.IsSolidType(ceiling.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(ceiling.TileType);
        if (valid) return;
        destroyingChandelier = true;
        try
        {
            for (int tx = left; tx < left + 3; tx++)
            for (int ty = top; ty < top + 3; ty++)
            {
                ref WorldTile part = ref At(tx, ty);
                if (!part.IsActive || part.Type != 34) continue;
                if (At(tx, ty + 1) is { IsActive: true, Type: 10, FrameX: < 54, FrameY: >= 594 and <= 646 })
                    throw new InvalidOperationException("Unverified locked-door/chandelier intersection.");
                // KillTile_GetTileDustAmount=10. MakeTileDust still draws Next(2) for type34 before
                // suppressing the dust; Item.NewItem itself refuses generation-time drops.
                for (int dust = 0; dust < 10; dust++) _ = random.Next(2);
                part.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
                part.Type = 0; part.Shape = 0; part.TileColor = 0; part.FrameX = part.FrameY = -1;
                FrameSquare(tx, ty);
            }
        }
        finally { destroyingChandelier = false; }
        FrameArea(left - 1, top - 1, left + 3, top + 3);
    }

    private void FrameTableObject(int x, int y)
    {
        WorldTile support = At(x, y + 1);
        bool remove = support.Shape == 1;
        if (support.Shape is 2 or 3)
        {
            int neighbor = support.Shape == 3 ? x - 1 : x + 1;
            remove = support.Type != 19 || At(neighbor, y + 1) is not { IsActive: true, Type: 19, Shape: 0 };
        }
        else if (!remove && (!support.IsActive || !GenerationFurnitureTiles1458.IsTable(support.Type)))
            throw new InvalidOperationException("Unsupported dungeon table-object attachment.");
        if (!remove) return;
        // CheckOnTable1x1 -> generation-time KillTile: these single-cell decorations have no item/drop
        // allocation or random dust draws on the dedicated server. Keep wall, wires and liquids.
        ref WorldTile cell = ref At(x, y);
        cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.Type = 0; cell.Shape = 0; cell.TileColor = 0; cell.FrameX = cell.FrameY = -1;
        FrameSquare(x, y);
    }

    private void ValidatePainting(int x, int y, WorldTile cell)
    {
        int width = cell.Type == 240 ? 3 : cell.Type == 241 ? 4 : 6, height = cell.Type == 242 ? 4 : 3;
        int dx = cell.FrameX / 18 % width, dy = cell.FrameY / 18 % height;
        ValidateParts(x - dx, y - dy, width, height, cell.Type, cell.FrameX - dx * 18, cell.FrameY - dy * 18, wall: true);
    }

    private void ValidateFurniture(int x, int y, WorldTile cell)
    {
        (int width, int height, int wrapY) = cell.Type switch
        {
            14 or 87 or 88 or 89 => (3, 2, 36), 15 => (1, 2, 40), 18 or 103 => (2, 1, 18),
            79 or 90 => (4, 2, 36), 93 => (1, 3, 54), 100 => (2, 2, 36),
            101 => (3, 4, 72), 104 => (2, 5, 90), 105 => (2, 3, 54),
            354 or 355 => (3, 3, 54), _ => throw new InvalidOperationException("Unsupported dungeon furniture frame.")
        };
        int dx = cell.FrameX / 18 % width, dy = cell.FrameY % wrapY / 18;
        int left = x - dx, top = y - dy;
        ValidateParts(left, top, width, height, cell.Type, cell.FrameX - dx * 18, cell.FrameY - dy * 18, wall: false);
        for (int column = left; column < left + width; column++)
        {
            WorldTile floor = At(column, top + height);
            bool table = GenerationFurnitureTiles1458.IsTable(floor.Type);
            if (!floor.IsActive || floor.IsActuated || (cell.Type == 103 ? !table :
                !DungeonGenerationTiles1458.IsSolidType(floor.TileType) && !(cell.Type == 100 && table)))
                throw new InvalidOperationException("Unsupported damaged dungeon furniture support.");
        }
    }

    private void ValidateParts(int left, int top, int width, int height, ushort type, int frameX, int frameY, bool wall)
    {
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            WorldTile part = At(left + dx, top + dy);
            if (!part.IsActive || part.Type != type || part.FrameX != frameX + dx * 18 || part.FrameY != frameY + dy * 18 || wall && part.Wall == 0)
                throw new InvalidOperationException("Unsupported damaged dungeon object framing.");
        }
    }

    private void ValidateChest(int x, int y, WorldTile anchor)
    {
        // WorldGen.CheckChest (1.4.5.8) leaves a complete, supported chest unchanged. Destruction needs
        // its side-table transaction; do not silently treat a damaged/unsupported chest as intact here.
        if (anchor.FrameX < 0 || anchor.FrameY is not (0 or 18))
            throw new InvalidOperationException("Invalid chest frames during dungeon generation.");
        int left = x - anchor.FrameX / 18 % 2, top = y - anchor.FrameY / 18;
        for (int column = 0; column < 2; column++)
        {
            for (int row = 0; row < 2; row++)
            {
                WorldTile part = At(left + column, top + row);
                if (!part.IsActive || part.Type != anchor.Type || part.FrameX < 0 ||
                    part.FrameX / 18 % 2 != column || part.FrameY != row * 18)
                    throw new InvalidOperationException("Damaged chest requires side-table removal during dungeon generation.");
            }
            WorldTile floor = At(left + column, top + 2);
            if (!floor.IsActive || !DungeonGenerationTiles1458.IsSolidType(floor.TileType))
                throw new InvalidOperationException("Unsupported chest requires inventory-aware framing during dungeon generation.");
        }
    }

    internal bool Banner(int x, int y, int style)
    {
        if (style is < 10 or > 15) throw new InvalidOperationException("Unsupported ordinary dungeon banner style.");
        if (At(x, y).IsActive) throw new InvalidOperationException("Dungeon banner anchor must be empty.");
        HellFortGenerator1458.ClearPlacementAnchor(ref At(x, y));
        WorldTile ceiling = At(x, y - 1);
        bool valid = ceiling.IsActive && !ceiling.IsActuated && DungeonGenerationTiles1458.IsSolidType(ceiling.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(ceiling.TileType) && !At(x, y + 1).IsActive && !At(x, y + 2).IsActive;
        if (valid)
            for (int row = 0; row < 3; row++)
            {
                ref WorldTile cell = ref At(x, y + row);
                cell.Flags |= WorldTileFlags.Active; cell.Type = 91;
                cell.FrameX = (short)(style * 18); cell.FrameY = (short)(row * 18);
            }
        FrameSquare(x, y);
        return valid;
    }

    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
