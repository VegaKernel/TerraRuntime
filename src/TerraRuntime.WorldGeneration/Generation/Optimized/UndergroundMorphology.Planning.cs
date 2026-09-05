using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Optimized;

internal static partial class UndergroundMorphology
{
    private static FeaturePlan BuildPlan(
        ulong seed,
        int width,
        int height,
        int baseSurface,
        int rockLayer,
        int underworldTop,
        int oceanWidth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (baseSurface < 0 || baseSurface >= height)
            throw new ArgumentOutOfRangeException(nameof(baseSurface));
        if (rockLayer <= baseSurface || rockLayer >= height)
            throw new ArgumentOutOfRangeException(nameof(rockLayer));
        if (underworldTop <= rockLayer || underworldTop >= height)
            throw new ArgumentOutOfRangeException(nameof(underworldTop));

        int left = Math.Clamp(oceanWidth + 18, 8, width - 9);
        int right = Math.Clamp(width - oceanWidth - 19, left + 1, width - 9);
        int minY = Math.Clamp(
            baseSurface + Math.Clamp(height / 22, 18, 110),
            baseSurface + 12,
            underworldTop - 46);
        int maxY = Math.Clamp(underworldTop - 18, minY + 16, height - 12);
        int interiorWidth = Math.Max(1, right - left + 1);
        int bandHeight = Math.Max(1, maxY - minY + 1);
        long fieldArea = (long)interiorWidth * bandHeight;

        int cheeseCount = Math.Clamp(checked((int)(fieldArea / 240_000L)) + 6, 8, 64);
        int spaghettiCount = Math.Clamp(width / 420 + 8, 10, 36);
        int noodleCount = Math.Clamp(width / 220 + 10, 12, 56);

        var cheese = new CheeseSpec[cheeseCount];
        double minRadiusX = Math.Clamp(width / 280d, 10d, 28d);
        double maxRadiusX = Math.Clamp(width / 130d, minRadiusX + 8d, 60d);
        double minRadiusY = Math.Clamp(height / 90d, 6d, 18d);
        double maxRadiusY = Math.Clamp(height / 42d, minRadiusY + 6d, 38d);
        for (int i = 0; i < cheese.Length; i++)
        {
            ulong featureSeed = seed ^ CheeseSeed ^ unchecked((ulong)i * 0x9E3779B97F4A7C15UL);
            double slot = (i + 0.5d + (Hash01(featureSeed, 1) - 0.5d) * 0.72d) / cheese.Length;
            double x = left + Math.Clamp(slot, 0.02d, 0.98d) * Math.Max(1, right - left);
            int verticalSlot = i & 3;
            double verticalFraction = (verticalSlot + 0.42d + Hash01(featureSeed, 2) * 0.16d) / 4d;
            double y = minY + verticalFraction * bandHeight;
            double radiusX = Lerp(minRadiusX, maxRadiusX, Hash01(featureSeed, 3));
            double radiusY = Lerp(minRadiusY, maxRadiusY, Hash01(featureSeed, 4));
            double rotation = (Hash01(featureSeed, 5) - 0.5d) * 0.95d;
            cheese[i] = new CheeseSpec(featureSeed, x, y, radiusX, radiusY, rotation);
        }

        var spaghetti = new TunnelSpec[spaghettiCount];
        for (int i = 0; i < spaghetti.Length; i++)
        {
            ulong featureSeed = seed ^ SpaghettiSeed ^ unchecked((ulong)i * 0xD1B54A32D192ED03UL);
            double slot = (i + 0.5d + (Hash01(featureSeed, 1) - 0.5d) * 0.65d) / spaghetti.Length;
            double x = left + Math.Clamp(slot, 0.015d, 0.985d) * Math.Max(1, right - left);
            int verticalSlot = i % 5;
            double y = minY + ((verticalSlot + 0.25d + Hash01(featureSeed, 2) * 0.5d) / 5d) * bandHeight;
            double facing = (i & 1) == 0 ? 1d : -1d;
            double angle = (Hash01(featureSeed, 3) - 0.5d) * 1.18d + (facing < 0d ? Math.PI : 0d);
            int steps = Math.Clamp(width / 10 + (int)Math.Round(Hash01(featureSeed, 4) * width / 35d), 90, 440);
            double radius = Lerp(2.6d, 5.2d, Hash01(featureSeed, 5));
            spaghetti[i] = new TunnelSpec(featureSeed, x, y, Math.Cos(angle), Math.Sin(angle), steps, radius, Hash01(featureSeed, 6) * Math.PI * 2d);
        }

        var noodles = new TunnelSpec[noodleCount];
        for (int i = 0; i < noodles.Length; i++)
        {
            ulong featureSeed = seed ^ NoodleSeed ^ unchecked((ulong)i * 0x94D049BB133111EBUL);
            double x = left + Hash01(featureSeed, 1) * Math.Max(1, right - left);
            int verticalSlot = i % 4;
            double y = minY + ((verticalSlot + 0.2d + Hash01(featureSeed, 2) * 0.6d) / 4d) * bandHeight;
            double angle = Hash01(featureSeed, 3) * Math.PI * 2d;
            int steps = Math.Clamp(width / 22 + (int)Math.Round(Hash01(featureSeed, 4) * width / 55d), 64, 240);
            double radius = Lerp(1.25d, 2.15d, Hash01(featureSeed, 5));
            noodles[i] = new TunnelSpec(featureSeed, x, y, Math.Cos(angle), Math.Sin(angle), steps, radius, Hash01(featureSeed, 6) * Math.PI * 2d);
        }

        ulong fingerprint = 1469598103934665603UL;
        fingerprint = Fingerprint(fingerprint, AlgorithmVersion);
        fingerprint = Fingerprint(fingerprint, width);
        fingerprint = Fingerprint(fingerprint, height);
        foreach (CheeseSpec feature in cheese)
        {
            fingerprint = Fingerprint(fingerprint, feature.Seed);
            fingerprint = Fingerprint(fingerprint, Quantize(feature.X));
            fingerprint = Fingerprint(fingerprint, Quantize(feature.Y));
            fingerprint = Fingerprint(fingerprint, Quantize(feature.RadiusX));
            fingerprint = Fingerprint(fingerprint, Quantize(feature.RadiusY));
        }
        foreach (TunnelSpec feature in spaghetti)
            fingerprint = FingerprintTunnel(fingerprint, feature);
        foreach (TunnelSpec feature in noodles)
            fingerprint = FingerprintTunnel(fingerprint, feature);

        return new FeaturePlan(minY, maxY, cheese, spaghetti, noodles, fingerprint);
    }

