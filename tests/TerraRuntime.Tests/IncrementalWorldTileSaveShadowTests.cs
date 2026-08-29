using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class IncrementalWorldTileSaveShadowTests
{
    [Fact]
    public void Shadow_requires_full_bootstrap_and_preserves_column_major_world_image()
    {
        var dimensions = new WorldDimensions(201, 150);
        var live = new WorldTileStore(dimensions);
        var shadow = new IncrementalWorldTileSaveShadow(dimensions);
        var left = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        var right = new WorldTile { Type = 2, Flags = WorldTileFlags.Active };
        live.Set(0, 0, in left);
        live.Set(200, 149, in right);

        Assert.False(shadow.TryCaptureImage(out _));

        Assert.True(live.TryCaptureSectionSnapshot(new WorldSectionId(0, 0), out WorldSectionTileSnapshot? first));
        Assert.NotNull(first);
        Assert.True(shadow.TryApply(first!));
        Assert.Equal(1, shadow.InitializedSectionCount);
        Assert.False(shadow.IsComplete);

        Assert.True(live.TryCaptureSectionSnapshot(new WorldSectionId(1, 0), out WorldSectionTileSnapshot? second));
        Assert.NotNull(second);
        Assert.True(shadow.TryApply(second!));
        Assert.Equal(2, shadow.InitializedSectionCount);
        Assert.True(shadow.IsComplete);

        Assert.True(shadow.TryCaptureImage(out WorldTileSaveImage? image));
        Assert.NotNull(image);
        Assert.Equal((ushort)1, image!.Get(0, 0).Type);
        Assert.Equal((ushort)2, image.Get(200, 149).Type);
        Assert.Equal(dimensions.WidthTiles * dimensions.HeightTiles, image.Count);

        ReadOnlySpan<WorldTile> columnMajor = image.ColumnMajorTiles.Span;
        Assert.Equal((ushort)1, columnMajor[0].Type);
        Assert.Equal((ushort)2, columnMajor[(200 * dimensions.HeightTiles) + 149].Type);
    }

    [Fact]
    public void Newer_section_revision_updates_shadow_without_mutating_previous_save_image()
    {
        var dimensions = new WorldDimensions(200, 150);
        var live = new WorldTileStore(dimensions);
        var shadow = new IncrementalWorldTileSaveShadow(dimensions);
        var original = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        live.Set(5, 6, in original);

        Assert.True(live.TryCaptureSectionSnapshot(new WorldSectionId(0, 0), out WorldSectionTileSnapshot? first));
        Assert.NotNull(first);
        Assert.True(shadow.TryApply(first!));
        Assert.True(shadow.TryCaptureImage(out WorldTileSaveImage? before));
        Assert.NotNull(before);

        var updated = new WorldTile { Type = 2, Flags = WorldTileFlags.Active };
        live.Set(5, 6, in updated);
        Assert.True(live.TryCaptureSectionSnapshot(new WorldSectionId(0, 0), out WorldSectionTileSnapshot? second));
        Assert.NotNull(second);
        Assert.True(second!.Revision > first!.Revision);
        Assert.True(shadow.TryApply(second));
        Assert.False(shadow.TryApply(first));

        Assert.True(shadow.TryCaptureImage(out WorldTileSaveImage? after));
        Assert.NotNull(after);
        Assert.Equal((ushort)1, before!.Get(5, 6).Type);
        Assert.Equal((ushort)2, after!.Get(5, 6).Type);
        Assert.Equal(1, shadow.InitializedSectionCount);
    }

    [Fact]
    public void Batch_apply_counts_only_newer_non_null_section_snapshots()
    {
        var dimensions = new WorldDimensions(201, 150);
        var live = new WorldTileStore(dimensions);
        var shadow = new IncrementalWorldTileSaveShadow(dimensions);

        Assert.True(live.TryCaptureSectionSnapshot(new WorldSectionId(0, 0), out WorldSectionTileSnapshot? first));
        Assert.True(live.TryCaptureSectionSnapshot(new WorldSectionId(1, 0), out WorldSectionTileSnapshot? second));
        Assert.NotNull(first);
        Assert.NotNull(second);

        WorldSectionTileSnapshot?[] batch = [first, null, second, first];
        Assert.Equal(2, shadow.Apply(batch));
        Assert.True(shadow.IsComplete);
        Assert.Equal(2, shadow.InitializedSectionCount);
    }
}
