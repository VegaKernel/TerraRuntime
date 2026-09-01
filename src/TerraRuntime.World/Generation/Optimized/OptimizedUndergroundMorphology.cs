using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Deterministic multi-family underground morphology for <c>terraruntime:optimized</c>. The pass deliberately mixes
/// three different mathematical families instead of scaling one random walker: warped SDF "cheese" caverns, long
/// curl-noise streamlines ("spaghetti"), and narrow correlated "noodles". Sparse quadratic connectors join the large
/// chambers into an exploration backbone. Every tile mutation is bounded by the base generator's reserved-region mask.
/// </summary>
internal static partial class OptimizedUndergroundMorphology
{
    internal const int AlgorithmVersion = 2;

    private const ulong CheeseSeed = 0x4348454553455632UL;
    private const ulong SpaghettiSeed = 0x5350414748455432UL;
    private const ulong NoodleSeed = 0x4E4F4F444C455632UL;
    private const ulong ConnectorSeed = 0x434F4E4E45435632UL;
    private const ulong WarpSeed = 0x5741525055565732UL;

    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort CorruptGrass = 23;
    private const ushort Ebonstone = 25;
    private const ushort Sand = 53;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort MushroomGrass = 70;
    private const ushort Snow = 147;
    private const ushort Ice = 161;
    private const ushort CrimsonGrass = 199;
    private const ushort Crimstone = 203;

    internal readonly record struct PlanMetrics(
        int CheeseCaverns,
        int SpaghettiTunnels,
        int NoodleTunnels,
        int ConnectorTunnels,
        int HorizontalSectors,
        int VerticalBands,
        int MinimumY,
        int MaximumY,
        ulong Fingerprint)
    {
        public int TotalFeatures => CheeseCaverns + SpaghettiTunnels + NoodleTunnels + ConnectorTunnels;
    }

    internal readonly record struct Report(
        int CheeseCaverns,
        int SpaghettiTunnels,
        int NoodleTunnels,
        int ConnectorTunnels,
        int CarvedTiles,
        int TouchedSectors,
        int UpperBandCarvedTiles,
        int DeepBandCarvedTiles,
        ulong PlanFingerprint)
    {
        public int TotalFeatures => CheeseCaverns + SpaghettiTunnels + NoodleTunnels + ConnectorTunnels;
    }