    private static void ValidateReport(Report report, FeaturePlan plan, int width, int bandHeight)
    {
        int minimumCheese = Math.Max(4, plan.Cheese.Length * 3 / 4);
        int minimumSpaghetti = Math.Max(5, plan.Spaghetti.Length * 2 / 3);
        int minimumNoodles = Math.Max(6, plan.Noodles.Length / 2);
        int minimumConnectors = Math.Max(3, Math.Max(0, plan.Cheese.Length - 1) / 2);
        long undergroundArea = (long)Math.Max(1, width) * Math.Max(1, bandHeight);
        int minimumCarvedTiles = Math.Clamp(checked((int)Math.Min(int.MaxValue, undergroundArea / 95L)), 650, 125_000);
        int minimumSectors = Math.Clamp(width / 700 + 5, 5, 12);

        if (report.CheeseCaverns < minimumCheese ||
            report.SpaghettiTunnels < minimumSpaghetti ||
            report.NoodleTunnels < minimumNoodles ||
            report.ConnectorTunnels < minimumConnectors ||
            report.CarvedTiles < minimumCarvedTiles ||
            report.TouchedSectors < minimumSectors ||
            report.UpperBandCarvedTiles < 80 ||
            report.DeepBandCarvedTiles < 80)
        {
            throw new InvalidOperationException(
                "Optimized underground morphology failed its density/topology gate: " +
                $"cheese={report.CheeseCaverns}/{minimumCheese}, " +
                $"spaghetti={report.SpaghettiTunnels}/{minimumSpaghetti}, " +
                $"noodles={report.NoodleTunnels}/{minimumNoodles}, " +
                $"connectors={report.ConnectorTunnels}/{minimumConnectors}, " +
                $"tiles={report.CarvedTiles}/{minimumCarvedTiles}, sectors={report.TouchedSectors}/{minimumSectors}, " +
                $"upper={report.UpperBandCarvedTiles}, deep={report.DeepBandCarvedTiles}.");
        }
    }
}
