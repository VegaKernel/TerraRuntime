using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary GraniteBiome pressure field, material placement and decoration (1.4.5.8).</summary>
internal sealed class GraniteBiome1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random, int seed,
    int lavaLine, CancellationToken cancellation)
{
    private const int Side = 200, Center = Side / 2;
    private readonly record struct Magma(double Pressure, double Resistance, bool Active);
    private readonly StoneBiomeTiles1458 framing = new(tiles, random);
    private Magma[,] source = new Magma[Side, Side], target = new Magma[Side, Side];

    public static void Apply(Workspace workspace, IWorldGenerationVanillaRandom random, int seed, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        int width = workspace.WidthTiles, height = workspace.HeightTiles;
        double rock = workspace.VanillaTerrainState?.CurrentRockLayer ??
            throw new InvalidOperationException("Granite requires retained GenVars terrain state.");
        int lava = workspace.VanillaLiquidLines?.LavaLine ??
            throw new InvalidOperationException("Granite requires retained liquid lines.");
        if (!TerrainPass1458.IsCanonicalWorldSize(width, height) || !double.IsFinite(rock) || rock < 100 || rock >= height - 260)
            throw new InvalidOperationException("Unsupported ordinary granite pass envelope.");
        double scale = width / 4200d;
        int count = random.Next((int)(4 * scale), (int)(8 * scale) + 1);
        double band = (width - 200d) / count;
        var origins = new List<WorldGenerationPoint>(count);
        int attempts = 0;
        while (origins.Count < count)
        {
            cancellation.ThrowIfCancellationRequested();
            // Unlike Marble, source computes this fraction and product in float.
            int left = (int)(origins.Count / (float)count * (width - 200)) + 100;
            int x = random.Next(left, left + (int)band), y = random.Next((int)rock + 20, height - 220);
            int rerolls = 0;
            while (x > width * .45 && x < width * .55)
            {
                if (++rerolls > 100000) throw new InvalidOperationException("Granite central-column search exhausted its safety bound.");
                x = random.Next(380, width - 380);
            }
            attempts++;
            if (!StoneBiomeTiles1458.BiomeTileCheck(workspace.TileStore, x, y) && !workspace.TileStore.Get(x, y).IsActive)
                origins.Add(new(x, y));
            else if (attempts > width * 10) break;
            // Vanilla does not reset this attempt counter on successful selection.
        }
        var biome = new GraniteBiome1458(workspace.TileStore, random, seed, lava, cancellation);
        var regions = new List<WorldTileRegion>(origins.Count);
        foreach (WorldGenerationPoint point in origins)
            if (biome.Place(point.X, point.Y, out WorldTileRegion region)) regions.Add(region);
        workspace.SetVanillaGraniteRegions(regions.ToArray());
    }

    public bool Place(int x, int y, out WorldTileRegion region)
    {
        cancellation.ThrowIfCancellationRequested();
        region = default;
        if ((uint)x >= tiles.Dimensions.WidthTiles || (uint)y >= tiles.Dimensions.HeightTiles)
            throw new ArgumentOutOfRangeException(nameof(x));
        // CanPlace performs the biome exclusion during selection; Place only retests activity.
        if (At(x, y).IsActive) return false;
        int left = x - Center, top = y - Center;
        if (left < 0 || top < 0 || left + Side > tiles.Dimensions.WidthTiles || top + Side > tiles.Dimensions.HeightTiles)
            throw new InvalidOperationException("Granite pressure map escaped its admitted world bounds.");
        Build(left, top);
        WorldTileRegion affected = Simulate();
        PlaceMaterial(left, top, affected, ShouldUseLava(x, y));
        Cleanup(left, top, affected);
        Decorate(left, top, affected);
        region = new(left + affected.X, top + affected.Y, affected.Width, affected.Height);
        return true;
    }

    private void Build(int left, int top)
    {
        for (int x = 0; x < Side; x++)
        for (int y = 0; y < Side; y++)
            source[x, y] = target[x, y] = new(0, framing.Flat(left + x, top + y) ? 4 : 1, false);
    }

    private WorldTileRegion Simulate()
    {
        int left = Center, right = Center, top = Center, bottom = Center;
        double diagonal = 1d / Math.Sqrt(2d);
        for (int iteration = 0; iteration < 300; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            // Bounds expand during the scan, but neighbours become active in the target
            // buffer only. Do not clear that buffer: source retains alternate-step values.
            for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                Magma cell = source[x, y];
                if (!cell.Active) continue;
                double total = 0, forceX = 0, forceY = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Magma neighbor = source[x + dx, y + dy];
                    if (cell.Pressure > .01 && !neighbor.Active)
                    {
                        if (dx == -1) left = Math.Clamp(x + dx, 1, left);
                        else right = Math.Clamp(x + dx, right, Side - 2);
                        if (dy == -1) top = Math.Clamp(y + dy, 1, top);
                        else bottom = Math.Clamp(y + dy, bottom, Side - 2);
                        target[x + dx, y + dy] = neighbor with { Active = true };
                    }
                    double lengthFactor = dx != 0 && dy != 0 ? diagonal : 1d;
                    total += neighbor.Pressure;
                    forceX += neighbor.Pressure * (dx * lengthFactor);
                    forceY += neighbor.Pressure * (dy * lengthFactor);
                }
                double mean = total / 8d;
                if (mean <= cell.Resistance) continue;
                double force = Math.Sqrt(forceX * forceX + forceY * forceY) / 8d;
                double pressure = Math.Max(0, Math.Max(mean - force - cell.Pressure, 0) + force + cell.Pressure * .875 - cell.Resistance);
                target[x, y] = new(pressure, Math.Max(0, cell.Resistance - pressure * .02), true);
            }
            if (iteration < 2) target[Center, Center] = new(25, 0, true);
            (source, target) = (target, source);
        }
        return new(left, top, right - left + 1, bottom - top + 1);
    }

    private bool ShouldUseLava(int x, int y)
    {
        if (y <= lavaLine - 30) return false;
        for (int dx = -50; dx < 50; dx++)
        for (int dy = -50; dy < 50; dy++)
        {
            if (!Inside(x + dx, y + dy)) continue;
            WorldTile cell = At(x + dx, y + dy);
            if (cell.IsActive && cell.Type is 147 or 161 or 162 or 163 or 200) return false;
        }
        return true;
    }

    private void PlaceMaterial(int left, int top, WorldTileRegion area, bool lava)
    {
        for (int x = area.X; x < area.ExclusiveRight; x++)
        for (int y = area.Y; y < area.ExclusiveBottom; y++)
        {
            if (!Inside(left + x, top + y) || !source[x, y].Active) continue;
            Magma magma = source[x, y];
            ref WorldTile cell = ref At(left + x, top + y);
            double wave = Math.Sin((top + y) * .4) * .7 + 1.2;
            double threshold = .2 + .5 / Math.Sqrt(Math.Max(0, magma.Pressure - magma.Resistance));
            if (Math.Max(1 - Math.Max(0, wave * threshold), magma.Pressure / 15d) > .35 + (framing.Flat(left + x, top + y) ? 0 : .5))
                cell = new WorldTile { Type = StoneBiomeTiles1458.IsOre(cell.Type) ? cell.Type : (ushort)368, Wall = 180, Flags = WorldTileFlags.Active };
            else if (magma.Resistance < .01) { framing.ClearTile(left + x, top + y); cell.Wall = 180; }
            if (cell.LiquidAmount > 0 && lava) cell.LiquidKind = WorldLiquidKind.Lava;
        }
    }

    private void Cleanup(int left, int top, WorldTileRegion area)
    {
        var isolated = new List<WorldGenerationPoint>();
        for (int x = area.X; x < area.ExclusiveRight; x++)
        for (int y = area.Y; y < area.ExclusiveBottom; y++)
        {
            int tx = left + x, ty = top + y;
            if (!source[x, y].Active || !Inside(tx, ty) || !framing.Flat(tx, ty)) continue;
            int solids = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++) if (framing.Flat(tx + dx, ty + dy)) solids++;
            if (solids < 3) isolated.Add(new(tx, ty));
        }
        foreach (WorldGenerationPoint point in isolated)
        {
            framing.ClearTile(point.X, point.Y, true);
            At(point.X, point.Y).Wall = 180;
        }
    }

    private void Decorate(int left, int top, WorldTileRegion area)
    {
        var field = new GenerationFieldRandom1458(seed).WithModifier(65440);
        for (int x = area.X; x < area.ExclusiveRight; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = area.Y; y < area.ExclusiveBottom; y++)
            {
                int tx = left + x, ty = top + y;
                if (!Inside(tx, ty) || !source[x, y].Active) continue;
                framing.Frame(tx, ty);
                DesertSurface1458.FrameWalls(tiles, random, tx, ty);
                var decoration = field.WithCoordinates(tx, ty);
                if (decoration.Next(8) == 0 && At(tx, ty).IsActive)
                {
                    if (!At(tx, ty + 1).IsActive) framing.PlaceUncheckedTight(tx, ty + 1, decoration.Next(2) == 0, decoration.Next(3));
                    if (!At(tx, ty - 1).IsActive) framing.PlaceUncheckedTight(tx, ty - 1, decoration.Next(2) == 0, decoration.Next(3));
                }
                if (decoration.Next(2) == 0) framing.Smooth(tx, ty);
            }
        }
    }

    private bool Inside(int x, int y) => x >= 10 && y >= 10 && x < tiles.Dimensions.WidthTiles - 10 && y < tiles.Dimensions.HeightTiles - 10;
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
