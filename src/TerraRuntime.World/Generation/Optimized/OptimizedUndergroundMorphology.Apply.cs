using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

internal static partial class OptimizedUndergroundMorphology
{
    public static Report Apply(
        IWorldGenerationContext context,
        int baseSurface,
        int rockLayer,
        int underworldTop,
        int oceanWidth,
        Func<int, int, bool> isProtected)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(isProtected);

        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        FeaturePlan plan = BuildPlan(
            context.Request.Seed,
            width,
            height,
            baseSurface,
            rockLayer,
            underworldTop,
            oceanWidth);
        var accumulator = new CarveAccumulator(width, plan.MinimumY, plan.MaximumY, rockLayer);

        int cheeseCarved = 0;
        for (int i = 0; i < plan.Cheese.Length; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int before = accumulator.CarvedTiles;
            CarveCheese(context.Workspace, plan.Cheese[i], plan.MinimumY, plan.MaximumY, isProtected, accumulator);
            if (accumulator.CarvedTiles - before >= 24)
                cheeseCarved++;
        }

        int connectorCarved = 0;
        CheeseSpec[] ordered = plan.Cheese.OrderBy(static feature => feature.X).ToArray();
        for (int i = 1; i < ordered.Length; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int before = accumulator.CarvedTiles;
            CarveConnector(
                context.Workspace,
                context.Request.Seed ^ ConnectorSeed ^ unchecked((ulong)i * 0x9E3779B97F4A7C15UL),
                ordered[i - 1],
                ordered[i],
                plan.MinimumY,
                plan.MaximumY,
                isProtected,
                accumulator);
            if (accumulator.CarvedTiles - before >= 12)
                connectorCarved++;
        }

        int spaghettiCarved = 0;
        for (int i = 0; i < plan.Spaghetti.Length; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int before = accumulator.CarvedTiles;
            CarveSpaghetti(
                context.Workspace,
                plan.Spaghetti[i],
                plan.MinimumY,
                plan.MaximumY,
                isProtected,
                accumulator);
            if (accumulator.CarvedTiles - before >= 12)
                spaghettiCarved++;
        }

        int noodleCarved = 0;
        for (int i = 0; i < plan.Noodles.Length; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int before = accumulator.CarvedTiles;
            CarveNoodle(
                context.Workspace,
                plan.Noodles[i],
                plan.MinimumY,
                plan.MaximumY,
                isProtected,
                accumulator);
            if (accumulator.CarvedTiles - before >= 6)
                noodleCarved++;
        }

        var report = new Report(
            cheeseCarved,
            spaghettiCarved,
            noodleCarved,
            connectorCarved,
            accumulator.CarvedTiles,
            accumulator.TouchedSectorCount,
            accumulator.UpperBandCarvedTiles,
            accumulator.DeepBandCarvedTiles,
            plan.Fingerprint);

        ValidateReport(report, plan, width, plan.MaximumY - plan.MinimumY + 1);
        context.ReportProgress(
            1d,
            $"Applied underground morphology v{AlgorithmVersion}: cheese={report.CheeseCaverns}, " +
            $"spaghetti={report.SpaghettiTunnels}, noodles={report.NoodleTunnels}, connectors={report.ConnectorTunnels}, " +
            $"tiles={report.CarvedTiles}, sectors={report.TouchedSectors}");
        return report;
    }

    internal static PlanMetrics AnalyzePlan(
        ulong seed,
        int width,
        int height,
        int baseSurface,
        int rockLayer,
        int underworldTop,
        int oceanWidth)
    {
        FeaturePlan plan = BuildPlan(seed, width, height, baseSurface, rockLayer, underworldTop, oceanWidth);
        ulong horizontalMask = 0;
        byte verticalMask = 0;

        foreach (CheeseSpec feature in plan.Cheese)
            MarkPlanCoverage(feature.X, feature.Y, width, plan.MinimumY, plan.MaximumY, ref horizontalMask, ref verticalMask);
        foreach (TunnelSpec feature in plan.Spaghetti)
            MarkPlanCoverage(feature.X, feature.Y, width, plan.MinimumY, plan.MaximumY, ref horizontalMask, ref verticalMask);
        foreach (TunnelSpec feature in plan.Noodles)
            MarkPlanCoverage(feature.X, feature.Y, width, plan.MinimumY, plan.MaximumY, ref horizontalMask, ref verticalMask);

        return new PlanMetrics(
            plan.Cheese.Length,
            plan.Spaghetti.Length,
            plan.Noodles.Length,
            Math.Max(0, plan.Cheese.Length - 1),
            PopCount(horizontalMask),
            PopCount(verticalMask),
            plan.MinimumY,
            plan.MaximumY,
            plan.Fingerprint);
    }
}
