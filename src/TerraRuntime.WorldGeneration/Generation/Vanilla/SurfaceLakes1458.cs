using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary Lakes placement and SonOfLakinater geometry, pinned to 1.4.5.8.
/// Owns only unpublished surface basins and their ordered exclusion columns.</summary>
internal sealed class SurfaceLakes1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    double worldSurface, double surfaceLow, CancellationToken cancellationToken)
{
    private readonly StoneBiomeTiles1458 framing = new(tiles, random);
    private readonly GenerationGrass1458 grass = new(tiles, random, cancellationToken);

    public void Generate(Workspace workspace)
    {
        Check(0);
        var tunnels = workspace.VanillaTunnelColumns;
        var caves = workspace.VanillaMountainCaves;
        var desert = workspace.VanillaUndergroundDesertRegion ??
            throw new InvalidOperationException("Lakes require retained underground desert bounds.");
        var lakes = new List<int>(workspace.VanillaLakeColumns.ToArray());
        double scale = tiles.Dimensions.WidthTiles / 4200d;
        int count = random.Next((int)(scale * 3), (int)(scale * 6));
        for (int lake = 0; lake < count && lakes.Count < 49; lake++)
        for (int attempt = 0; attempt < tiles.Dimensions.WidthTiles / 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(340, tiles.Dimensions.WidthTiles - 340);
            for (int redraw = 0; x > tiles.Dimensions.WidthTiles * .45 && x < tiles.Dimensions.WidthTiles * .55; redraw++)
            {
                Check(redraw);
                x = random.Next(340, tiles.Dimensions.WidthTiles - 340);
            }
            bool rejected = false;
            foreach (int existing in lakes) if (Math.Abs(x - existing) < 150) { rejected = true; break; }
            foreach (var cave in caves) if (Math.Abs(x - cave.X) < 100) { rejected = true; break; }
            foreach (int tunnel in tunnels) if (Math.Abs(x - tunnel) < 100) { rejected = true; break; }
            if (rejected) continue;
            int y = (int)surfaceLow - 20;
            while (!At(x,y).IsActive)
            {
                y++;
                if (y >= worldSurface || At(x,y).Wall > 0) { rejected = true; break; }
            }
            if (At(x,y).Type == 53) rejected = true;
            if (rejected) continue;
            for (int tx = x - 50; tx <= x + 50; tx++)
            for (int ty = y - 50; ty <= y + 50; ty++)
                if (At(tx,ty).Type is 203 or 25) { rejected = true; break; }
            if (rejected) continue;
            int originalY = y;
            while (!Flat(x - 20,y) || !Flat(x + 20,y))
            {
                cancellationToken.ThrowIfCancellationRequested();
                y++;
                if (y > worldSurface - 50) rejected = true;
                if (y >= tiles.Dimensions.HeightTiles) throw new InvalidOperationException("Lake support scan left the candidate.");
            }
            if (rejected || y - originalY > 10) continue;
            for (int tx = x - 60; tx <= x + 60; tx++)
                if (At(tx,y - 20).IsActive || At(tx,y - 20).Wall > 0) rejected = true;
            if (rejected) continue;
            int solids = 0;
            for (int tx = x - 60; tx <= x + 60; tx++)
            for (int ty = y; ty <= y + 120; ty++)
                if (Flat(tx,ty)) solids++;
            if (solids < 121 * 121 * .8 ||
                (x - 8 < desert.Right && x + 8 > desert.X && y - 8 < desert.Bottom && y + 8 > desert.Y)) continue;
            Carve(x,y);
            lakes.Add(x);
            break;
        }
        workspace.SetVanillaLakeColumns(lakes.ToArray());
    }

