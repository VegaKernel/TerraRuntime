using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTerrainSilhouetteAnalyzer1458Tests
{
    [Fact]
    public void Capture_ignores_isolated_solid_foreground_and_uses_supported_ground()
    {
        var store = new WorldTileStore(new WorldDimensions(8, 16));
        FillGround(store, y: 9);
        SetSolid(store, x: 3, y: 2);

        VanillaTerrainSilhouette1458 silhouette =
            VanillaTerrainSilhouetteAnalyzer1458.Capture(store, bucketCount: 4);

        Assert.Equal(Enumerable.Repeat(9, 8), silhouette.SurfaceY);
        Assert.Equal([9, 9, 9, 9], silhouette.BucketMedians);
        Assert.Equal(0, silhouette.TotalVariation);
    }

    [Fact]
    public void Compare_reports_column_error_bucket_error_correlation_and_variation()
    {
        var referenceStore = new WorldTileStore(new WorldDimensions(8, 16));
        var candidateStore = new WorldTileStore(new WorldDimensions(8, 16));
        for (int x = 0; x < 8; x++)
        {
            FillColumn(referenceStore, x, 6 + x / 2);
            FillColumn(candidateStore, x, 7 + x / 2);
        }
        VanillaTerrainSilhouette1458 reference =
            VanillaTerrainSilhouetteAnalyzer1458.Capture(referenceStore, bucketCount: 4);
        VanillaTerrainSilhouette1458 candidate =
            VanillaTerrainSilhouetteAnalyzer1458.Capture(candidateStore, bucketCount: 4);

        VanillaTerrainSilhouetteComparison1458 comparison =
            VanillaTerrainSilhouetteAnalyzer1458.Compare(reference, candidate);

        Assert.Equal(1d, comparison.MeanAbsoluteError);
        Assert.Equal(1d, comparison.RootMeanSquareError);
        Assert.Equal(1, comparison.Percentile95AbsoluteError);
        Assert.Equal(1d / 16d, comparison.NormalizedMeanAbsoluteError);
        Assert.Equal(1d, comparison.Correlation, precision: 12);
        Assert.Equal(1d, comparison.TotalVariationRatio);
    }

    private static void FillGround(WorldTileStore store, int y)
    {
        for (int x = 0; x < store.Dimensions.WidthTiles; x++)
            FillColumn(store, x, y);
    }

    private static void FillColumn(WorldTileStore store, int x, int surfaceY)
    {
        for (int y = surfaceY; y < store.Dimensions.HeightTiles; y++)
            SetSolid(store, x, y);
    }

    private static void SetSolid(WorldTileStore store, int x, int y)
    {
        ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
        tile.Type = 0;
        tile.Flags |= WorldTileFlags.Active;
    }
}
