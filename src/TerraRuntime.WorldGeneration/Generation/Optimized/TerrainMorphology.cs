using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Optimized;

/// <summary>
/// Deterministic large-scale terrain morphology for <c>terraruntime:optimized</c>. The baseline terrain pass owns the
/// canonical surface/material bootstrap; this layer deforms only natural inland columns after biome painting and
/// before cave carving. It combines domain-warped fractal noise, ridged multifractal detail and bounded tectonic-style
/// uplift/basin/mesa fields while preserving protected anchor columns used by later progression placement.
/// </summary>
internal static class TerrainMorphology
{
    internal const int AlgorithmVersion = 2;

    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort CorruptGrass = 23;
    private const ushort Ebonstone = 25;
    private const ushort Sand = 53;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Snow = 147;
    private const ushort Ice = 161;
    private const ushort CrimsonGrass = 199;
    private const ushort Crimstone = 203;

    private const ulong WarpSeed = 0x4D4F525048574152UL;
    private const ulong MacroSeed = 0x4D4F5250484D4143UL;
    private const ulong RidgeSeed = 0x4D4F525048524944UL;
    private const ulong LandformSeed = 0x4D4F5250484C414EUL;

    internal readonly record struct ProfileMetrics(
        int MinimumDelta,
        int MaximumDelta,
        int MaximumAdjacentStep,
        int DirectionChanges,
        int FlatRunColumns,
        ulong Fingerprint)
    {
        public int Relief => MaximumDelta - MinimumDelta;
    }

