using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Second source-backed Terraria 1.4.5.8 world-generation overlay. It extends the ordinary canonical pipeline from
/// the first Jungle pass through Slush, keeping the source catalog order and the same shared UnifiedRandom stream.
/// Aggregate compatibility biomes are reduced to an ocean-only residual and the old aggregate ore pass becomes a
/// dependency barrier after source-shaped Shinies has placed the pre-hardmode ore tiers.
/// </summary>
public sealed class SourceBackedMidPipeline1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId MudCavesToGrassId = new("terraria:1.4.5.8/MudCavesToGrass");
    internal static readonly WorldGenerationPassId FullDesertId = new("terraria:1.4.5.8/FullDesert");
    internal static readonly WorldGenerationPassId MushroomPatchesId = new("terraria:1.4.5.8/MushroomPatches");
    internal static readonly WorldGenerationPassId MarbleId = new("terraria:1.4.5.8/Marble");
    internal static readonly WorldGenerationPassId GraniteId = new("terraria:1.4.5.8/Granite");
    internal static readonly WorldGenerationPassId FloatingIslandsId = new("terraria:1.4.5.8/FloatingIslands");
    internal static readonly WorldGenerationPassId DirtToMudId = new("terraria:1.4.5.8/DirtToMud");
    internal static readonly WorldGenerationPassId SiltId = new("terraria:1.4.5.8/Silt");
    internal static readonly WorldGenerationPassId ShiniesId = new("terraria:1.4.5.8/Shinies");
    internal static readonly WorldGenerationPassId WebsId = new("terraria:1.4.5.8/Webs");
    internal static readonly WorldGenerationPassId UnderworldId = new("terraria:1.4.5.8/Underworld");
    internal static readonly WorldGenerationPassId CorruptionId = new("terraria:1.4.5.8/Corruption");
    internal static readonly WorldGenerationPassId LakesId = new("terraria:1.4.5.8/Lakes");
    internal static readonly WorldGenerationPassId SlushId = new("terraria:1.4.5.8/Slush");

    private static readonly WorldGenerationPassId JungleId = new("terraria:1.4.5.8/Jungle");
    private static readonly WorldGenerationPassId CompatibilityBiomesId = new("terraria:1.4.5.8/Biomes");
    private static readonly WorldGenerationPassId CompatibilityOresId = new("terraria:1.4.5.8/Ores");

    private readonly SourceBackedPipeline1458 baseline = new();

    public WorldGeneratorId Id => baseline.Id;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);

        WorldGenerationRequest requestCopy = request;
        VanillaWorldSeedProfile1458 profile = WorldSeedResolver1458.Resolve(in requestCopy);
        if (!profile.IsDefault || !TerrainPass1458.IsCanonicalWorldSize(request.WidthTiles, request.HeightTiles))
        {
            capture.Replay(builder);
            return;
        }

        CapturedPass residualBiomes = capture.Require(CompatibilityBiomesId);
        var state = new MidState1458();

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == CompatibilityBiomesId)
            {
                Add(builder, MudCavesToGrassId, JungleId,
                    new MidPass1458(MidStage1458.MudCavesToGrass, state));
                Add(builder, FullDesertId, MudCavesToGrassId,
                    new MidPass1458(MidStage1458.FullDesert, state));
                Add(builder, MushroomPatchesId, FullDesertId,
                    new MidPass1458(MidStage1458.MushroomPatches, state));
                Add(builder, MarbleId, MushroomPatchesId,
                    new MidPass1458(MidStage1458.Marble, state));
                Add(builder, GraniteId, MarbleId,
                    new MidPass1458(MidStage1458.Granite, state));
                Add(builder, FloatingIslandsId, GraniteId,
                    new MidPass1458(MidStage1458.FloatingIslands, state));
                Add(builder, DirtToMudId, FloatingIslandsId,
                    new MidPass1458(MidStage1458.DirtToMud, state));
                Add(builder, SiltId, DirtToMudId,
                    new MidPass1458(MidStage1458.Silt, state));
                Add(builder, ShiniesId, SiltId,
                    new MidPass1458(MidStage1458.Shinies, state));
                Add(builder, WebsId, ShiniesId,
                    new MidPass1458(MidStage1458.Webs, state));
                Add(builder, UnderworldId, WebsId,
                    new MidPass1458(MidStage1458.Underworld, state));
                Add(builder, CorruptionId, UnderworldId,
                    new MidPass1458(MidStage1458.Corruption, state));
                Add(builder, LakesId, CorruptionId,
                    new MidPass1458(MidStage1458.Lakes, state));
                Add(builder, SlushId, LakesId,
                    new MidPass1458(MidStage1458.Slush, state));

                builder.Add(
                    CloneDescriptor(residualBiomes.Descriptor, WorldGenerationRngMode.IsolatedDeterministic, [SlushId]),
                    new OceanResidualCompatibilityBiomesPass1458(residualBiomes.Pass));
                continue;
            }

            if (entry.Descriptor.Id == CompatibilityOresId)
            {
                builder.Add(entry.Descriptor, SourceBackedOreCompatibilityBarrier1458.Instance);
                continue;
            }

            builder.Add(entry.Descriptor, entry.Pass);
        }
    }

    private static void Add(
        IWorldGenerationPlanBuilder builder,
        WorldGenerationPassId id,
        WorldGenerationPassId after,
        IWorldGenerationPass pass) =>
        builder.Add(
            new WorldGenerationPassDescriptor(
                id,
                WorldGenerationRngMode.VanillaSharedRng,
                requiredAfter: [after]),
            pass);

    private static WorldGenerationPassDescriptor CloneDescriptor(
        WorldGenerationPassDescriptor source,
        WorldGenerationRngMode rngMode,
        WorldGenerationPassId[] requiredAfter) =>
        new(
            source.Id,
            rngMode,
            requiredAfter,
            source.OptionalAfter.ToArray(),
            source.OptionalBefore.ToArray());

    private readonly record struct CapturedPass(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly List<CapturedPass> entries = [];
        public IReadOnlyList<CapturedPass> Entries => entries;

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            entries.Add(new CapturedPass(descriptor, pass));

        public CapturedPass Require(WorldGenerationPassId id)
        {
            foreach (CapturedPass entry in entries)
            {
                if (entry.Descriptor.Id == id)
                    return entry;
            }

            throw new InvalidOperationException($"Baseline early vanilla plan did not expose required pass '{id}'.");
        }

        public void Replay(IWorldGenerationPlanBuilder builder)
        {
            foreach (CapturedPass entry in entries)
                builder.Add(entry.Descriptor, entry.Pass);
        }
    }
}

