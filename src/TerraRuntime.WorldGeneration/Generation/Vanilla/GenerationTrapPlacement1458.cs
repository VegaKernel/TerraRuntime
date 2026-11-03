using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary cavern trap assemblers using the retained generation tile and RNG owners.</summary>
internal sealed class GenerationTrapPlacement1458
{
    private readonly Workspace workspace;
    private readonly WorldTileStore store;
    private readonly IWorldGenerationVanillaRandom random;
    private readonly GenerationTileFraming1458 framing;
    private readonly double worldSurface;
    private readonly int lavaLine;
    private readonly WorldGenerationPoint shimmer;
    private readonly int dungeonSide;
    private int Width => workspace.WidthTiles;
    private int Height => workspace.HeightTiles;

    internal GenerationTrapPlacement1458(Workspace workspace, IWorldGenerationVanillaRandom random, double worldSurface,
        int lavaLine, WorldGenerationPoint shimmer, int dungeonSide)
    {
        this.workspace = workspace; store = workspace.TileStore; this.random = random; this.worldSurface = worldSurface;
        this.lavaLine = lavaLine; this.shimmer = shimmer; this.dungeonSide = dungeonSide;
        framing = new(store, random, workspace, useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity);
    }

    internal bool Place(int x, int y, int kind = -1)
    {
        if (!Continue(x, y)) return false;
        double dx = x - shimmer.X, dy = y - shimmer.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 100) return false;
        int floor = y; bool underworld = false;
        while (!framing.SolidTile(x, floor))
        {
            if (++floor > Height - 10 || !Continue(x, floor)) return false;
            if (floor >= Height - 300) underworld = true;
        }
        int plateY = floor - 1;
        if (Near(x, plateY, 70, 20) || At(x, plateY).Wall is 87 or 350) return false;
        bool lava = At(x, plateY).LiquidAmount > 0 && At(x, plateY).LiquidKind == WorldLiquidKind.Lava;
        if (kind == -1)
        {
            if (random.Next(20) == 0) kind = 2;
            else if (plateY > lavaLine + 30 && random.Next(6) != 0) kind = 3;
            else kind = random.Next(2);
        }
        if (!InWorld(x, plateY, 3) || (lava || underworld) && kind != 3) return false;
        for (int xx = x - 1; xx <= x + 1; xx++) for (int yy = plateY - 2; yy <= plateY; yy++)
            if (At(xx, yy).IsActive && !At(xx, yy).IsActuated) return false;
        if (At(x, plateY + 1).IsActive && At(x, plateY + 1).Type is 48 or 232) return false;
        if (!framing.CanGenerateTrapPressurePlate(x, plateY)) return false;
        if (kind == 1 && (Near(x, plateY, 147, 3) || Near(x, plateY, 161, 3))) kind = 0;
        return kind switch { 0 => Dart(x, plateY), 1 => Boulder(x, plateY), 2 => Explosive(x, plateY), 3 => Geyser(x, plateY), _ => false };
    }

    private bool Dart(int x, int y)
    {
        int launcherY = y - random.Next(3), left = x, right = x;
        while (!framing.SolidTile(left, launcherY) && (!At(left, launcherY).IsActive || !Cracked(At(left, launcherY).Type)))
            if (--left < 0 || !Continue(left, launcherY)) return false;
        while (!framing.SolidTile(right, launcherY) && (!At(right, launcherY).IsActive || !Cracked(At(right, launcherY).Type)))
            if (++right >= Width || !Continue(right, launcherY)) return false;
        bool leftOK = WallUsable(left, launcherY, x - left), rightOK = WallUsable(right, launcherY, right - x);
        int target, direction;
        if (leftOK && rightOK) { target = left; direction = 1; if (random.Next(2) == 0) { target = right; direction = -1; } }
        else if (rightOK) { target = right; direction = -1; }
        else if (leftOK) { target = left; direction = 1; }
        else return false;
        if (At(target, launcherY).Type == 190 || At(target, launcherY).Wall == 350) return false;
        framing.PlaceTempleMechanism(x, y, 135, At(x, y).Wall > 0 ? 2 : random.Next(2, 4));
        framing.KillTile(target, launcherY);
        framing.PlaceTempleMechanism(target, launcherY, 137, 0);
        if (direction == 1) At(target, launcherY).FrameX += 18;
        Wire(x, y, target, launcherY);
        return true;
    }

    private bool WallUsable(int x, int y, int distance) => distance > 5 && distance < 50 && framing.SolidTile(x, y + 1) &&
        !(At(x, y).IsActive && At(x, y).Type is 10 or 48) && !(At(x, y + 1).IsActive && At(x, y + 1).Type is 10 or 48);

    private bool Boulder(int x, int y)
    {
        int shaft = x + random.Next(-1, 2), ceiling = y - 8;
        while (true)
        {
            bool solid = true; int natural = 0;
            for (int xx = shaft - 2; xx <= shaft + 3; xx++) for (int yy = ceiling; yy <= ceiling + 3; yy++)
            {
                if (!framing.SolidTile(xx, yy)) solid = false;
                if (!Continue(xx, yy)) return false;
                WorldTile tile = At(xx, yy);
                if (!tile.IsActive) continue;
                if (tile.Type == 226 || DungeonGenerationTiles1458.IsDungeonTile(tile.Type) ||
                    (tile.Flags & WorldTileFlags.Actuator) != 0 || GenerationObjectSupport1458.IsBoulder(tile.Type)) return false;
                if (tile.Type is 0 or 1 or 59) natural++;
            }
            if (--ceiling < worldSurface) return false;
            if (solid && natural > 2) break;
        }
        if (y - ceiling <= 5 || y - ceiling >= 40 || Near(shaft, ceiling, 21, 4) || Near(shaft, ceiling, 467, 4) ||
            NearBoulder(shaft, ceiling, 10)) return false;
        for (int xx = shaft; xx <= shaft + 1; xx++) for (int yy = ceiling; yy <= y; yy++)
            if (At(xx, yy).Type != 379) framing.KillTile(xx, yy);
        for (int xx = shaft - 2; xx <= shaft + 3; xx++) for (int yy = ceiling - 2; yy <= ceiling + 3; yy++)
            if (framing.SolidTile(xx, yy)) At(xx, yy).Type = 1;
        framing.PlaceTempleMechanism(x, y, 135, 7);
        framing.PlaceTrapStone(shaft, ceiling + 2); framing.PlaceTrapStone(shaft + 1, ceiling + 2);
        ushort type = 138;
        if (random.Next(2) == 0 && SampleNearby(shaft, ceiling, 80, 3, (xx, yy) => At(xx, yy).Wall == 62)) type = 714;
        else if (random.Next(6) == 0 && NearDungeon(shaft, ceiling)) type = 715;
        else if (random.Next(3) == 0 && SampleNearby(shaft, ceiling, 60, 3, (xx, yy) => At(xx, yy).LiquidAmount > 0 && At(xx, yy).LiquidKind == WorldLiquidKind.Lava)) type = 713;
        else if (random.Next(25) == 0) type = 711;
        else if (random.Next(20) == 0) type = 664;
        int quota = Width <= 4200 ? 2 : Width <= 6400 ? 4 : 6;
        if (type == 138 && workspace.VanillaBoulderPetsPlaced < quota) { workspace.VanillaBoulderPetsPlaced++; type = 716; }
        framing.PlaceTrapBoulder(shaft + 1, ceiling + 1, type);
        ceiling += 2;
        for (int column = 0; column < 2; column++) for (int row = 0; row < 3; row++)
        {
            int xx = shaft + column, yy = ceiling + row; framing.PlaceTrapStone(xx, yy);
            At(xx, yy).Flags |= WorldTileFlags.WireRed | WorldTileFlags.Actuator;
        }
        Wire(x, y, shaft, ceiling);
        return true;
    }

    private bool Explosive(int x, int y)
    {
        int depth = random.Next(4, 7), target = x + random.Next(-1, 2), bottom = y;
        for (int step = 0; step < depth; step++) if (!InWorld(target, ++bottom, 5) || !framing.SolidTile(target, bottom) || !Continue(target, bottom)) return false;
        for (int xx = target - 2; xx <= target + 2; xx++) for (int yy = bottom - 2; yy <= bottom + 2; yy++)
            if (!InWorld(xx, yy) || !framing.SolidTile(xx, yy) || !Continue(xx, yy)) return false;
        framing.KillTile(target, bottom);
        ref WorldTile explosive = ref At(target, bottom); explosive.Flags |= WorldTileFlags.Active; explosive.Type = 141;
        explosive.FrameX = 0; explosive.FrameY = (short)(18 * random.Next(2));
        framing.PlaceTempleMechanism(x, y, 135, random.Next(2, 4)); Wire(x, y, target, bottom);
        // Source case2 breaks out of its switch after mutating, then reaches the final false return.
        return false;
    }

    private bool Geyser(int x, int y)
    {
        if (At(x + 1, y).IsActive || (At(x, y).LiquidAmount > 0 && At(x, y).LiquidKind != WorldLiquidKind.Lava) || !Continue(x, y)) return false;
        for (int xx = x; xx <= x + 1; xx++) if (!InWorld(xx, y + 1, 5) || !framing.SolidTile(xx, y + 1) || !Continue(xx, y + 1)) return false;
        int style = random.Next(2);
        for (int column = 0; column < 2; column++) { ref WorldTile tile = ref At(x + column, y); tile.Flags |= WorldTileFlags.Active; tile.Type = 443; tile.FrameX = (short)(18 * column + 36 * style); tile.FrameY = 0; }
        return true;
    }

    internal bool PlaceSand(int x, int y)
    {
        int floor = y;
        while (!At(x, floor).IsActive && floor < Height - 200) floor++;
        if (floor >= Height - 200) return false;
        WorldTile ground = At(x, floor);
        if (!framing.GenerationSolid(ground.Type) || ground.IsHalfBrick || ground.SourceSlope is 1 or 2 ||
            ground.Type is not (53 or 397 or 396) || ground.Wall is not (216 or 187)) return false;
        floor--;
        int halfWidth = random.Next(6, 12), depth = random.Next(6, 14);
        for (int xx = x - 25; xx <= x + 25; xx++) for (int yy = floor - 25; yy < floor + 25; yy++)
        {
            WorldTile tile = At(xx, yy);
            if ((tile.Flags & WorldTileFlags.WireRed) != 0 || tile.Type is 21 or 467 or 240 or 241 or 242 or 245 or 246 or 10 or 19 or 15 or 219 or 314) return false;
        }
        for (int xx = x - 2; xx <= x + 2; xx++) for (int yy = floor + 1; yy <= floor + 3; yy++)
            if (!At(xx, yy).IsActive || !framing.GenerationSolid(At(xx, yy).Type)) return false;
        if (At(x, floor + 1).Type == 162) return false;
        int roof = -1;
        for (int yy = floor; yy > floor - 30; yy--)
        {
            if (!At(x, yy).IsActive) continue;
            if (At(x, yy).Type != 396) return false;
            roof = yy; break;
        }
        if (roof < 0 || floor - roof < depth + 4) return false;
        int mass = 0, middle = (floor + roof) / 2;
        for (int xx = x - halfWidth; xx <= x + halfWidth; xx++)
        {
            if (ActiveSolid(xx, middle)) return false;
            for (int yy = roof - depth; yy <= roof; yy++)
                if (At(xx, yy).IsActive)
                {
                    if (StoneBiomeTiles1458.IsOre(At(xx, yy).Type) || At(xx, yy).Type == 404) return false;
                    if (framing.GenerationSolid(At(xx, yy).Type)) mass++;
                }
        }
        if (mass < (halfWidth * 2 + 1) * (depth + 1) * .75) return false;
        for (int xx = x - halfWidth - 1; xx <= x + halfWidth + 1; xx++) for (int yy = roof - depth; yy <= roof; yy++)
        {
            bool solid = ActiveSolid(xx, yy);
            ref WorldTile cell = ref At(xx, yy);
            if (yy == roof)
            {
                cell.ClearSourceShape();
                if (!solid) { cell.Flags |= WorldTileFlags.Active; cell.Type = 396; }
            }
            else if (yy == roof - depth)
            {
                DungeonGenerationTiles1458.ClearTile(ref cell); cell.Flags |= WorldTileFlags.Active;
                cell.Type = (ushort)(solid && ActiveSolid(xx, yy - 1) ? 397 : 396);
            }
            else if (xx == x - halfWidth - 1 || xx == x + halfWidth + 1)
            {
                if (solid) cell.ClearSourceShape();
                else { DungeonGenerationTiles1458.ClearTile(ref cell); cell.Flags |= WorldTileFlags.Active; cell.Type = 396; }
            }
            else { DungeonGenerationTiles1458.ClearTile(ref cell); cell.Flags |= WorldTileFlags.Active; cell.Type = 53; }
        }
        for (int yy = (int)(roof - depth * .666); yy <= roof - depth * .333; yy++)
        {
            ref WorldTile left = ref At(x - halfWidth - 2, yy);
            if (yy < roof - depth * .4) { if (left.SourceSlope is 3 or 4) left.TrySetSourceShape(left.IsHalfBrick, 0); }
            else if (yy > roof - depth * .6) { if (left.SourceSlope is 1 or 2) left.TrySetSourceShape(left.IsHalfBrick, 0); left.TrySetSourceShape(false, left.SourceSlope); }
            else left.ClearSourceShape();
            if (!ActiveSolid(x - halfWidth - 2, yy)) { left.Flags |= WorldTileFlags.Active; left.Type = 396; }
            if (!ActiveSolid(x + halfWidth + 2, yy)) { At(x + halfWidth + 2, yy).Flags |= WorldTileFlags.Active; At(x + halfWidth + 2, yy).Type = 396; }
        }
        for (int yy = roof - depth; yy <= roof; yy++)
        {
            At(x - halfWidth - 2, yy).ClearSourceShape(); At(x - halfWidth - 1, yy).ClearSourceShape();
            At(x - halfWidth + 1, yy).ClearSourceShape(); At(x - halfWidth + 2, yy).ClearSourceShape();
        }
        for (int xx = x - halfWidth - 1; xx < x + halfWidth + 1; xx++)
        {
            ref WorldTile cell = ref At(xx, floor - depth - 1);
            cell.TrySetSourceShape(false, cell.SourceSlope is 3 or 4 ? (byte)0 : cell.SourceSlope);
        }
        framing.KillTile(x - 2, floor); framing.KillTile(x - 1, floor); framing.KillTile(x + 1, floor); framing.KillTile(x + 2, floor);
        framing.PlaceTempleMechanism(x, floor, 135, 7);
        for (int xx = x - halfWidth; xx <= x + halfWidth; xx++)
        {
            int limit = floor;
            if (xx < x - halfWidth * .8 || xx > x + halfWidth * .8) limit -= 3;
            else if (xx < x - halfWidth * .6 || xx > x + halfWidth * .6) limit -= 2;
            else if (xx < x - halfWidth * .4 || xx > x + halfWidth * .4) limit--;
            for (int yy = roof; yy <= floor; yy++)
            {
                if (xx == x) At(xx, yy).Flags |= WorldTileFlags.WireRed;
                if (!ActiveSolid(xx, yy)) continue;
                if (yy < roof + 2) At(xx, yy).Flags |= WorldTileFlags.Actuator | WorldTileFlags.WireRed;
                else if (yy < limit) framing.KillTile(xx, yy);
            }
        }
        return true;
    }
    private bool ActiveSolid(int x, int y) => At(x, y).IsActive && framing.GenerationSolid(At(x, y).Type);

    private bool NearDungeon(int x, int y)
    {
        if (dungeonSide <= -1 && x > Width / 2 || dungeonSide >= 1 && x < Width / 2) return false;
        return SampleNearby(x, y, 1000, 10, (xx, yy) => DungeonGenerationTiles1458.IsDungeonWall(At(xx, yy).Wall));
    }
    private bool SampleNearby(int x, int y, int radius, int stride, Func<int, int, bool> match)
    { for (int xx = x - radius; xx <= x + radius; xx += stride) for (int yy = y - radius; yy <= y + radius; yy += stride) if (InWorld(xx, yy) && match(xx, yy)) return true; return false; }
    private bool Near(int x, int y, ushort type, int radius) => SampleNearby(x, y, radius, 1, (xx, yy) => At(xx, yy).IsActive && At(xx, yy).Type == type);
    private bool NearBoulder(int x, int y, int radius) => SampleNearby(x, y, radius, 1, (xx, yy) => At(xx, yy).IsActive && GenerationObjectSupport1458.IsBoulder(At(xx, yy).Type));
    private static bool Cracked(ushort type) => type is 481 or 482 or 483;
    private bool Continue(int x, int y) => InWorld(x, y) && At(x, y).Wall != 350;
    private bool InWorld(int x, int y, int padding = 0) => x >= padding && y >= padding && x < Width - padding && y < Height - padding;
    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
    private void Wire(int x, int y, int targetX, int targetY)
    { while (x != targetX || y != targetY) { At(x, y).Flags |= WorldTileFlags.WireRed; x += Math.Sign(targetX - x); At(x, y).Flags |= WorldTileFlags.WireRed; y += Math.Sign(targetY - y); At(x, y).Flags |= WorldTileFlags.WireRed; } }
}