    public static void Apply(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        int baseSurface = CalculateBaseSurface(height);
        int rockLayer = CalculateRockLayer(height, baseSurface);
        int maxRelief = CalculateMaximumRelief(height);
        int searchTop = Math.Max(2, baseSurface - maxRelief - 32);
        int searchBottom = Math.Min(height - 3, baseSurface + maxRelief + 52);

        var source = new int[width];
        for (int x = 0; x < width; x++)
        {
            if ((x & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            source[x] = FindNaturalSurfaceY(context.Workspace, x, searchTop, searchBottom);
        }

        int[] target = BuildTargetSurfaceProfile(context.Request.Seed, width, height, source);
        int changedColumns = 0;
        int movedTiles = 0;
        for (int x = 0; x < width; x++)
        {
            if ((x & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int fromY = source[x];
            int toY = target[x];
            if (fromY < 0 || toY < 0 || fromY == toY)
                continue;
            if (toY >= rockLayer - 8)
                continue;
            if (!TryReadNaturalColumnFamily(context.Workspace, x, fromY, out ushort topType, out ushort bodyType))
                continue;
            if (!CanMoveColumn(context.Workspace, x, fromY, toY))
                continue;

            if (toY < fromY)
            {
                for (int y = toY; y < fromY; y++)
                {
                    SetNaturalTile(
                        context.Workspace,
                        x,
                        y,
                        y == toY ? topType : bodyType);
                    movedTiles++;
                }

                SetNaturalTile(context.Workspace, x, fromY, bodyType);
            }
            else
            {
                for (int y = fromY; y < toY; y++)
                {
                    ClearSurfaceTile(context.Workspace, x, y);
                    movedTiles++;
                }

                SetNaturalTile(context.Workspace, x, toY, topType);
            }

            changedColumns++;
        }

        ProfileMetrics metrics = AnalyzeProfile(source, target);
        if (changedColumns == 0 || metrics.MinimumDelta >= 0 || metrics.MaximumDelta <= 0)
        {
            throw new InvalidOperationException(
                "Optimized terrain morphology produced no usable uplift/basin deformation.");
        }

        context.ReportProgress(
            1d,
            $"Applied terrain morphology v{AlgorithmVersion}: columns={changedColumns}, tiles={movedTiles}, " +
            $"relief={metrics.Relief}, turns={metrics.DirectionChanges}");
    }

    internal static int[] BuildTargetSurfaceProfile(
        ulong seed,
        int width,
        int height,
        ReadOnlySpan<int> sourceSurface)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (sourceSurface.Length != width)
            throw new ArgumentException("Source surface width does not match the requested morphology width.", nameof(sourceSurface));

        int[] target = sourceSurface.ToArray();
        bool[] locked = new bool[width];
        int baseSurface = CalculateBaseSurface(height);
        int rockLayer = CalculateRockLayer(height, baseSurface);
        int oceanWidth = Math.Clamp(width / 12, 48, 360);
        int oceanFade = Math.Clamp(width / 36, 20, 150);
        int spawnHalfWidth = Math.Clamp(width / 28, 18, 110);
        int spawnCenter = width / 2;
        int spawnFade = Math.Clamp(width / 90, 12, 64);
        int evilCenter = CalculateEvilCenter(seed, width);
        int anchorRadius = Math.Clamp(width / 260, 5, 22);
        int anchorFade = Math.Clamp(width / 120, 10, 48);
        int maxRelief = CalculateMaximumRelief(height);
        double warpScale = Math.Clamp(width / 18d, 46d, 360d);
        double warpAmount = Math.Clamp(width / 82d, 12d, 92d);
        double macroScale = Math.Clamp(width / 14d, 78d, 520d);
        double ridgeScale = Math.Clamp(width / 31d, 42d, 240d);
        int landformCount = Math.Clamp(width / 850 + 4, 4, 13);
        int minimumSurface = Math.Max(28, baseSurface - maxRelief - 18);
        int maximumSurface = Math.Min(rockLayer - 16, baseSurface + maxRelief + 18);

        for (int x = 0; x < width; x++)
        {
            int sourceY = sourceSurface[x];
            if (sourceY < 0)
            {
                locked[x] = true;
                continue;
            }

            double edgeMask = EdgeLandMask(x, width, oceanWidth, oceanFade);
            double spawnMask = AnchorMask(x, spawnCenter, spawnHalfWidth, spawnFade);
            double evilMask = AnchorMask(x, evilCenter, anchorRadius, anchorFade);
            double deformationMask = edgeMask * spawnMask * evilMask;
            if (deformationMask <= 0.0001d)
            {
                locked[x] = true;
                target[x] = sourceY;
                continue;
            }

            double warp = FractalNoise1D(seed ^ WarpSeed, x, warpScale, 3) * warpAmount;
            double warpedX = x + warp;
            double macro = FractalNoise1D(seed ^ MacroSeed, warpedX, macroScale, 4);
            double ridge = RidgedFractal1D(seed ^ RidgeSeed, warpedX, ridgeScale, 4);
            double ridgeGate = SmoothStep((FractalNoise1D(seed ^ (RidgeSeed >> 1), warpedX, macroScale * 0.72d, 2) + 1d) * 0.5d);

            double delta = macro * maxRelief * 0.24d;
            delta -= (ridge - 0.46d) * ridgeGate * maxRelief * 0.34d;
            delta += EvaluateLandforms(seed, warpedX, width, maxRelief, landformCount);
            delta *= deformationMask;
            delta = Math.Clamp(delta, -maxRelief, maxRelief);
            target[x] = Math.Clamp(sourceY + (int)Math.Round(delta), minimumSurface, maximumSurface);
        }

        LimitSurfaceSlope(target, sourceSurface, locked, maximumStep: 2);
        return target;
    }

    internal static ProfileMetrics AnalyzeProfile(ReadOnlySpan<int> source, ReadOnlySpan<int> target)
    {
        if (source.Length != target.Length)
            throw new ArgumentException("Profile lengths must match.", nameof(target));

        int minDelta = int.MaxValue;
        int maxDelta = int.MinValue;
        int maximumStep = 0;
        int directionChanges = 0;
        int flatRunColumns = 0;
        int previousDirection = 0;
        ulong fingerprint = 1469598103934665603UL;

        for (int i = 0; i < target.Length; i++)
        {
            int delta = source[i] < 0 || target[i] < 0 ? 0 : target[i] - source[i];
            minDelta = Math.Min(minDelta, delta);
            maxDelta = Math.Max(maxDelta, delta);
            fingerprint ^= unchecked((ulong)(uint)(target[i] + 0x10000));
            fingerprint *= 1099511628211UL;

            if (i == 0 || target[i] < 0 || target[i - 1] < 0)
                continue;

            int step = target[i] - target[i - 1];
            maximumStep = Math.Max(maximumStep, Math.Abs(step));
            if (step == 0)
            {
                flatRunColumns++;
                continue;
            }

            int direction = Math.Sign(step);
            if (previousDirection != 0 && direction != previousDirection)
                directionChanges++;
            previousDirection = direction;
        }

        if (minDelta == int.MaxValue)
            minDelta = 0;
        if (maxDelta == int.MinValue)
            maxDelta = 0;

        return new ProfileMetrics(minDelta, maxDelta, maximumStep, directionChanges, flatRunColumns, fingerprint);
    }

    private static double EvaluateLandforms(
        ulong seed,
        double x,
        int width,
        int maxRelief,
        int count)
    {
        double sum = 0d;
        int oceanWidth = Math.Clamp(width / 12, 48, 360);
        int interiorLeft = Math.Min(width - 1, oceanWidth + 12);
        int interiorRight = Math.Max(interiorLeft + 1, width - oceanWidth - 13);
        int rotation = (int)(Hash01(seed ^ LandformSeed, 0) * 4d) & 3;

        for (int i = 0; i < count; i++)
        {
            ulong featureSeed = seed ^ LandformSeed ^ unchecked((ulong)i * 0x9E3779B97F4A7C15UL);
            double center = interiorLeft + Hash01(featureSeed, 1) * Math.Max(1, interiorRight - interiorLeft);
            double radius = Math.Clamp(
                width * (0.055d + Hash01(featureSeed, 2) * 0.095d),
                48d,
                680d);
            double distance = Math.Abs(x - center) / radius;
            if (distance >= 1d)
                continue;

            double envelope = SmoothStep(1d - distance);
            double amplitude = maxRelief * (0.46d + Hash01(featureSeed, 3) * 0.48d);
            int kind = (i + rotation) & 3;
            switch (kind)
            {
                case 0: // compression ridge / mountain chain
                {
                    double localRidge = RidgedFractal1D(featureSeed ^ RidgeSeed, x, Math.Max(28d, radius * 0.32d), 3);
                    sum -= amplitude * envelope * (0.58d + localRidge * 0.42d);
                    break;
                }
                case 1: // extensional basin / rift
                {
                    double rift = 0.72d + Math.Abs(ValueNoise1D(featureSeed ^ MacroSeed, x / Math.Max(18d, radius * 0.21d))) * 0.28d;
                    sum += amplitude * 0.86d * envelope * rift;
                    break;
                }
                case 2: // mesa / plateau with a broad flat crown and softened escarpment
                {
                    double crown = distance <= 0.38d
                        ? 1d
                        : 1d - SmoothStep((distance - 0.38d) / 0.62d);
                    sum -= amplitude * 0.68d * crown;
                    break;
                }
                default: // saddle/highland, intentionally asymmetric through the warped sample
                {
                    double asymmetry = 0.65d + 0.35d * ValueNoise1D(featureSeed ^ WarpSeed, x / Math.Max(24d, radius * 0.27d));
                    sum -= amplitude * 0.38d * envelope * asymmetry;
                    break;
                }
            }
        }

        return sum * 0.72d;
    }

    private static void LimitSurfaceSlope(
        Span<int> target,
        ReadOnlySpan<int> source,
        ReadOnlySpan<bool> locked,
        int maximumStep)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (int x = 1; x < target.Length; x++)
            {
                if (locked[x] || target[x] < 0 || target[x - 1] < 0)
                    continue;
                target[x] = Math.Clamp(target[x], target[x - 1] - maximumStep, target[x - 1] + maximumStep);
            }

            for (int x = target.Length - 2; x >= 0; x--)
            {
                if (locked[x] || target[x] < 0 || target[x + 1] < 0)
                    continue;
                target[x] = Math.Clamp(target[x], target[x + 1] - maximumStep, target[x + 1] + maximumStep);
            }
        }

        for (int x = 0; x < target.Length; x++)
        {
            if (locked[x] && source[x] >= 0)
                target[x] = source[x];
        }
    }

    private static int FindNaturalSurfaceY(
        IWorldGenerationWorkspace workspace,
        int x,
        int startInclusive,
        int endInclusive)
    {
        for (int y = startInclusive; y <= endInclusive; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0 ||
                tile.LiquidAmount != 0)
            {
                continue;
            }

            return IsNaturalTerrain(tile.Type) ? y : -1;
        }

        return -1;
    }