internal enum MidStage1458 : byte
{
    MudCavesToGrass,
    FullDesert,
    MushroomPatches,
    Marble,
    Granite,
    FloatingIslands,
    DirtToMud,
    Silt,
    Shinies,
    Webs,
    Underworld,
    Corruption,
    Lakes,
    Slush
}

internal sealed class MidState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; set; }
    public int DesertLeft { get; set; } = -1;
    public int DesertRight { get; set; } = -1;

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Mid vanilla world generation requires the Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Mid vanilla world generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Max((int)RockLayer + 180, workspace.HeightTiles - 200);
    }
}

internal sealed class MidPass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort CorruptGrass = 23;
    private const ushort Ebonstone = 25;
    private const ushort Cobweb = 51;
    private const ushort Sand = 53;
    private const ushort Ash = 57;
    private const ushort Hellstone = 58;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort MushroomGrass = 70;
    private const ushort Ebonsand = 112;
    private const ushort Silt = 123;
    private const ushort Snow = 147;
    private const ushort Ice = 161;
    private const ushort Cloud = 189;
    private const ushort CrimsonGrass = 199;
    private const ushort Crimstone = 203;
    private const ushort Slush = 224;
    private const ushort Crimsand = 234;
    private const ushort Marble = 367;
    private const ushort Granite = 368;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;

    private readonly MidStage1458 stage;
    private readonly MidState1458 state;

    public MidPass1458(
        MidStage1458 stage,
        MidState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Source-backed mid Terraria generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Source-backed mid Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case MidStage1458.MudCavesToGrass:
                ApplyMudCavesToGrass(context, grid, random);
                break;
            case MidStage1458.FullDesert:
                ApplyFullDesert(context, grid, random);
                break;
            case MidStage1458.MushroomPatches:
                ApplyMushroomPatches(context, grid, random);
                break;
            case MidStage1458.Marble:
                ApplyStoneMicroBiome(context, grid, random, Marble, "marble");
                break;
            case MidStage1458.Granite:
                ApplyStoneMicroBiome(context, grid, random, Granite, "granite");
                break;
            case MidStage1458.FloatingIslands:
                ApplyFloatingIslands(context, grid, random);
                break;
            case MidStage1458.DirtToMud:
                ApplyDirtToMud(context, grid, random);
                break;
            case MidStage1458.Silt:
                ApplySilt(context, grid, random);
                break;
            case MidStage1458.Shinies:
                ApplyShinies(context, grid, random);
                break;
            case MidStage1458.Webs:
                ApplyWebs(context, grid, random);
                break;
            case MidStage1458.Underworld:
                ApplyUnderworld(context, grid, random);
                break;
            case MidStage1458.Corruption:
                ApplyEvilBiome(context, grid, random);
                break;
            case MidStage1458.Lakes:
                ApplyLakes(context, grid, random);
                break;
            case MidStage1458.Slush:
                ApplySlush(context, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplyMudCavesToGrass(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int halfWidth = Math.Max(180, grid.Width / 9);
        int left = Math.Max(1, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 1, bootstrap.JungleOriginX + halfWidth);
        int top = Math.Clamp((int)state.WorldSurface, 2, grid.Height - 2);
        int bottom = Math.Min(state.UnderworldTop, grid.Height - 2);
        long converted = 0;

        for (int x = left; x < right; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = top; y < bottom; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || tile.Type != Mud || !grid.HasOpenNeighbor(x, y))
                    continue;
                if (random.Next(6) != 0)
                    continue;

                tile.Type = JungleGrass;
                tile.FrameX = -1;
                tile.FrameY = -1;
                converted++;
            }
        }

        context.ReportProgress(1d, $"Spreading jungle grass across mud cave surfaces ({converted} tiles)");
    }

    private void ApplyFullDesert(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int width = Math.Clamp((int)Math.Round(grid.Width * 0.12d), 360, 920);
        int left = PickDesertLeft(grid, random, bootstrap, width);
        int right = Math.Min(grid.Width - 1, left + width);
        state.DesertLeft = left;
        state.DesertRight = right;

        int maxDepth = Math.Min(state.UnderworldTop - 80, (int)state.RockLayer + Math.Max(180, grid.Height / 7));
        for (int x = left; x < right; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int surface = grid.FindFirstActiveY(x, 40, Math.Min(grid.Height, (int)state.RockLayer + 60));
            if (surface >= grid.Height)
                continue;

            double t = (x - left) / (double)Math.Max(1, right - left - 1);
            double envelope = Math.Sin(t * Math.PI);
            int depth = Math.Clamp(
                55 + (int)Math.Round(envelope * 150d) + random.Next(-8, 9),
                35,
                Math.Max(36, maxDepth - surface));

            int end = Math.Min(maxDepth, surface + depth);
            for (int y = surface; y < end; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive && y > surface + 10)
                    continue;

                ushort type = y < surface + 35
                    ? Sand
                    : y < surface + depth * 2 / 3
                        ? HardenedSand
                        : Sandstone;
                SetType(ref tile, type);
                tile.LiquidAmount = 0;
                tile.LiquidKind = WorldLiquidKind.Water;
            }
        }

        int chambers = Math.Max(5, width / 80);
        for (int i = 0; i < chambers; i++)
        {
            int cx = random.Next(left + 30, Math.Max(left + 31, right - 30));
            int cy = random.Next(
                Math.Min(state.UnderworldTop - 90, (int)state.RockLayer + 35),
                Math.Max(Math.Min(state.UnderworldTop - 89, (int)state.RockLayer + 36), state.UnderworldTop - 70));
            int rx = random.Next(18, 42);
            int ry = random.Next(7, 16);
            CarveEllipse(grid, cx, cy, rx, ry);
            if ((i & 3) == 0)
                CarveTunnel(grid, random, cx, cy, random.Next(35, 80), radius: random.Next(3, 7), downwardBias: 0.25d);
        }

        context.ReportProgress(1d, "Generating Terraria full desert shell and underground chambers");
    }

    private void ApplyMushroomPatches(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int count = Math.Max(4, grid.Width / 700);
        int top = Math.Clamp((int)state.RockLayer + 40, 10, state.UnderworldTop - 90);
        int bottom = Math.Max(top + 1, state.UnderworldTop - 50);

        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(120, grid.Width - 120);
            if (state.DesertLeft >= 0 && x >= state.DesertLeft - 80 && x <= state.DesertRight + 80)
            {
                i--;
                continue;
            }

            int y = random.Next(top, bottom);
            int radiusX = random.Next(24, 48);
            int radiusY = random.Next(12, 26);
            FillEllipse(grid, x, y, radiusX, radiusY, Mud, overwriteAir: false);
            CarveEllipse(grid, x, y - 1, Math.Max(5, radiusX - 8), Math.Max(4, radiusY - 7));
            ConvertExposedSurface(grid, x - radiusX - 2, x + radiusX + 2, y - radiusY - 3, y + radiusY + 3, Mud, MushroomGrass, random, 2);
        }

        context.ReportProgress(1d, "Generating glowing mushroom patches");
    }

    private void ApplyStoneMicroBiome(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IRandom random,
        ushort type,
        string name)
    {
        int count = Math.Max(5, grid.Width / 560);
        int top = Math.Clamp((int)state.RockLayer + 40, 20, state.UnderworldTop - 100);
        int bottom = Math.Max(top + 1, state.UnderworldTop - 70);

        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(100, grid.Width - 100);
            int y = random.Next(top, bottom);
            int radiusX = random.Next(18, 38);
            int radiusY = random.Next(12, 28);
            FillEllipse(grid, x, y, radiusX, radiusY, type, overwriteAir: false);
            CarveEllipse(grid, x + random.Next(-4, 5), y + random.Next(-2, 3),
                Math.Max(4, radiusX / 2), Math.Max(3, radiusY / 2));
        }

        context.ReportProgress(1d, $"Generating Terraria {name} micro-biomes");
    }

    private void ApplyFloatingIslands(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int count = grid.Width switch
        {
            <= 4200 => 3,
            <= 6400 => 5,
            _ => 7
        };
        int lakeBudget = Math.Clamp(bootstrap.SkyLakes, 0, count);
        var used = new List<int>(count);

        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = PickFloatingIslandX(grid, random, used);
            used.Add(x);
            int maxY = Math.Max(120, (int)state.WorldSurface - 90);
            int y = random.Next(85, maxY);
            int rx = random.Next(34, 62);
            int ry = random.Next(9, 17);

            FillEllipse(grid, x, y + 8, rx, ry, Cloud, overwriteAir: true);
            FillEllipse(grid, x, y, Math.Max(16, rx - 8), Math.Max(6, ry - 3), Dirt, overwriteAir: true);
            ConvertExposedSurface(grid, x - rx, x + rx, y - ry - 4, y + ry + 6, Dirt, Grass, random, 1);

            if (i < lakeBudget)
            {
                int lakeRx = Math.Max(8, rx / 3);
                int lakeRy = Math.Max(3, ry / 3);
                CarveEllipse(grid, x, y - 1, lakeRx, lakeRy);
                FillLiquidEllipse(grid, x, y, lakeRx - 1, Math.Max(2, lakeRy - 1), WorldLiquidKind.Water);
            }
        }

        context.ReportProgress(1d, "Generating floating islands and sky lakes");
    }

    private void ApplyDirtToMud(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int halfWidth = Math.Max(220, grid.Width / 8);
        int left = Math.Max(1, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 1, bootstrap.JungleOriginX + halfWidth);
        int top = Math.Clamp((int)state.RockLayer - 30, 1, state.UnderworldTop - 1);

        for (int x = left; x < right; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = top; y < state.UnderworldTop; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && tile.Type == Dirt && random.Next(5) == 0)
                {
                    tile.Type = Mud;
                    tile.FrameX = -1;
                    tile.FrameY = -1;
                }
            }
        }

        context.ReportProgress(1d, "Converting deep jungle dirt to mud");
    }

    private void ApplySilt(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        long area = (long)grid.Width * grid.Height;
        int count = Math.Max(40, (int)(area * 0.000045d));
        int top = Math.Clamp((int)state.RockLayer, 10, state.UnderworldTop - 40);

        for (int i = 0; i < count; i++)
        {
            if ((i & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            RunMaterialBlob(
                grid,
                random,
                random.Next(30, grid.Width - 30),
                random.Next(top, state.UnderworldTop),
                random.Next(3, 8),
                random.Next(4, 12),
                Silt);
        }

        context.ReportProgress(1d, "Generating silt deposits");
    }

    private void ApplyShinies(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        long area = (long)grid.Width * grid.Height;
        int surface = Math.Clamp((int)state.WorldSurface, 10, grid.Height - 10);
        int rock = Math.Clamp((int)state.RockLayer, surface + 1, state.UnderworldTop - 2);
        int deep = Math.Clamp((rock + state.UnderworldTop) / 2, rock + 1, state.UnderworldTop - 1);

        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.CopperOre), area, 0.000060d, surface, rock);
        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.CopperOre), area, 0.000080d, rock, deep);
        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.CopperOre), area, 0.000020d, deep, state.UnderworldTop);

        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.IronOre), area, 0.000030d, surface, rock);
        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.IronOre), area, 0.000080d, rock, deep);
        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.IronOre), area, 0.000200d, deep, state.UnderworldTop);

        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.SilverOre), area, 0.000026d, surface, rock);
        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.SilverOre), area, 0.000150d, rock, deep);
        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.SilverOre), area, 0.000170d, deep, state.UnderworldTop);

        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.GoldOre), area, 0.000120d, rock, deep);
        PlaceOreBand(context, grid, random, checked((ushort)bootstrap.GoldOre), area, 0.000120d, deep, state.UnderworldTop);

        ushort evilOre = context.Request.Options.Evil == WorldGenerationEvil.Crimson ? (ushort)204 : (ushort)22;
        PlaceOreBand(context, grid, random, evilOre, area, 0.0000225d, rock, state.UnderworldTop);

        context.ReportProgress(1d, "Generating source-shaped pre-hardmode ore tiers");
    }

    private static void PlaceOreBand(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IRandom random,
        ushort type,
        long area,
        double density,
        int minY,
        int maxY)
    {
        if (maxY <= minY + 1)
            return;

        int count = Math.Max(1, (int)(area * density));
        for (int i = 0; i < count; i++)
        {
            if ((i & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            RunOreBlob(
                grid,
                random,
                random.Next(10, grid.Width - 10),
                random.Next(minY, maxY),
                random.Next(3, 7),
                random.Next(3, 9),
                type);
        }
    }

    private void ApplyWebs(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int count = Math.Max(300, grid.Width / 2);
        int minY = Math.Clamp((int)state.WorldSurface + 45, 10, state.UnderworldTop - 70);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 40);

        for (int i = 0; i < count; i++)
        {
            if ((i & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(5, grid.Width - 5);
            int y = random.Next(minY, maxY);
            if (grid.At(x, y).IsActive || !grid.HasSolidNeighbor(x, y))
                continue;

            int radius = random.Next(2, 5);
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int tx = x + dx;
                    int ty = y + dy;
                    if (!grid.Contains(tx, ty) || dx * dx + dy * dy > radius * radius + random.Next(3))
                        continue;
                    ref WorldTile tile = ref grid.At(tx, ty);
                    if (tile.IsActive)
                        continue;
                    SetType(ref tile, Cobweb);
                }
            }
        }

        context.ReportProgress(1d, "Generating cave cobweb patches");
    }

    private void ApplyUnderworld(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        state.UnderworldTop = Math.Clamp(grid.Height - 200, (int)state.RockLayer + 120, grid.Height - 90);
        int roof = state.UnderworldTop + random.Next(20, 36);
        int floor = grid.Height - random.Next(42, 58);
        int roofVelocity = 0;
        int floorVelocity = 0;

        for (int x = 0; x < grid.Width; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            if (random.Next(3) == 0)
                roofVelocity = Math.Clamp(roofVelocity + random.Next(-1, 2), -2, 2);
            if (random.Next(3) == 0)
                floorVelocity = Math.Clamp(floorVelocity + random.Next(-1, 2), -2, 2);

            roof = Math.Clamp(roof + roofVelocity, state.UnderworldTop + 10, state.UnderworldTop + 65);
            floor = Math.Clamp(floor + floorVelocity, grid.Height - 72, grid.Height - 28);
            if (floor - roof < 60)
                floor = Math.Min(grid.Height - 28, roof + 60);

            for (int y = state.UnderworldTop - 10; y < roof; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && (tile.Type == Dirt || tile.Type == Stone || tile.Type == Mud))
                    tile.Type = Ash;
            }

            for (int y = roof; y < floor; y++)
                ClearTile(ref grid.At(x, y));

            for (int y = floor; y < grid.Height; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                SetType(ref tile, Ash);
                tile.LiquidAmount = 0;
                tile.LiquidKind = WorldLiquidKind.Water;
            }

            if ((x % 19) < 8)
            {
                int lavaTop = Math.Max(roof + 20, floor - random.Next(7, 15));
                for (int y = lavaTop; y < floor; y++)
                {
                    ref WorldTile tile = ref grid.At(x, y);
                    ClearTile(ref tile);
                    tile.LiquidAmount = byte.MaxValue;
                    tile.LiquidKind = WorldLiquidKind.Lava;
                }
            }
        }

        int hellstoneRuns = Math.Max(120, grid.Width / 15);
        for (int i = 0; i < hellstoneRuns; i++)
        {
            RunOreBlob(
                grid,
                random,
                random.Next(15, grid.Width - 15),
                random.Next(state.UnderworldTop + 15, grid.Height - 18),
                random.Next(3, 8),
                random.Next(4, 10),
                Hellstone);
        }

        context.ReportProgress(1d, "Generating underworld ash caverns, lava and hellstone");
    }

    private void ApplyEvilBiome(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int biomeCount = Math.Max(1, (int)Math.Round(grid.Width * 0.00045d));
        bool crimson = context.Request.Options.Evil == WorldGenerationEvil.Crimson;
        ushort evilGrass = crimson ? CrimsonGrass : CorruptGrass;
        ushort evilStone = crimson ? Crimstone : Ebonstone;
        ushort evilSand = crimson ? Crimsand : Ebonsand;

        for (int biome = 0; biome < biomeCount; biome++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int center = PickEvilCenter(grid, random, bootstrap);
            int halfWidth = random.Next(85, 145);
            int left = Math.Max(5, center - halfWidth);
            int right = Math.Min(grid.Width - 5, center + halfWidth);

            for (int x = left; x < right; x++)
            {
                int surface = grid.FindFirstActiveY(x, 30, Math.Min(grid.Height, (int)state.RockLayer + 100));
                int bottom = Math.Min(state.UnderworldTop - 30, (int)state.RockLayer + 260);
                for (int y = surface; y < bottom; y++)
                {
                    ref WorldTile tile = ref grid.At(x, y);
                    if (!tile.IsActive)
                        continue;

                    tile.Type = tile.Type switch
                    {
                        Grass => evilGrass,
                        Stone => evilStone,
                        Sand => evilSand,
                        HardenedSand => evilSand,
                        Sandstone => evilStone,
                        _ => tile.Type
                    };
                }
            }

            int chasms = crimson ? random.Next(3, 6) : random.Next(2, 5);
            for (int i = 0; i < chasms; i++)
            {
                int x = random.Next(left + 8, Math.Max(left + 9, right - 8));
                int y = grid.FindFirstActiveY(x, 20, Math.Min(grid.Height, (int)state.RockLayer));
                CarveEvilChasm(grid, random, x, Math.Max(10, y - 2), evilStone);
            }
        }

        context.ReportProgress(1d, crimson
            ? "Generating crimson surface conversion and chasms"
            : "Generating corruption surface conversion and chasms");
    }

    private void ApplyLakes(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int count = Math.Max(4, grid.Width / 700 + 2);
        int minY = Math.Clamp((int)state.WorldSurface + 25, 20, state.UnderworldTop - 100);
        int maxY = Math.Clamp((int)state.RockLayer + 120, minY + 1, state.UnderworldTop - 60);

        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(120, grid.Width - 120);
            if (state.DesertLeft >= 0 && x >= state.DesertLeft - 40 && x <= state.DesertRight + 40)
            {
                i--;
                continue;
            }

            int y = random.Next(minY, maxY);
            int rx = random.Next(14, 30);
            int ry = random.Next(5, 12);
            CarveEllipse(grid, x, y, rx, ry);
            FillLiquidEllipse(grid, x, y + Math.Max(1, ry / 3), rx - 2, Math.Max(2, ry - 2), WorldLiquidKind.Water);
        }

        context.ReportProgress(1d, "Generating underground lakes");
    }

    private void ApplySlush(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int left = Math.Max(10, bootstrap.SnowOriginLeft - 80);
        int right = Math.Min(grid.Width - 10, bootstrap.SnowOriginRight + 80);
        int minY = Math.Clamp((int)state.RockLayer, 20, state.UnderworldTop - 80);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 50);
        int count = Math.Max(50, (right - left) / 2);

        for (int i = 0; i < count; i++)
        {
            if ((i & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            RunSlushBlob(
                grid,
                random,
                random.Next(left, right),
                random.Next(minY, maxY),
                random.Next(3, 7),
                random.Next(4, 10));
        }

        context.ReportProgress(1d, "Generating slush deposits in the ice biome");
    }

    private int PickDesertLeft(
        RuntimeGrid grid,
        IRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap,
        int width)
    {
        int min = Math.Max(bootstrap.LeftBeachEnd + 180, 280);
        int max = Math.Min(bootstrap.RightBeachStart - width - 180, grid.Width - width - 280);
        if (max <= min)
            return Math.Clamp(grid.Width / 2 - width / 2, 20, grid.Width - width - 20);

        for (int attempt = 0; attempt < 4000; attempt++)
        {
            int left = random.Next(min, max + 1);
            int right = left + width;
            int center = left + width / 2;
            if (Math.Abs(center - grid.Width / 2) < 360)
                continue;
            if (Math.Abs(center - bootstrap.JungleOriginX) < width / 2 + 420)
                continue;
            if (right >= bootstrap.SnowOriginLeft - 220 && left <= bootstrap.SnowOriginRight + 220)
                continue;
            if (Math.Abs(center - bootstrap.DungeonLocation) < width / 2 + 220)
                continue;
            return left;
        }

        int fallbackCenter = bootstrap.JungleOriginX < grid.Width / 2
            ? (int)(grid.Width * 0.72d)
            : (int)(grid.Width * 0.28d);
        return Math.Clamp(fallbackCenter - width / 2, min, max);
    }

    private static int PickFloatingIslandX(RuntimeGrid grid, IRandom random, List<int> used)
    {
        for (int attempt = 0; attempt < 2000; attempt++)
        {
            int x = random.Next(180, grid.Width - 180);
            if (Math.Abs(x - grid.Width / 2) < 220)
                continue;
            bool close = false;
            foreach (int previous in used)
            {
                if (Math.Abs(previous - x) < 260)
                {
                    close = true;
                    break;
                }
            }
            if (!close)
                return x;
        }

        return Math.Clamp((used.Count + 1) * grid.Width / (used.Count + 2), 180, grid.Width - 180);
    }

    private int PickEvilCenter(RuntimeGrid grid, IRandom random, VanillaWorldGenerationBootstrapState1458 bootstrap)
    {
        for (int attempt = 0; attempt < 8000; attempt++)
        {
            int x = random.Next(bootstrap.LeftBeachEnd + 180, bootstrap.RightBeachStart - 180);
            if (Math.Abs(x - grid.Width / 2) < 240)
                continue;
            if (Math.Abs(x - bootstrap.JungleOriginX) < 480)
                continue;
            if (x >= bootstrap.SnowOriginLeft - 220 && x <= bootstrap.SnowOriginRight + 220)
                continue;
            if (Math.Abs(x - bootstrap.DungeonLocation) < 260)
                continue;
            if (state.DesertLeft >= 0 && x >= state.DesertLeft - 180 && x <= state.DesertRight + 180)
                continue;
            return x;
        }

        return bootstrap.JungleOriginX < grid.Width / 2
            ? Math.Clamp((int)(grid.Width * 0.68d), bootstrap.LeftBeachEnd + 180, bootstrap.RightBeachStart - 180)
            : Math.Clamp((int)(grid.Width * 0.32d), bootstrap.LeftBeachEnd + 180, bootstrap.RightBeachStart - 180);
    }

    private void CarveEvilChasm(RuntimeGrid grid, IRandom random, int startX, int startY, ushort evilStone)
    {
        double x = startX;
        double y = startY;
        double vx = random.Next(-10, 11) * 0.04d;
        int bottom = Math.Min(state.UnderworldTop - 50, (int)state.RockLayer + random.Next(150, 300));

        while (y < bottom)
        {
            int radius = random.Next(3, 7);
            int cx = (int)x;
            int cy = (int)y;
            for (int dx = -radius - 2; dx <= radius + 2; dx++)
            {
                for (int dy = -radius - 2; dy <= radius + 2; dy++)
                {
                    int tx = cx + dx;
                    int ty = cy + dy;
                    if (!grid.Contains(tx, ty))
                        continue;
                    int distance = dx * dx + dy * dy;
                    ref WorldTile tile = ref grid.At(tx, ty);
                    if (distance <= radius * radius)
                        ClearTile(ref tile);
                    else if (distance <= (radius + 2) * (radius + 2) && tile.IsActive &&
                             tile.Type is Dirt or Stone or Grass or Sand)
                        tile.Type = evilStone;
                }
            }

            x += vx;
            y += 1d;
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.015d, -0.7d, 0.7d);
            x = Math.Clamp(x, 8d, grid.Width - 9d);
        }
    }

    private static void CarveTunnel(
        RuntimeGrid grid,
        IRandom random,
        int startX,
        int startY,
        int steps,
        int radius,
        double downwardBias)
    {
        double x = startX;
        double y = startY;
        double vx = random.Next(-10, 11) * 0.08d;
        double vy = downwardBias + random.Next(-5, 6) * 0.04d;

        for (int step = 0; step < steps; step++)
        {
            ClearCircle(grid, (int)x, (int)y, radius);
            x = Math.Clamp(x + vx, radius + 1, grid.Width - radius - 2);
            y = Math.Clamp(y + vy, radius + 1, grid.Height - radius - 2);
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.02d, -1.1d, 1.1d);
            vy = Math.Clamp(vy + random.Next(-10, 11) * 0.02d, -0.4d, 1.3d);
        }
    }

    private static void RunMaterialBlob(
        RuntimeGrid grid,
        IRandom random,
        int startX,
        int startY,
        int strength,
        int steps,
        ushort type)
    {
        double x = startX;
        double y = startY;
        double vx = random.Next(-10, 11) * 0.08d;
        double vy = random.Next(-10, 11) * 0.08d;

        for (int step = 0; step < steps; step++)
        {
            double scale = 1d - step / (double)Math.Max(1, steps);
            int radius = Math.Max(1, (int)Math.Round(strength * scale));
            PaintCircle(grid, (int)x, (int)y, radius, type, onlyReplaceNatural: true);
            x = Math.Clamp(x + vx, 2d, grid.Width - 3d);
            y = Math.Clamp(y + vy, 2d, grid.Height - 3d);
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.02d, -1d, 1d);
            vy = Math.Clamp(vy + random.Next(-10, 11) * 0.02d, -1d, 1d);
        }
    }

    private static void RunOreBlob(
        RuntimeGrid grid,
        IRandom random,
        int startX,
        int startY,
        int strength,
        int steps,
        ushort type)
    {
        double x = startX;
        double y = startY;
        double vx = random.Next(-10, 11) * 0.06d;
        double vy = random.Next(-10, 11) * 0.06d;

        for (int step = 0; step < steps; step++)
        {
            double scale = 1d - step / (double)Math.Max(1, steps);
            int radius = Math.Max(1, (int)Math.Round(strength * scale));
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx * dx + dy * dy > radius * radius + random.Next(3))
                        continue;
                    int tx = (int)x + dx;
                    int ty = (int)y + dy;
                    if (!grid.Contains(tx, ty))
                        continue;
                    ref WorldTile tile = ref grid.At(tx, ty);
                    if (!tile.IsActive || !IsOreReplaceable(tile.Type))
                        continue;
                    tile.Type = type;
                    tile.FrameX = -1;
                    tile.FrameY = -1;
                }
            }

            x = Math.Clamp(x + vx, 2d, grid.Width - 3d);
            y = Math.Clamp(y + vy, 2d, grid.Height - 3d);
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.02d, -1d, 1d);
            vy = Math.Clamp(vy + random.Next(-10, 11) * 0.02d, -1d, 1d);
        }
    }

    private static void RunSlushBlob(
        RuntimeGrid grid,
        IRandom random,
        int startX,
        int startY,
        int strength,
        int steps)
    {
        double x = startX;
        double y = startY;
        for (int step = 0; step < steps; step++)
        {
            int radius = Math.Max(1, strength - step / 2);
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int tx = (int)x + dx;
                    int ty = (int)y + dy;
                    if (!grid.Contains(tx, ty) || dx * dx + dy * dy > radius * radius + random.Next(3))
                        continue;
                    ref WorldTile tile = ref grid.At(tx, ty);
                    if (tile.IsActive && tile.Type is Snow or Ice or Dirt or Stone)
                    {
                        tile.Type = Slush;
                        tile.FrameX = -1;
                        tile.FrameY = -1;
                    }
                }
            }

            x = Math.Clamp(x + random.Next(-1, 2), 2d, grid.Width - 3d);
            y = Math.Clamp(y + random.Next(-1, 2), 2d, grid.Height - 3d);
        }
    }

    private static void FillEllipse(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        ushort type,
        bool overwriteAir)
    {
        radiusX = Math.Max(1, radiusX);
        radiusY = Math.Max(1, radiusY);
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)radiusX;
            for (int dy = -radiusY; dy <= radiusY; dy++)
            {
                double ny = dy / (double)radiusY;
                if (nx * nx + ny * ny > 1d)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (!overwriteAir && !tile.IsActive)
                    continue;
                SetType(ref tile, type);
                tile.LiquidAmount = 0;
                tile.LiquidKind = WorldLiquidKind.Water;
            }
        }
    }

    private static void CarveEllipse(RuntimeGrid grid, int centerX, int centerY, int radiusX, int radiusY)
    {
        radiusX = Math.Max(1, radiusX);
        radiusY = Math.Max(1, radiusY);
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)radiusX;
            for (int dy = -radiusY; dy <= radiusY; dy++)
            {
                double ny = dy / (double)radiusY;
                if (nx * nx + ny * ny > 1d)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (grid.Contains(x, y))
                    ClearTile(ref grid.At(x, y));
            }
        }
    }

    private static void FillLiquidEllipse(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        WorldLiquidKind liquid)
    {
        radiusX = Math.Max(1, radiusX);
        radiusY = Math.Max(1, radiusY);
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)radiusX;
            for (int dy = 0; dy <= radiusY; dy++)
            {
                double ny = dy / (double)radiusY;
                if (nx * nx + ny * ny > 1d)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive)
                    continue;
                tile.LiquidAmount = byte.MaxValue;
                tile.LiquidKind = liquid;
            }
        }
    }

    private static void ConvertExposedSurface(
        RuntimeGrid grid,
        int minX,
        int maxX,
        int minY,
        int maxY,
        ushort from,
        ushort to,
        IRandom random,
        int chanceDivisor)
    {
        int left = Math.Max(1, minX);
        int right = Math.Min(grid.Width - 1, maxX);
        int top = Math.Max(1, minY);
        int bottom = Math.Min(grid.Height - 1, maxY);
        for (int x = left; x < right; x++)
        {
            for (int y = top; y < bottom; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || tile.Type != from || !grid.HasOpenNeighbor(x, y))
                    continue;
                if (chanceDivisor > 1 && random.Next(chanceDivisor) != 0)
                    continue;
                tile.Type = to;
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
        }
    }

    private static void PaintCircle(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radius,
        ushort type,
        bool onlyReplaceNatural)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive)
                    continue;
                if (onlyReplaceNatural && !IsNaturalReplaceable(tile.Type))
                    continue;
                tile.Type = type;
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
        }
    }

    private static void ClearCircle(RuntimeGrid grid, int centerX, int centerY, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (grid.Contains(x, y))
                    ClearTile(ref grid.At(x, y));
            }
        }
    }

    private static bool IsNaturalReplaceable(ushort type) =>
        type is Dirt or Stone or Sand or Mud or Snow or Ice or HardenedSand or Sandstone or Ash;

    private static bool IsOreReplaceable(ushort type) =>
        IsNaturalReplaceable(type) || type is Marble or Granite or Silt;

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Mid vanilla pass executed before bootstrap state initialization.");

    private static void SetType(ref WorldTile tile, ushort type)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
    }

    private static void ClearTile(ref WorldTile tile)
    {
        tile.Flags &= ~WorldTileFlags.Active;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
    }

    private interface IRandom
    {
        int Next();
        int Next(int max);
        int Next(int min, int max);
        double NextDouble();
    }

    private sealed class VanillaRandom(IWorldGenerationVanillaRandom inner) : IRandom
    {
        public int Next() => inner.Next();
        public int Next(int max) => inner.Next(max);
        public int Next(int min, int max) => inner.Next(min, max);
        public double NextDouble() => inner.NextDouble();
    }

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;

        public RuntimeGrid(Workspace workspace) => store = workspace.TileStore;

        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;

        public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

        public int FindFirstActiveY(int x, int minY, int maxExclusive)
        {
            int max = Math.Min(Height, maxExclusive);
            for (int y = Math.Max(0, minY); y < max; y++)
            {
                if (At(x, y).IsActive)
                    return y;
            }

            return max;
        }

        public bool HasOpenNeighbor(int x, int y) =>
            !At(x - 1, y).IsActive ||
            !At(x + 1, y).IsActive ||
            !At(x, y - 1).IsActive ||
            !At(x, y + 1).IsActive;

        public bool HasSolidNeighbor(int x, int y) =>
            At(x - 1, y).IsActive ||
            At(x + 1, y).IsActive ||
            At(x, y - 1).IsActive ||
            At(x, y + 1).IsActive;
    }
}

