using TerraRuntime.Contracts.Gameplay;
using System.Text.Json.Serialization;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Final post-pass solid-ground silhouette used by the TerrariaServer 1.4.5.8 reference differential. A surface
/// sample requires a collision-solid tile with supporting solid mass below it, so trees, furniture, ropes, and other
/// isolated foreground objects do not become false terrain peaks.
/// </summary>
internal sealed record TerrainSilhouette1458(
    int Width,
    int Height,
    [property: JsonIgnore] int[] SurfaceY,
    int[] BucketMedians,
    double MeanY,
    double StandardDeviationY,
    int Percentile05Y,
    int MedianY,
    int Percentile95Y,
    long TotalVariation);

internal sealed record TerrainSilhouetteComparison1458(
    double MeanAbsoluteError,
    double RootMeanSquareError,
    int Percentile95AbsoluteError,
    int MaximumAbsoluteError,
    double NormalizedMeanAbsoluteError,
    double NormalizedRootMeanSquareError,
    double NormalizedPercentile95AbsoluteError,
    double BucketNormalizedMeanAbsoluteError,
    double Correlation,
    double TotalVariationRatio);

internal static class TerrainSilhouetteAnalyzer1458
{
    public const int DefaultBucketCount = 256;
    private const int SupportDepth = 5;
    private const int RequiredSolidSupport = 3;

    public static TerrainSilhouette1458 Capture(
        WorldTileStore store,
        int bucketCount = DefaultBucketCount)
    {
        ArgumentNullException.ThrowIfNull(store);
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        if (bucketCount <= 0 || bucketCount > width)
            throw new ArgumentOutOfRangeException(nameof(bucketCount));

        var surface = new int[width];
        for (int x = 0; x < width; x++)
            surface[x] = FindSupportedSurface(store, x, height);

        int[] ordered = surface.ToArray();
        Array.Sort(ordered);
        double mean = surface.Average();
        double variance = 0d;
        long totalVariation = 0;
        for (int x = 0; x < width; x++)
        {
            double delta = surface[x] - mean;
            variance += delta * delta;
            if (x > 0)
                totalVariation += Math.Abs(surface[x] - surface[x - 1]);
        }

        return new TerrainSilhouette1458(
            width,
            height,
            surface,
            CaptureBucketMedians(surface, bucketCount),
            mean,
            Math.Sqrt(variance / width),
            Percentile(ordered, 0.05d),
            Percentile(ordered, 0.50d),
            Percentile(ordered, 0.95d),
            totalVariation);
    }

    public static TerrainSilhouetteComparison1458 Compare(
        TerrainSilhouette1458 reference,
        TerrainSilhouette1458 candidate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidate);
        if (reference.Width != candidate.Width || reference.Height != candidate.Height)
            throw new ArgumentException("Terrain silhouettes must have identical dimensions.", nameof(candidate));
        if (reference.BucketMedians.Length != candidate.BucketMedians.Length)
            throw new ArgumentException("Terrain silhouettes must have identical bucket counts.", nameof(candidate));

        var absoluteErrors = new int[reference.Width];
        double squaredError = 0d;
        double referenceMean = reference.MeanY;
        double candidateMean = candidate.MeanY;
        double covariance = 0d;
        double referenceVariance = 0d;
        double candidateVariance = 0d;
        long absoluteError = 0;
        int maximumError = 0;
        for (int x = 0; x < reference.Width; x++)
        {
            int error = Math.Abs(candidate.SurfaceY[x] - reference.SurfaceY[x]);
            absoluteErrors[x] = error;
            absoluteError += error;
            maximumError = Math.Max(maximumError, error);
            squaredError += (double)error * error;

            double referenceDelta = reference.SurfaceY[x] - referenceMean;
            double candidateDelta = candidate.SurfaceY[x] - candidateMean;
            covariance += referenceDelta * candidateDelta;
            referenceVariance += referenceDelta * referenceDelta;
            candidateVariance += candidateDelta * candidateDelta;
        }

        Array.Sort(absoluteErrors);
        double meanAbsoluteError = absoluteError / (double)reference.Width;
        double rootMeanSquareError = Math.Sqrt(squaredError / reference.Width);
        int percentile95Error = Percentile(absoluteErrors, 0.95d);
        double bucketAbsoluteError = 0d;
        for (int index = 0; index < reference.BucketMedians.Length; index++)
            bucketAbsoluteError += Math.Abs(candidate.BucketMedians[index] - reference.BucketMedians[index]);

        double correlationDenominator = Math.Sqrt(referenceVariance * candidateVariance);
        double correlation = correlationDenominator == 0d
            ? (reference.SurfaceY.AsSpan().SequenceEqual(candidate.SurfaceY) ? 1d : 0d)
            : covariance / correlationDenominator;

        return new TerrainSilhouetteComparison1458(
            meanAbsoluteError,
            rootMeanSquareError,
            percentile95Error,
            maximumError,
            meanAbsoluteError / reference.Height,
            rootMeanSquareError / reference.Height,
            percentile95Error / (double)reference.Height,
            bucketAbsoluteError / reference.BucketMedians.Length / reference.Height,
            correlation,
            Ratio(candidate.TotalVariation, reference.TotalVariation));
    }

    private static int FindSupportedSurface(WorldTileStore store, int x, int height)
    {
        int lastStart = height - SupportDepth;
        for (int y = 0; y <= lastStart; y++)
        {
            if (!IsCollisionSolid(store.Get(x, y)))
                continue;

            int solidSupport = 0;
            for (int offset = 0; offset < SupportDepth; offset++)
            {
                if (IsCollisionSolid(store.Get(x, y + offset)))
                    solidSupport++;
            }
            if (solidSupport >= RequiredSolidSupport)
                return y;
        }
        return height - 1;
    }

    private static bool IsCollisionSolid(in WorldTile tile) =>
        tile.IsActive &&
        !tile.IsActuated &&
        VanillaTileCollisionCatalog.IsSolid(new TileTypeId(tile.Type));

    private static int[] CaptureBucketMedians(int[] surface, int bucketCount)
    {
        var medians = new int[bucketCount];
        for (int bucket = 0; bucket < bucketCount; bucket++)
        {
            int start = bucket * surface.Length / bucketCount;
            int end = (bucket + 1) * surface.Length / bucketCount;
            int[] values = surface[start..end];
            Array.Sort(values);
            medians[bucket] = Percentile(values, 0.50d);
        }
        return medians;
    }

    private static int Percentile(int[] ordered, double percentile)
    {
        int index = (int)Math.Round((ordered.Length - 1) * percentile, MidpointRounding.AwayFromZero);
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static double Ratio(long numerator, long denominator)
    {
        if (denominator == 0)
            return numerator == 0 ? 1d : double.PositiveInfinity;
        return numerator / (double)denominator;
    }
}
