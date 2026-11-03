using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal static class NotTheBeesEarly1458
{
    internal static void Apply(Workspace workspace, IWorldGenerationVanillaRandom random, VanillaWorldSeedProfile1458 profile,
        double surface, double rock, int lavaLine, int dungeonSide, CancellationToken cancellation)
    {
        if (profile.Has(VanillaSpecialWorldSeed1458.Skyblock) || !profile.Has(VanillaSpecialWorldSeed1458.NotTheBees)) return;
        int width = workspace.WidthTiles, height = workspace.HeightTiles, sideBand = width / 7;
        bool remix = profile.Has(VanillaSpecialWorldSeed1458.Remix), anniversary = profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10),
            constant = profile.Has(VanillaSpecialWorldSeed1458.TheConstant), drunk = profile.Has(VanillaSpecialWorldSeed1458.DrunkWorld);
        int honeyBottom = remix ? height - 180 : (lavaLine + height - 180) / 2;
        for (int x = 5; x < width - 5; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 5; y < height - 180; y++)
            {
                ref WorldTile cell = ref At(x, y);
                bool edge = y <= (surface + rock) / 2d + 40d && (x < 380 || x > width - 380) ||
                    y < (surface + rock * 2d) / 3d + random.Next(3) && (x < 330 - random.Next(3) || x > width - 330 + random.Next(3));
                if (remix && (x < sideBand + random.Next(3) || x >= width - sideBand - random.Next(3) ||
                    y > (surface * 2d + rock) / 3d + random.Next(3) && y < height - 350 - random.Next(3))) continue;
                if (!remix && (anniversary || constant || dungeonSide <= -1 && x < width / 2 || dungeonSide >= 1 && x > width / 2) && edge)
                {
                    if (cell.IsActive && cell.Type == 229) { cell.Flags &= ~WorldTileFlags.Active; cell.LiquidKind = WorldLiquidKind.Water; cell.LiquidAmount = 255; }
                    if (cell.IsActive && IsDungeon(cell.Type)) cell.TileColor = 14;
                    if (DungeonGenerationTiles1458.IsDungeonWall(cell.Wall)) cell.WallColor = 14;
                    continue;
                }
                if (!remix && (anniversary || constant || drunk) && x > width * 0.4d - random.Next(3) && x < width * 0.6d + random.Next(3))
                {
                    if (y < rock - 1d)
                    {
                        if (cell.Type == 0 && HasOpenNeighbour(x, y)) cell.Type = 2;
                        if (cell.Wall == 15)
                        {
                            ushort wall = random.Next(2) == 0 ? (ushort)63 : (ushort)65;
                            if (constant) wall = 63; else if (anniversary) wall = 65;
                            cell.Wall = wall;
                        }
                    }
                    if (cell.Type == 60) cell.Type = 59;
                    continue;
                }
                if (cell.Type is 52 or 382) cell.Type = 62;
                bool solid = cell.IsActive && !cell.IsActuated && workspace.IsVanillaGenerationSolid(cell.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(cell.TileType);
                if ((solid || cell.IsActive && cell.Type is 481 or 482 or 483) &&
                    (!constant || remix || cell.Type is not (1 or 147 or 161 or 30 or 321 or 158 or 190 or 162)) &&
                    !StoneBiomeTiles1458.IsOre(cell.Type) && cell.Type is not (368 or 367 or 123 or 40 or 379))
                {
                    if (cell.Type is 191 or 383) { if (!remix) cell.Type = 383; }
                    else if (cell.Type is 192 or 384) { if (!remix) cell.Type = 384; }
                    else if (!Excluded(cell.Type))
                    {
                        if (cell.Type == 224) { if (!constant || remix) cell.Type = 229; }
                        else if (cell.Type == 53)
                        { if ((!constant || remix) && (x < 380 + random.Next(3) || x > width - 380 - random.Next(3))) cell.Type = 229; }
                        else if ((x <= 380 - random.Next(3) || x >= width - 380 + random.Next(3) || cell.Type is not (397 or 396)) &&
                            cell.Type is not (10 or 203 or 25 or 137 or 141) && !GenerationObjectSupport1458.IsBoulder(cell.Type))
                        {
                            if (IsDungeon(cell.Type)) cell.TileColor = 14;
                            else if (cell.Type is not (226 or 202 or 70 or 48 or 232))
                            {
                                if (cell.Type is 2 or 23 or 199 or 109 or 477 or 492 or 60 or 70)
                                    cell.Type = y > lavaLine + random.Next(-2, 3) + 2 ? (ushort)70 : (ushort)60;
                                else if (cell.Type is 0 or 59) cell.Type = 59;
                                else if (cell.Type is not (633 or 562 or 563) && (!constant || remix))
                                {
                                    if (y > lavaLine + random.Next(-2, 3) + 2)
                                    { if (y <= honeyBottom + random.Next(3)) cell.Type = 230; }
                                    else if (!remix || y > surface + random.Next(-1, 2)) cell.Type = 225;
                                }
                            }
                        }
                    }
                }
                if (!remix && y < height - 50 && y > 50 && x > 600 && x < width - 600)
                {
                    Antialias(x, y, 225, 60);
                    Antialias(x, y, 230, 70);
                }
                if (cell.Wall is not (15 or 64 or 204 or 205 or 206 or 207 or 87))
                {
                    if (DungeonGenerationTiles1458.IsDungeonWall(cell.Wall)) cell.WallColor = 14;
                    else if ((remix || !anniversary && !constant && !drunk || !(x > width * 0.4d - random.Next(3)) || !(x < width * 0.6d + random.Next(3))) &&
                        (!constant || remix) && cell.Wall is not (23 or 24 or 42 or 10 or 21 or 88 or 89 or 90 or 91 or 92 or 241 or 93 or 243 or 242 or 82 or 187 or 216 or 34 or 244))
                    {
                        if (cell.Wall == 63) cell.Wall = 64;
                        else if (cell.Wall is not (2 or 196 or 197 or 198 or 199 or 3 or 83 or 73 or 62 or 180 or 178 or 13 or 14 or 312 or 313) && cell.Wall > 0 &&
                            (!remix || y > surface + random.Next(-1, 2)) && y <= honeyBottom + random.Next(3)) cell.Wall = 86;
                    }
                }
                if ((!anniversary || remix || !(y < surface)) && (!constant || remix) && cell.LiquidAmount > 0 && y <= lavaLine + 2)
                {
                    if (x > width * 0.4d && x < width * 0.6d || y > rock && (x < 580 || x > width - 580) || DungeonGenerationTiles1458.IsDungeonWall(cell.Wall))
                        cell.LiquidKind = (WorldLiquidKind)((int)cell.LiquidKind & ~2);
                    else cell.LiquidKind = WorldLiquidKind.Honey;
                }
            }
        }
        ref WorldTile At(int x, int y) => ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];
        bool HasOpenNeighbour(int x, int y)
        { foreach (var d in Neighbours) if (!At(x + d.X, y + d.Y).IsActive) return true; return false; }
        bool NeighbourWallsEmpty(int x, int y)
        { foreach (var d in Neighbours) if (At(x + d.X, y + d.Y).Wall != 0) return false; return true; }
        void Antialias(int x, int y, ushort type, ushort replacement)
        {
            ref WorldTile cell = ref At(x, y);
            if (!cell.IsActive || cell.Type != type || !HasOpenNeighbour(x, y) || !(y < surface || NeighbourWallsEmpty(x, y))) return;
            cell.Type = replacement;
            for (int sample = 0; sample < 8; sample++)
            {
                int column = x + random.Next(-2, 3), row = y + random.Next(-2, 3);
                ref WorldTile other = ref At(column, row);
                if (other.IsActive && other.Type == type && !HasOpenNeighbour(column, row)) other.Type = 59;
            }
        }
    }
    // The source repeats upper-right and never tests lower-right in this stencil.
    private static readonly (int X, int Y)[] Neighbours = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1)];
    private static bool IsDungeon(ushort type) => DungeonGenerationTiles1458.IsDungeonTile(type) || type is 481 or 482 or 483;
    private static bool Excluded(ushort type) => type is 151 or 662 or 661 or 189 or 196 or 460 or 717 or 718 or 719 or 120 or 158 or 175 or 45 or 119 or >= 63 and <= 68 or 57 or 76 or 75 or 229 or 230 or 407 or 404;
}