/// <summary>
/// Leaves the compatibility-biome aggregate alive only as a temporary source for the ocean body. Interior biome
/// writes and underworld writes are acknowledged but discarded so source-backed Jungle/Desert/evil/Underworld state
/// cannot be painted over by the older compatibility approximation.
/// </summary>
internal sealed class OceanResidualCompatibilityBiomesPass1458 : IWorldGenerationPass
{
    private readonly IWorldGenerationPass inner;

    public OceanResidualCompatibilityBiomesPass1458(IWorldGenerationPass inner) =>
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public void Execute(IWorldGenerationContext context)
    {
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Ocean residual requires Workspace.");
        VanillaWorldGenerationBootstrapState1458 bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Ocean residual requires Reset beach bounds.");
        var filtered = new OceanOnlyWorkspace(context.Workspace, bootstrap);
        inner.Execute(new ResidualContext(context, filtered));
    }

    private sealed class ResidualContext(IWorldGenerationContext parent, IWorldGenerationWorkspace workspace) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => parent.Request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace? Metadata => parent.Metadata;
        public IWorldGenerationRandom Random => parent.Random;
        public IWorldGenerationVanillaRandom? VanillaRandom => parent.VanillaRandom;
        public CancellationToken CancellationToken => parent.CancellationToken;
        public void ReportProgress(double fraction, string? message = null) =>
            parent.ReportProgress(fraction, "Applying compatibility ocean residual");
    }

    private sealed class OceanOnlyWorkspace(
        IWorldGenerationWorkspace inner,
        VanillaWorldGenerationBootstrapState1458 bootstrap) : IWorldGenerationWorkspace
    {
        private readonly int verticalLimit = (int)(inner.HeightTiles * 0.70d);
        private readonly int leftLimit = Math.Min(inner.WidthTiles, bootstrap.LeftBeachEnd + 96);
        private readonly int rightLimit = Math.Max(0, bootstrap.RightBeachStart - 96);

        public int WidthTiles => inner.WidthTiles;
        public int HeightTiles => inner.HeightTiles;

        public bool TryGetTile(int x, int y, out WorldGenerationTile tile) =>
            inner.TryGetTile(x, y, out tile);

        public bool TrySetTile(int x, int y, in WorldGenerationTile tile)
        {
            bool oceanBand = y < verticalLimit && (x < leftLimit || x >= rightLimit);
            if (!oceanBand)
                return true;
            return inner.TrySetTile(x, y, in tile);
        }
    }
}

/// <summary>
/// Keeps the aggregate compatibility Ores identity in the dependency graph after the source-shaped Shinies pass has
/// placed the Reset-selected ore tiers. The barrier intentionally consumes no RNG and performs no tile writes.
/// </summary>
internal sealed class SourceBackedOreCompatibilityBarrier1458 : IWorldGenerationPass
{
    public static SourceBackedOreCompatibilityBarrier1458 Instance { get; } = new();

    private SourceBackedOreCompatibilityBarrier1458()
    {
    }

    public void Execute(IWorldGenerationContext context) =>
        context.ReportProgress(1d, "Compatibility Ores replaced by source-shaped Shinies");
}
