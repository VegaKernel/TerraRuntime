using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Eleventh source-backed Terraria 1.4.5.8 world-generation overlay. It advances ordinary canonical generation from
/// Gems In Ice Biome through Larva. This block owns underground gem scatter, exposed moss continuation, Jungle cave
/// walls and correctly framed 3x3 Larva objects inside existing Hive regions. Micro Biomes remains the next boundary.
/// </summary>
public sealed class SourceBackedUndergroundFinish1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId GemsInIceBiomeId = new("terraria:1.4.5.8/GemsInIceBiome");
    internal static readonly WorldGenerationPassId RandomGemsId = new("terraria:1.4.5.8/RandomGems");
    internal static readonly WorldGenerationPassId MossGrassId = new("terraria:1.4.5.8/MossGrass");
    internal static readonly WorldGenerationPassId MudsWallsInJungleId = new("terraria:1.4.5.8/MudsWallsInJungle");
    internal static readonly WorldGenerationPassId LarvaId = new("terraria:1.4.5.8/Larva");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedVegetation1458 baseline = new();

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

        var state = new UndergroundFinishState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, GemsInIceBiomeId, SourceBackedVegetation1458.MushroomsId,
                new UndergroundFinishPass1458(UndergroundFinishStage1458.GemsInIceBiome, state));
            Add(builder, RandomGemsId, GemsInIceBiomeId,
                new UndergroundFinishPass1458(UndergroundFinishStage1458.RandomGems, state));
            Add(builder, MossGrassId, RandomGemsId,
                new UndergroundFinishPass1458(UndergroundFinishStage1458.MossGrass, state));
            Add(builder, MudsWallsInJungleId, MossGrassId,
                new UndergroundFinishPass1458(UndergroundFinishStage1458.MudsWallsInJungle, state));
            Add(builder, LarvaId, MudsWallsInJungleId,
                new UndergroundFinishPass1458(UndergroundFinishStage1458.Larva, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [LarvaId]), entry.Pass);
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
        WorldGenerationPassId[] requiredAfter) =>
        new(source.Id, source.RngMode, requiredAfter, source.OptionalAfter.ToArray(), source.OptionalBefore.ToArray());

    private readonly record struct CapturedPass(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly List<CapturedPass> entries = [];
        public IReadOnlyList<CapturedPass> Entries => entries;
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            entries.Add(new CapturedPass(descriptor, pass));
        public void Replay(IWorldGenerationPlanBuilder builder)
        {
            foreach (CapturedPass entry in entries)
                builder.Add(entry.Descriptor, entry.Pass);
        }
    }
}

internal enum UndergroundFinishStage1458 : byte
{
    GemsInIceBiome,
    RandomGems,
    MossGrass,
    MudsWallsInJungle,
    Larva
}