    private static bool TryReadNaturalColumnFamily(
        IWorldGenerationWorkspace workspace,
        int x,
        int surfaceY,
        out ushort topType,
        out ushort bodyType)
    {
        topType = 0;
        bodyType = 0;
        if (!workspace.TryGetTile(x, surfaceY, out WorldGenerationTile surface) ||
            (surface.Flags & WorldGenerationTileFlags.Active) == 0 ||
            surface.LiquidAmount != 0 ||
            !IsNaturalTerrain(surface.Type))
        {
            return false;
        }

        topType = surface.Type;
        bodyType = InferBodyType(topType);
        for (int y = surfaceY + 1; y <= Math.Min(workspace.HeightTiles - 1, surfaceY + 5); y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile below) ||
                (below.Flags & WorldGenerationTileFlags.Active) == 0 ||
                !IsNaturalTerrain(below.Type))
            {
                continue;
            }

            bodyType = below.Type;
            if (bodyType != topType || y == surfaceY + 5)
                break;
        }

        return true;
    }

    private static bool CanMoveColumn(
        IWorldGenerationWorkspace workspace,
        int x,
        int fromY,
        int toY)
    {
        int top = Math.Min(fromY, toY);
        int bottom = Math.Max(fromY, toY);
        for (int y = Math.Max(1, top - 1); y <= Math.Min(workspace.HeightTiles - 2, bottom + 1); y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                return false;
            if (tile.LiquidAmount != 0)
                return false;
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 && !IsNaturalTerrain(tile.Type))
                return false;
        }

        return true;
    }

    private static void SetNaturalTile(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort type)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile current))
            throw new InvalidOperationException($"Optimized terrain morphology could not read tile ({x},{y}).");

        var tile = new WorldGenerationTile(
            Type: type,
            Wall: current.Wall,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.Active,
            LiquidAmount: 0,
            TileColor: current.TileColor,
            WallColor: current.WallColor,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized terrain morphology could not write tile ({x},{y}).");
    }

    private static void ClearSurfaceTile(IWorldGenerationWorkspace workspace, int x, int y)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile current))
            throw new InvalidOperationException($"Optimized terrain morphology could not read tile ({x},{y}).");

        var tile = new WorldGenerationTile(
            Type: 0,
            Wall: 0,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.None,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized terrain morphology could not clear tile ({x},{y}).");
    }

    private static bool IsNaturalTerrain(ushort type) =>
        type is Dirt or Stone or Grass or CorruptGrass or Ebonstone or Sand or Mud or JungleGrass or Snow or Ice or CrimsonGrass or Crimstone;

    private static ushort InferBodyType(ushort topType) => topType switch
    {
        Grass => Dirt,
        CorruptGrass => Ebonstone,
        JungleGrass => Mud,
        Snow => Ice,
        CrimsonGrass => Crimstone,
        _ => topType
    };

    private static int CalculateBaseSurface(int height) =>
        Math.Clamp((int)Math.Round(height * 0.30d), 64, height - 150);

    private static int CalculateRockLayer(int height, int baseSurface) =>
        Math.Clamp((int)Math.Round(height * 0.52d), baseSurface + 40, height - 90);

    private static int CalculateMaximumRelief(int height) =>
        Math.Clamp(height / 14, 10, 42);

    private static int CalculateEvilCenter(ulong seed, int width)
    {
        bool jungleOnRight = (seed & 1UL) == 0UL;
        double start = jungleOnRight ? 0.61d : 0.27d;
        double end = jungleOnRight ? 0.73d : 0.39d;
        int left = Math.Clamp((int)Math.Round(width * start), 1, width - 2);
        int right = Math.Clamp((int)Math.Round(width * end), left, width - 2);
        return left + (right - left + 1) / 2;
    }

    private static double EdgeLandMask(int x, int width, int oceanWidth, int fade)
    {
        int distance = Math.Min(x - oceanWidth, width - oceanWidth - 1 - x);
        if (distance <= 0)
            return 0d;
        return SmoothStep(distance / (double)Math.Max(1, fade));
    }

    private static double AnchorMask(int x, int center, int radius, int fade)
    {
        int distance = Math.Abs(x - center);
        if (distance <= radius)
            return 0d;
        return SmoothStep((distance - radius) / (double)Math.Max(1, fade));
    }

    private static double RidgedFractal1D(ulong seed, double position, double baseScale, int octaves)
    {
        double value = 0d;
        double amplitude = 1d;
        double total = 0d;
        double scale = baseScale;
        double weight = 1d;

        for (int octave = 0; octave < octaves; octave++)
        {
            double sample = 1d - Math.Abs(ValueNoise1D(seed + (ulong)octave * 0x9E3779B97F4A7C15UL, position / scale));
            sample *= sample;
            sample *= weight;
            weight = Math.Clamp(sample * 1.85d, 0.15d, 1d);
            value += sample * amplitude;
            total += amplitude;
            amplitude *= 0.52d;
            scale *= 0.51d;
        }

        return total == 0d ? 0d : value / total;
    }

    private static double FractalNoise1D(ulong seed, double position, double baseScale, int octaves)
    {
        double value = 0d;
        double amplitude = 1d;
        double total = 0d;
        double scale = baseScale;
        for (int octave = 0; octave < octaves; octave++)
        {
            value += ValueNoise1D(seed + (ulong)octave * 0x9E3779B97F4A7C15UL, position / scale) * amplitude;
            total += amplitude;
            amplitude *= 0.5d;
            scale *= 0.5d;
        }

        return total == 0d ? 0d : value / total;
    }

    private static double ValueNoise1D(ulong seed, double position)
    {
        int left = (int)Math.Floor(position);
        int right = left + 1;
        double fraction = position - left;
        double t = fraction * fraction * (3d - 2d * fraction);
        double a = HashSigned(seed, left);
        double b = HashSigned(seed, right);
        return a + (b - a) * t;
    }

    private static double HashSigned(ulong seed, int coordinate) =>
        Hash01(seed, coordinate) * 2d - 1d;

    private static double Hash01(ulong seed, int coordinate)
    {
        ulong value = unchecked(seed ^ (unchecked((ulong)(long)coordinate) * 0x9E3779B97F4A7C15UL));
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 11) * (1d / (1UL << 53));
    }

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value * value * (3d - 2d * value);
    }
}