    private readonly record struct Rect(int Left, int Top, int Right, int Bottom)
    {
        public bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    private sealed class ReservedMask(Rect[] regions)
    {
        private readonly Rect[] regions = regions;

        public bool Contains(int x, int y)
        {
            foreach (Rect region in regions)
            {
                if (region.Contains(x, y))
                    return true;
            }
            return false;
        }

        public static ReservedMask Create(
            ulong seed,
            int width,
            int height,
            int baseSurface,
            int rockLayer,
            int underworldTop,
            int oceanWidth)
        {
            bool jungleOnRight = (seed & 1UL) == 0UL;
            (int Left, int Right) leftCold = Band(width, 0.12d, 0.24d);
            (int Left, int Right) rightJungle = Band(width, 0.76d, 0.88d);
            (int Left, int Right) snow = jungleOnRight ? leftCold : rightJungle;
            (int Left, int Right) jungle = jungleOnRight ? rightJungle : leftCold;

            int spawnHalfWidth = Math.Clamp(width / 28, 18, 110);
            var spawn = new Rect(width / 2 - spawnHalfWidth, baseSurface - 20, width / 2 + spawnHalfWidth, baseSurface + 36);

            int dungeonWidth = Math.Clamp(width / 18, 38, 160);
            int dungeonCenter = snow.Left + (snow.Right - snow.Left + 1) / 2;
            int dungeonX = Math.Clamp(dungeonCenter, oceanWidth + dungeonWidth, width - oceanWidth - dungeonWidth - 1);
            int dungeonTop = Math.Clamp(baseSurface - 12, 24, height - 160);
            int dungeonBottom = Math.Clamp((int)Math.Round(height * 0.72d), dungeonTop + 80, underworldTop - 20);
            var dungeon = new Rect(dungeonX - dungeonWidth / 2, dungeonTop, dungeonX + dungeonWidth / 2, dungeonBottom);

            int jungleCenter = jungle.Left + (jungle.Right - jungle.Left + 1) / 2;
            int templeWidth = Math.Clamp(width / 28, 34, 120);
            int templeHeight = Math.Clamp(height / 15, 24, 70);
            int templeTop = Math.Clamp((int)Math.Round(height * 0.58d), rockLayer + 16, underworldTop - templeHeight - 16);
            var temple = new Rect(jungleCenter - templeWidth / 2, templeTop, jungleCenter + templeWidth / 2, templeTop + templeHeight);

            int hiveWidth = Math.Clamp(width / 45, 24, 72);
            int hiveHeight = Math.Clamp(height / 28, 16, 44);
            int jungleWidth = jungle.Right - jungle.Left + 1;
            int hiveCenterX = Math.Clamp(
                jungleCenter - Math.Max(hiveWidth, jungleWidth / 4),
                jungle.Left + hiveWidth / 2 + 2,
                jungle.Right - hiveWidth / 2 - 2);
            int hiveTop = Math.Clamp((int)Math.Round(height * 0.43d), baseSurface + 30, temple.Top - hiveHeight - 12);
            var hive = new Rect(hiveCenterX - hiveWidth / 2, hiveTop, hiveCenterX + hiveWidth / 2, hiveTop + hiveHeight);

            int shimmerWidth = Math.Clamp(width / 55, 20, 64);
            int shimmerHeight = Math.Clamp(height / 34, 12, 34);
            int shimmerTop = Math.Clamp(temple.Bottom + 10, rockLayer + 12, underworldTop - shimmerHeight - 6);
            var shimmer = new Rect(jungleCenter - shimmerWidth / 2, shimmerTop, jungleCenter + shimmerWidth / 2, shimmerTop + shimmerHeight);

            return new ReservedMask([spawn, dungeon, temple, hive, shimmer]);
        }

        private static (int Left, int Right) Band(int width, double start, double end)
        {
            int left = Math.Clamp((int)Math.Round(width * start), 1, width - 2);
            int right = Math.Clamp((int)Math.Round(width * end), left, width - 2);
            return (left, right);
        }
    }

    private readonly record struct CheeseSpec(
        ulong Seed,
        double X,
        double Y,
        double RadiusX,
        double RadiusY,
        double Rotation);

    private readonly record struct TunnelSpec(
        ulong Seed,
        double X,
        double Y,
        double DirectionX,
        double DirectionY,
        int Steps,
        double Radius,
        double Phase);

    private sealed class FeaturePlan(
        int minimumY,
        int maximumY,
        CheeseSpec[] cheese,
        TunnelSpec[] spaghetti,
        TunnelSpec[] noodles,
        ulong fingerprint)
    {
        public int MinimumY { get; } = minimumY;
        public int MaximumY { get; } = maximumY;
        public CheeseSpec[] Cheese { get; } = cheese;
        public TunnelSpec[] Spaghetti { get; } = spaghetti;
        public TunnelSpec[] Noodles { get; } = noodles;
        public ulong Fingerprint { get; } = fingerprint;
    }

    public static Report Apply(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        int baseSurface = Math.Clamp((int)Math.Round(height * 0.30d), 64, height - 150);
        int rockLayer = Math.Clamp((int)Math.Round(height * 0.52d), baseSurface + 40, height - 90);
        int underworldTop = Math.Clamp((int)Math.Round(height * 0.84d), rockLayer + 40, height - 45);
        int oceanWidth = Math.Clamp(width / 12, 48, 360);
        ReservedMask mask = ReservedMask.Create(context.Request.Seed, width, height, baseSurface, rockLayer, underworldTop, oceanWidth);
        return Apply(context, baseSurface, rockLayer, underworldTop, oceanWidth, mask.Contains);
    }
}