internal sealed class UndergroundFinishState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double RockLayer { get; private set; }
    public double WorldSurface { get; private set; }
    public int UnderworldTop { get; private set; }

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;
        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Underground-finish generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Underground-finish generation requires source-backed Terrain layers.");
        RockLayer = layers.RockLayer;
        WorldSurface = layers.WorldSurface;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class UndergroundFinishPass1458 : IWorldGenerationPass
{
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort IceBlock = 161;
    private const ushort GreenMoss = 179;
    private const ushort BrownMoss = 180;
    private const ushort RedMoss = 181;
    private const ushort BlueMoss = 182;
    private const ushort PurpleMoss = 183;
    private const ushort Hive = 225;
    private const ushort Larva = 231;

    private const ushort MudUnsafeWall = 15;
    private const ushort HiveUnsafeWall = 86;

    private readonly UndergroundFinishStage1458 stage;
    private readonly UndergroundFinishState1458 state;

    public UndergroundFinishPass1458(
        UndergroundFinishStage1458 stage,
        UndergroundFinishState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Underground-finish generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Underground-finish generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case UndergroundFinishStage1458.GemsInIceBiome:
                ApplyGemsInIceBiome(context, grid, random);
                break;
            case UndergroundFinishStage1458.RandomGems:
                ApplyRandomGems(context, grid, random);
                break;
            case UndergroundFinishStage1458.MossGrass:
                ApplyMossGrass(context, workspace);
                break;
            case UndergroundFinishStage1458.MudsWallsInJungle:
                ApplyMudsWallsInJungle(context, grid, random);
                break;
            case UndergroundFinishStage1458.Larva:
                ApplyLarva(context, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplyGemsInIceBiome(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        ReadOnlySpan<VanillaSnowRow1458> snowRows = (context.Workspace as Workspace ??
            throw new InvalidOperationException("Gems In Ice Biome requires Workspace.")).VanillaSnowRows;
        int minimumY = checked((int)(state.WorldSurface + state.RockLayer) / 2);
        int maximumY = RequireLavaLine(context, grid);
        int placed = 0;

        // TerrariaServer 1.4.5.8 WorldGen.AddPasses / ExposedGemsInIceBiome: each offer first samples
        // a row, then that row's retained snow interval. Geometry/style rolls happen only after an Ice-family
        // candidate is accepted; moving them outside this gate changes the shared generation RNG stream.
        for (int offer = 0; offer < grid.Width * 0.25d; offer++)
        {
            if ((offer & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int y = random.Next(minimumY, maximumY);
            VanillaSnowRow1458 row = snowRows[y];
            int x = random.Next(row.Left, row.Right);
            if (!grid.At(x, y).IsActive || grid.At(x, y).Type is not (147 or IceBlock or 162 or 224))
                continue;

            int left = random.Next(1, 4);
            int right = random.Next(1, 4);
            int up = random.Next(1, 4);
            int down = random.Next(1, 4);
            int style = SelectGemStyle(random.Next(12));
            for (int tx = x - left; tx < x + right; tx++)
            for (int ty = y - up; ty < y + down; ty++)
            {
                if (!grid.ContainsWithMargin(tx, ty, 40) || grid.At(tx, ty).IsActive)
                    continue;
                if (TryPlaceLooseGem(grid, random, tx, ty, style)) placed++;
            }
        }

        context.ReportProgress(1d, $"Placing source-backed exposed gems in the Ice biome ({placed} objects)");
    }

    private void ApplyRandomGems(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int placed = 0;
        int lavaLine = RequireLavaLine(context, grid);
        for (int offer = 0; offer < grid.Width; offer++)
        {
            if ((offer & 511) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(20, grid.Width - 20);
            int y = random.Next((int)state.RockLayer, grid.Height - 300);
            WorldTile tile = grid.At(x, y);
            if (tile.IsActive || HasLava(in tile) || DungeonGenerationTiles1458.IsDungeonWall(tile.Wall) || tile.Wall == 27)
                continue;
            if (TryPlaceLooseGem(grid, random, x, y, SelectGemStyle(random.Next(12)))) placed++;
        }

        for (int offer = 0; offer < grid.Width; offer++)
        {
            if ((offer & 511) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(20, grid.Width - 20);
            int y = random.Next((int)state.WorldSurface, grid.Height - 300);
            WorldTile tile = grid.At(x, y);
            if (tile.IsActive || HasLava(in tile) || tile.Wall is not (216 or 187))
                continue;
            int left = random.Next(1, 4);
            int right = random.Next(1, 4);
            int up = random.Next(1, 4);
            int down = random.Next(1, 4);
            for (int tx = x - left; tx < x + right; tx++)
            for (int ty = y - up; ty < y + down; ty++)
                if (!grid.At(tx, ty).IsActive && TryPlaceLooseGem(grid, random, tx, ty, 6)) placed++;
        }

        context.ReportProgress(1d, $"Placing source-backed random exposed gems ({placed} objects)");
    }

    private static int RequireLavaLine(IWorldGenerationContext context, RuntimeGrid grid)
    {
        int lavaLine = (context.Workspace as Workspace)?.VanillaLiquidLines?.LavaLine
            ?? throw new InvalidOperationException("Exposed gems require source-backed liquid lines.");
        return lavaLine > 0 && lavaLine < grid.Height ? lavaLine : throw new InvalidOperationException("Invalid source-backed lava line.");
    }

    private static int SelectGemStyle(int roll) => roll switch { < 3 => 0, < 6 => 1, < 8 => 2, < 10 => 3, 10 => 4, _ => 5 };

    private static bool HasLava(in WorldTile tile) => tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Lava;

    private static bool TryPlaceLooseGem(RuntimeGrid grid, IRandom random, int x, int y, int style)
    {
        if (!grid.HasLooseGemAnchor(x, y)) return false;
        ref WorldTile tile = ref grid.At(x, y);
        tile.Type = 178;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = (short)(style * 18);
        tile.FrameY = (short)(random.Next(3) * 18);
        return true;
    }

    private void ApplyMossGrass(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Moss Grass requires shared UnifiedRandom semantics.");

        // The strand's appearance and its survival are both decided by the ordinary SquareTileFrame that
        // PlaceTile triggers - twice per placement - so the pass is handed the real framer rather than a
        // biome-specific one.
        var framing = new GenerationTileFraming1458(workspace.TileStore, random);
        var pass = new LongMossPass1458(workspace.TileStore, random, framing, context.CancellationToken);
        pass.Apply();
        context.ReportProgress(1d, $"Growing Moss Grass ({pass.Placed} strands)");
    }

    private void ApplyMudsWallsInJungle(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        // TerrariaServer 1.4.5.8 WorldGen.AddPasses,
        // DirtWallsIntoMudWallsInJungleAndJungleMinMax: locate the extreme surface Jungle Grass columns,
        // then replace only dirt/unsafe-dirt walls.  The two edge conditions deliberately precede the wall
        // test: their short-circuited random draws are part of the shared generation stream even for other walls.
        int minJungleX = 0;
        int maxJungleX = 0;
        int scanEnd = Math.Min(grid.Height, (int)state.WorldSurface + 20);

        bool found = false;
        for (int x = 5; x < grid.Width - 5 && !found; x++)
        {
            for (int y = 0; y < scanEnd; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (!tile.IsActive || tile.Type != JungleGrass)
                    continue;
                minJungleX = x;
                found = true;
                break;
            }
        }

        found = false;
        for (int x = grid.Width - 5; x > 5 && !found; x--)
        {
            for (int y = 0; y < scanEnd; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (!tile.IsActive || tile.Type != JungleGrass)
                    continue;
                maxJungleX = x;
                found = true;
                break;
            }
        }

        int painted = 0;
        for (int x = minJungleX; x <= maxJungleX; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < scanEnd; y++)
            {
                bool interiorTwo = x >= minJungleX + 2 && x <= maxJungleX - 2;
                bool interiorThree = x >= minJungleX + 3 && x <= maxJungleX - 3;
                if ((interiorTwo || random.Next(2) != 0) &&
                    (interiorThree || random.Next(3) != 0) &&
                    (grid.At(x, y).Wall is 2 or 59))
                {
                    grid.At(x, y).Wall = MudUnsafeWall;
                    painted++;
                }
            }
        }

        context.ReportProgress(1d, $"Converting Jungle dirt walls ({painted} cells)");
    }

    private void ApplyLarva(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch { <= 4200 => 4, <= 6400 => 6, _ => 8 };
        int placed = 0;
        var candidates = new List<(int X, int Y)>();

        // Larva belongs inside Bee Hives. Find air pockets carrying Hive wall and surrounded by Hive material, then
        // place the complete 3x3 frame-important object rather than emitting an orphan anchor tile.
        for (int x = 5; x < grid.Width - 7; x += 2)
        {
            if ((x & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = Math.Max(20, (int)state.RockLayer); y < state.UnderworldTop - 8; y += 2)
            {
                if (grid.At(x, y).Wall != HiveUnsafeWall || !grid.IsEmptyRectangle(x, y, 3, 3))
                    continue;
                if (!grid.HasHiveShellNearby(x + 1, y + 1, 5, 5))
                    continue;
                candidates.Add((x, y));
            }
        }

        while (candidates.Count > 0 && placed < target)
        {
            int index = random.Next(candidates.Count);
            (int left, int top) = candidates[index];
            candidates.RemoveAt(index);
            if (!CanPlaceLarva(grid, left, top))
                continue;

            for (int dx = 0; dx < 3; dx++)
            for (int dy = 0; dy < 3; dy++)
            {
                ref WorldTile tile = ref grid.At(left + dx, top + dy);
                SetFramedTile(ref tile, Larva, dx * 18, dy * 18);
                tile.Wall = HiveUnsafeWall;
            }
            placed++;
        }

        context.ReportProgress(1d, $"Placing complete Hive Larva objects ({placed}/{target})");
    }

    private static bool CanPlaceLarva(RuntimeGrid grid, int left, int top)
    {
        if (!grid.IsEmptyRectangle(left, top, 3, 3))
            return false;
        for (int x = left - 1; x <= left + 3; x++)
        {
            for (int y = top - 1; y <= top + 3; y++)
            {
                if (!grid.Contains(x, y))
                    return false;
                WorldTile tile = grid.At(x, y);
                if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    return false;
            }
        }
        return grid.HasHiveShellNearby(left + 1, top + 1, 5, 5);
    }

    private static void SetFramedTile(ref WorldTile tile, ushort type, int frameX, int frameY)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = checked((short)frameX);
        tile.FrameY = checked((short)frameY);
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private interface IRandom
    {
        int Next(int max);
        int Next(int min, int max);
    }

    private sealed class VanillaRandom(IWorldGenerationVanillaRandom inner) : IRandom
    {
        public int Next(int max) => inner.Next(max);
        public int Next(int min, int max) => inner.Next(min, max);
    }

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;
        public RuntimeGrid(Workspace workspace) => store = workspace.TileStore;
        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;
        public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
        public bool ContainsWithMargin(int x, int y, int margin) =>
            x >= margin && x < Width - margin && y >= margin && y < Height - margin;
        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

        public bool HasLooseGemAnchor(int x, int y) =>
            IsLooseGemAnchor(in At(x, y + 1)) || IsLooseGemAnchor(in At(x - 1, y)) ||
            IsLooseGemAnchor(in At(x + 1, y)) || IsLooseGemAnchor(in At(x, y - 1));

        public bool IsEmptyRectangle(int left, int top, int width, int height)
        {
            if (left < 1 || top < 1 || left + width >= Width - 1 || top + height >= Height - 1)
                return false;
            for (int x = left; x < left + width; x++)
            for (int y = top; y < top + height; y++)
            {
                WorldTile tile = At(x, y);
                if (tile.IsActive || tile.LiquidAmount != 0)
                    return false;
            }
            return true;
        }

        public bool HasHiveShellNearby(int centerX, int centerY, int radiusX, int radiusY)
        {
            int hive = 0;
            int hiveWall = 0;
            int left = Math.Max(1, centerX - radiusX);
            int right = Math.Min(Width - 2, centerX + radiusX);
            int top = Math.Max(1, centerY - radiusY);
            int bottom = Math.Min(Height - 2, centerY + radiusY);
            for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                WorldTile tile = At(x, y);
                if (tile.IsActive && tile.Type == Hive)
                    hive++;
                if (tile.Wall == HiveUnsafeWall)
                    hiveWall++;
            }
            return hive >= 3 && hiveWall >= 9;
        }

        private static bool IsLooseGemAnchor(in WorldTile tile) =>
            tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
            VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }
}
