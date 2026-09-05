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
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class UndergroundFinishPass1458 : IWorldGenerationPass
{
    private const ushort Stone = 1;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Sapphire = 63;
    private const ushort Ruby = 64;
    private const ushort Emerald = 65;
    private const ushort Topaz = 66;
    private const ushort Amethyst = 67;
    private const ushort Diamond = 68;
    private const ushort IceBlock = 161;
    private const ushort GreenMoss = 179;
    private const ushort BrownMoss = 180;
    private const ushort RedMoss = 181;
    private const ushort BlueMoss = 182;
    private const ushort PurpleMoss = 183;
    private const ushort MossGrowth = 184;
    private const ushort Hive = 225;
    private const ushort Larva = 231;

    private const ushort MudUnsafeWall = 15;
    private const ushort JungleUnsafeWall = 64;
    private const ushort HiveUnsafeWall = 86;

    private static readonly ushort[] GemTiles = [Sapphire, Ruby, Emerald, Topaz, Amethyst, Diamond];
    private static readonly ushort[] MossTiles = [GreenMoss, BrownMoss, RedMoss, BlueMoss, PurpleMoss];

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
                ApplyMossGrass(context, grid, random);
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
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int left = Math.Max(20, bootstrap.SnowOriginLeft - 70);
        int right = Math.Min(grid.Width - 20, bootstrap.SnowOriginRight + 70);
        int minY = Math.Clamp((int)state.RockLayer - 30, 30, state.UnderworldTop - 120);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 60);
        int target = grid.Width switch { <= 4200 => 40, <= 6400 => 58, _ => 76 };
        int converted = 0;

        for (int cluster = 0; cluster < target; cluster++)
        {
            if ((cluster & 7) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int cx = random.Next(left, right);
            int cy = random.Next(minY, maxY);
            int radius = random.Next(2, 5);
            ushort gem = GemTiles[random.Next(GemTiles.Length)];

            for (int x = cx - radius; x <= cx + radius; x++)
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                if (!grid.Contains(x, y))
                    continue;
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy > radius * radius + random.Next(3))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || tile.Type != IceBlock)
                    continue;
                tile.Type = gem;
                tile.FrameX = 0;
                tile.FrameY = 0;
                tile.Shape = 0;
                converted++;
            }
        }

        context.ReportProgress(1d, $"Seeding gems in the Ice biome ({converted} blocks)");
    }

    private void ApplyRandomGems(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int attempts = grid.Width switch { <= 4200 => 1700, <= 6400 => 2500, _ => 3300 };
        int minY = Math.Clamp((int)state.RockLayer - 20, 25, state.UnderworldTop - 120);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 45);
        int converted = 0;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if ((attempt & 511) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(3, grid.Width - 3);
            int y = random.Next(minY, maxY);
            ref WorldTile tile = ref grid.At(x, y);
            if (!tile.IsActive || tile.Type != Stone || !grid.HasOpenNeighbor(x, y))
                continue;
            tile.Type = GemTiles[random.Next(GemTiles.Length)];
            tile.FrameX = 0;
            tile.FrameY = 0;
            tile.Shape = 0;
            converted++;
        }

        context.ReportProgress(1d, $"Scattering random exposed gems ({converted} blocks)");
    }

    private void ApplyMossGrass(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int attempts = grid.Width * 4;
        int minY = Math.Clamp((int)state.RockLayer - 20, 20, state.UnderworldTop - 100);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 25);
        int moss = 0;
        int growth = 0;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if ((attempt & 1023) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(2, grid.Width - 2);
            int y = random.Next(minY, maxY);
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.IsActive && tile.Type == Stone && grid.HasOpenNeighbor(x, y) && random.Next(3) == 0)
            {
                tile.Type = MossTiles[random.Next(MossTiles.Length)];
                tile.FrameX = 0;
                tile.FrameY = 0;
                tile.Shape = 0;
                moss++;
                continue;
            }

            if (tile.IsActive || tile.LiquidAmount != 0 || !grid.TryGetAdjacentMoss(x, y, out ushort adjacentMoss))
                continue;
            int style = Array.IndexOf(MossTiles, adjacentMoss);
            if (style < 0)
                continue;
            SetFramedTile(ref tile, MossGrowth, style * 18, 0);
            growth++;
        }

        context.ReportProgress(1d, $"Extending moss grass and growth ({moss} moss, {growth} growth)");
    }

    private void ApplyMudsWallsInJungle(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int halfWidth = Math.Max(280, grid.Width / 9);
        int left = Math.Max(10, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 10, bootstrap.JungleOriginX + halfWidth);
        int minY = Math.Clamp((int)state.RockLayer - 40, 20, state.UnderworldTop - 100);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 25);
        int painted = 0;

        for (int x = left; x < right; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = minY; y < maxY; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive || tile.Wall != 0)
                    continue;
                if (!grid.HasNeighborMaterial(x, y, Mud, JungleGrass) || random.Next(5) != 0)
                    continue;
                tile.Wall = random.Next(4) == 0 ? JungleUnsafeWall : MudUnsafeWall;
                painted++;
            }
        }

        context.ReportProgress(1d, $"Adding Mud/Jungle cave walls ({painted} cells)");
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

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Underground-finish pass executed before bootstrap initialization.");

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
        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

        public bool HasOpenNeighbor(int x, int y) =>
            !At(x - 1, y).IsActive || !At(x + 1, y).IsActive || !At(x, y - 1).IsActive || !At(x, y + 1).IsActive;

        public bool HasNeighborMaterial(int x, int y, ushort a, ushort b)
        {
            WorldTile left = At(x - 1, y);
            WorldTile right = At(x + 1, y);
            WorldTile up = At(x, y - 1);
            WorldTile down = At(x, y + 1);
            return IsMaterial(left, a, b) || IsMaterial(right, a, b) || IsMaterial(up, a, b) || IsMaterial(down, a, b);
        }

        public bool TryGetAdjacentMoss(int x, int y, out ushort moss)
        {
            ushort[] candidates = [At(x - 1, y).Type, At(x + 1, y).Type, At(x, y - 1).Type, At(x, y + 1).Type];
            foreach (ushort type in candidates)
            {
                if (type is >= GreenMoss and <= PurpleMoss)
                {
                    moss = type;
                    return true;
                }
            }
            moss = 0;
            return false;
        }

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

        private static bool IsMaterial(WorldTile tile, ushort a, ushort b) =>
            tile.IsActive && (tile.Type == a || tile.Type == b);
    }
}