    public void Carve(int originX, int originY)
    {
        Check(0);
        _ = random.Next(3); // Ordinary world samples the secret-seed liquid branch but keeps water.
        double strength = random.Next(15,31), remaining = random.Next(30,61);
        if (random.Next(5) == 0) { strength *= 1.3; remaining *= 1.3; }
        double x = originX, y = originY;
        double acceleration = random.NextDouble() * .002;
        double vx;
        if (random.Next(4) != 0) vx = random.Next(-15,16) * .01;
        else { vx = random.Next(-50,51) * .01; acceleration = random.NextDouble() * .004 + .001; }
        double vy = random.Next(101) * .01, originalSteps = remaining;
        for (int iteration = 0; strength > 3 && remaining > 0; iteration++)
        {
            Check(iteration);
            strength -= random.Next(11) * .1;
            remaining--;
            int left = Math.Max(0,(int)(x - strength * 4)), right = Math.Min(tiles.Dimensions.WidthTiles,(int)(x + strength * 4));
            int top = Math.Max(0,(int)(y - strength * 3)), bottom = Math.Min(tiles.Dimensions.HeightTiles,(int)(y + strength * 2));
            for (int tx = left; tx < right; tx++)
            for (int ty = top; ty < bottom; ty++)
            {
                double dx = Math.Abs(tx - x) * .6, dy = Math.Abs(ty - y) * 1.4;
                dx += (Math.Abs(tx - x) * .3 - dx) * (remaining / originalSteps);
                dy += (Math.Abs(ty - y) * 5 - dy) * (remaining / originalSteps);
                double distance = Math.Sqrt(dx * dx + dy * dy);
                ref WorldTile cell = ref At(tx,ty);
                if (distance < strength * .4)
                {
                    if (ty >= originY && (ty > originY + 1 || WaterStays(tx,ty)))
                    { cell.LiquidAmount = 255; cell.LiquidKind = WorldLiquidKind.Water; }
                    Excavate(tx,ty);
                }
                else if (ty > originY + 1 && distance < strength && cell.LiquidAmount == 0)
                {
                    if (Math.Abs(tx - x) * .8 < strength && !cell.IsActive && cell.Wall > 0 &&
                        At(tx - 1,ty).Wall > 0 && At(tx + 1,ty).Wall > 0 && At(tx,ty + 1).Wall > 0)
                    {
                        if (!VanillaWallDefinitionCatalog.TryGet(cell.WallType,out _)) throw new InvalidOperationException("Unknown lake terrain wall.");
                        // WallID.Sets.WallTypeToTerrainTileType; all other known walls map to Dirt0.
                        cell.Type = cell.Wall switch { 40 => 147, 71 => 161, 15 => 59, 86 => 225, 3 => 25, 83 => 203, 178 => 367, 180 => 368, _ => 0 };
                        cell.Flags |= WorldTileFlags.Active;
                    }
                }
                else if (ty < originY && remaining == originalSteps - 1 && ty > surfaceLow - 20 && cell.IsActive && !IsCloud(cell.Type))
                {
                    double horizontal = Math.Abs(tx - originX) * .7;
                    double taper = 1 - (double)Math.Abs(tx - originX) / (right - originX);
                    taper *= 2.3; taper *= taper; taper *= taper;
                    if (horizontal < strength * .4 + Math.Abs(ty - (originY + 5)) * .5 * taper) Excavate(tx,ty);
                }
            }
            x += vx; y += vy;
            vx = Math.Clamp(vx + random.Next(-100,101) * acceleration,-1,1);
            vy = Math.Clamp(vy + random.Next(-100,101) * .01,.5 * (1 - remaining / originalSteps),1);
        }
    }

    private void Excavate(int x, int y)
    {
        ref WorldTile cell = ref At(x,y);
        cell.Flags &= ~WorldTileFlags.Active;
        if (cell.Type is not (59 or 60)) return;
        grass.Apply(x - 1,y,59,60); grass.Apply(x + 1,y,59,60); grass.Apply(x,y + 1,59,60);
    }

    private bool WaterStays(int x, int y) => RetainsWater(At(x,y + 1)) && RetainsWater(At(x - 1,y)) && RetainsWater(At(x + 1,y));
    private static bool RetainsWater(in WorldTile tile)
    {
        if (tile.IsActive && tile.Type >= VanillaTileIds.Count) throw new InvalidOperationException("Unknown lake boundary material.");
        return tile.LiquidAmount == 255 || (tile.IsActive && tile.Type != 484 &&
            VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType));
    }
    private static bool IsCloud(ushort type)
    {
        if (type >= VanillaTileIds.Count) throw new InvalidOperationException("Unknown material above a lake.");
        return type is 189 or 196 or 460 or 717 or 718 or 719;
    }
    private bool Flat(int x, int y)
    {
        WorldTile tile = At(x,y); // Validate coordinates before the shared framing owner's unchecked access.
        if (tile.IsActive && tile.Type >= VanillaTileIds.Count) throw new InvalidOperationException("Unknown lake support material.");
        return framing.Flat(x,y);
    }
    private void Check(int iteration)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (iteration >= 100_000) throw new InvalidOperationException("Lake generation exceeded its safety iteration budget.");
    }
    private ref WorldTile At(int x, int y)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
            throw new InvalidOperationException("Lake geometry left the candidate terrain.");
        return ref tiles.Tiles[tiles.GetUncheckedIndex(x,y)];
    }
}
