using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaMultiTileObjectMutationServiceTests
{
    [Fact]
    public void Container_placement_commits_base_style_frames_and_metadata()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        SetActiveTile(tiles, 210, 162, VanillaTileIds.Stone);
        SetActiveTile(tiles, 211, 162, VanillaTileIds.Stone);

        WorldTile decoratedTarget = tiles.Get(210, 160);
        Assert.True(decoratedTarget.TrySetWallType(VanillaWallIds.Stone));
        decoratedTarget.Flags = WorldTileFlags.WireRed;
        decoratedTarget.LiquidAmount = 100;
        decoratedTarget.LiquidKind = WorldLiquidKind.Water;
        tiles.Set(210, 160, in decoratedTarget);
        DrainDirty(tiles);

        var metadata = new RecordingMetadataLifecycle();
        var service = new VanillaMultiTileObjectMutationService(tiles);
        VanillaMultiTileObjectMutationResult result = service.TryPlaceAtOrigin(
            VanillaTileIds.Containers,
            originX: 210,
            originY: 161,
            metadata);

        Assert.True(result.Applied);
        Assert.Equal(new WorldTileRegion(210, 160, 2, 2), result.Descriptor.Bounds);
        Assert.Equal(4, result.ChangedTiles);
        Assert.Equal(VanillaTileObjectMetadataKind.Chest, result.Descriptor.MetadataKind);
        Assert.Equal(1, metadata.CreateCount);
        Assert.Equal(0, metadata.RemoveCount);

        AssertObjectCell(tiles, 210, 160, VanillaTileIds.Containers, 0, 0);
        AssertObjectCell(tiles, 211, 160, VanillaTileIds.Containers, 18, 0);
        AssertObjectCell(tiles, 210, 161, VanillaTileIds.Containers, 0, 18);
        AssertObjectCell(tiles, 211, 161, VanillaTileIds.Containers, 18, 18);

        WorldTile preserved = tiles.Get(210, 160);
        Assert.Equal(VanillaWallIds.Stone, preserved.WallType);
        Assert.True((preserved.Flags & WorldTileFlags.WireRed) != 0);
        Assert.Equal((byte)100, preserved.LiquidAmount);
    }

    [Fact]
    public void Container_placement_rejects_missing_support_without_metadata_or_tile_changes()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        SetActiveTile(tiles, 210, 162, VanillaTileIds.Stone);
        DrainDirty(tiles);
        var metadata = new RecordingMetadataLifecycle();
        var service = new VanillaMultiTileObjectMutationService(tiles);

        VanillaMultiTileObjectMutationResult result = service.TryPlaceAtOrigin(
            VanillaTileIds.Containers,
            originX: 210,
            originY: 161,
            metadata);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.MissingSupport, result.Status);
        Assert.Equal(0, metadata.CreateCount);
        Assert.False(tiles.Get(210, 160).IsActive);
        Assert.False(tiles.Get(211, 161).IsActive);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Dresser_origin_translates_to_three_by_two_top_left()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        for (int x = 219; x <= 221; x++)
            SetActiveTile(tiles, x, 162, VanillaTileIds.Stone);
        DrainDirty(tiles);

        var metadata = new RecordingMetadataLifecycle();
        var service = new VanillaMultiTileObjectMutationService(tiles);
        VanillaMultiTileObjectMutationResult result = service.TryPlaceAtOrigin(
            VanillaTileIds.Dressers,
            originX: 220,
            originY: 161,
            metadata);

        Assert.True(result.Applied);
        Assert.Equal(new WorldTileRegion(219, 160, 3, 2), result.Descriptor.Bounds);
        Assert.Equal(6, result.ChangedTiles);
        Assert.Equal(220, result.Descriptor.OriginX);
        Assert.Equal(161, result.Descriptor.OriginY);
        for (int objectY = 0; objectY < 2; objectY++)
        {
            for (int objectX = 0; objectX < 3; objectX++)
            {
                AssertObjectCell(
                    tiles,
                    219 + objectX,
                    160 + objectY,
                    VanillaTileIds.Dressers,
                    checked((short)(objectX * 18)),
                    checked((short)(objectY * 18)));
            }
        }
    }

    [Fact]
    public void Sign_placement_remains_fail_closed_until_support_rules_are_verified()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        var metadata = new RecordingMetadataLifecycle();
        var service = new VanillaMultiTileObjectMutationService(tiles);

        VanillaMultiTileObjectMutationResult result = service.TryPlaceAtOrigin(
            VanillaTileIds.Signs,
            originX: 210,
            originY: 161,
            metadata);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.UnsupportedPlacementRules, result.Status);
        Assert.Equal(0, metadata.CreateCount);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Break_from_non_anchor_cell_resolves_and_clears_whole_object_preserving_independent_state()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        SetActiveTile(tiles, 210, 162, VanillaTileIds.Stone);
        SetActiveTile(tiles, 211, 162, VanillaTileIds.Stone);
        DrainDirty(tiles);
        var metadata = new RecordingMetadataLifecycle();
        var service = new VanillaMultiTileObjectMutationService(tiles);
        Assert.True(service.TryPlaceAtOrigin(VanillaTileIds.Containers, 210, 161, metadata).Applied);

        WorldTile bottomRight = tiles.Get(211, 161);
        Assert.True(bottomRight.TrySetWallType(VanillaWallIds.Stone));
        bottomRight.Flags |= WorldTileFlags.WireBlue;
        bottomRight.LiquidAmount = 73;
        bottomRight.LiquidKind = WorldLiquidKind.Honey;
        tiles.Set(211, 161, in bottomRight);
        DrainDirty(tiles);

        VanillaMultiTileObjectMutationResult result = service.TryBreakAt(211, 161, metadata);

        Assert.True(result.Applied);
        Assert.Equal(new WorldTileRegion(210, 160, 2, 2), result.Descriptor.Bounds);
        Assert.Equal(4, result.ChangedTiles);
        Assert.Equal(1, metadata.RemoveCount);
        for (int y = 160; y <= 161; y++)
        {
            for (int x = 210; x <= 211; x++)
            {
                WorldTile cleared = tiles.Get(x, y);
                Assert.False(cleared.IsActive);
                Assert.Equal((ushort)0, cleared.Type);
            }
        }

        WorldTile preserved = tiles.Get(211, 161);
        Assert.Equal(VanillaWallIds.Stone, preserved.WallType);
        Assert.True((preserved.Flags & WorldTileFlags.WireBlue) != 0);
        Assert.Equal((byte)73, preserved.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Honey, preserved.LiquidKind);
    }

    [Fact]
    public void Malformed_object_frame_rejects_break_atomically()
    {
        var tiles = CreateSupportedContainerWorld();
        var metadata = new RecordingMetadataLifecycle();
        var service = new VanillaMultiTileObjectMutationService(tiles);
        Assert.True(service.TryPlaceAtOrigin(VanillaTileIds.Containers, 210, 161, metadata).Applied);

        WorldTile corrupt = tiles.Get(211, 160);
        corrupt.FrameX = 7;
        tiles.Set(211, 160, in corrupt);
        DrainDirty(tiles);
        int removesBefore = metadata.RemoveCount;

        VanillaMultiTileObjectMutationResult result = service.TryBreakAt(210, 160, metadata);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.InvalidObjectState, result.Status);
        Assert.Equal(removesBefore, metadata.RemoveCount);
        Assert.True(tiles.Get(210, 160).IsActive);
        Assert.True(tiles.Get(211, 160).IsActive);
        Assert.True(tiles.Get(210, 161).IsActive);
        Assert.True(tiles.Get(211, 161).IsActive);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Metadata_veto_rejects_break_before_world_changes()
    {
        var tiles = CreateSupportedContainerWorld();
        var metadata = new RecordingMetadataLifecycle();
        var service = new VanillaMultiTileObjectMutationService(tiles);
        Assert.True(service.TryPlaceAtOrigin(VanillaTileIds.Containers, 210, 161, metadata).Applied);
        DrainDirty(tiles);
        WorldTile before = tiles.Get(210, 160);
        metadata.AllowRemove = false;

        VanillaMultiTileObjectMutationResult result = service.TryBreakAt(210, 160, metadata);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.MetadataRejected, result.Status);
        Assert.Equal(before, tiles.Get(210, 160));
        Assert.Equal(0, metadata.RemoveCount);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Metadata_create_commit_failure_leaves_world_unchanged()
    {
        var tiles = CreateSupportedContainerWorld();
        var metadata = new RecordingMetadataLifecycle { AllowCreateCommit = false };
        var service = new VanillaMultiTileObjectMutationService(tiles);
        DrainDirty(tiles);

        VanillaMultiTileObjectMutationResult result = service.TryPlaceAtOrigin(
            VanillaTileIds.Containers,
            210,
            161,
            metadata);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.MetadataCommitFailed, result.Status);
        Assert.Equal(1, metadata.CreateCount);
        Assert.False(tiles.Get(210, 160).IsActive);
        Assert.False(tiles.Get(211, 161).IsActive);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Metadata_remove_commit_failure_leaves_object_unchanged()
    {
        var tiles = CreateSupportedContainerWorld();
        var metadata = new RecordingMetadataLifecycle();
        var service = new VanillaMultiTileObjectMutationService(tiles);
        Assert.True(service.TryPlaceAtOrigin(VanillaTileIds.Containers, 210, 161, metadata).Applied);
        DrainDirty(tiles);
        metadata.AllowRemoveCommit = false;

        VanillaMultiTileObjectMutationResult result = service.TryBreakAt(211, 161, metadata);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.MetadataCommitFailed, result.Status);
        Assert.Equal(1, metadata.RemoveCount);
        Assert.True(tiles.Get(210, 160).IsActive);
        Assert.True(tiles.Get(211, 161).IsActive);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Placement_crossing_section_boundary_dirties_both_sections()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        SetActiveTile(tiles, 199, 162, VanillaTileIds.Stone);
        SetActiveTile(tiles, 200, 162, VanillaTileIds.Stone);
        DrainDirty(tiles);
        var service = new VanillaMultiTileObjectMutationService(tiles);
        var metadata = new RecordingMetadataLifecycle();

        VanillaMultiTileObjectMutationResult result = service.TryPlaceAtOrigin(
            VanillaTileIds.Containers,
            originX: 199,
            originY: 161,
            metadata);

        Assert.True(result.Applied);
        Assert.True(tiles.DirtySections.IsDirty(new WorldSectionId(0, 1)));
        Assert.True(tiles.DirtySections.IsDirty(new WorldSectionId(1, 1)));
        Assert.True(tiles.PersistenceDirtySections.IsDirty(new WorldSectionId(0, 1)));
        Assert.True(tiles.PersistenceDirtySections.IsDirty(new WorldSectionId(1, 1)));
    }

    [Fact]
    public void Style_offset_frames_still_resolve_same_object()
    {
        var tiles = CreateSupportedContainerWorld();
        var metadata = new RecordingMetadataLifecycle();
        var service = new VanillaMultiTileObjectMutationService(tiles);
        Assert.True(service.TryPlaceAtOrigin(VanillaTileIds.Containers, 210, 161, metadata).Applied);

        for (int y = 160; y <= 161; y++)
        {
            for (int x = 210; x <= 211; x++)
            {
                WorldTile cell = tiles.Get(x, y);
                cell.FrameX = checked((short)(cell.FrameX + 36));
                tiles.Set(x, y, in cell);
            }
        }
        DrainDirty(tiles);

        VanillaMultiTileObjectMutationStatus status = service.TryResolveObjectAt(
            211,
            161,
            out VanillaMultiTileObjectMutationDescriptor descriptor);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.Applied, status);
        Assert.Equal(new WorldTileRegion(210, 160, 2, 2), descriptor.Bounds);
        Assert.Equal(VanillaTileIds.Containers, descriptor.Definition.TileType);
    }

    private static WorldTileStore CreateSupportedContainerWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        SetActiveTile(tiles, 210, 162, VanillaTileIds.Stone);
        SetActiveTile(tiles, 211, 162, VanillaTileIds.Stone);
        DrainDirty(tiles);
        return tiles;
    }

    private static void SetActiveTile(WorldTileStore tiles, int x, int y, TileTypeId type)
    {
        WorldTile tile = tiles.Get(x, y);
        Assert.True(tile.TrySetTileType(type));
        tile.Flags |= WorldTileFlags.Active;
        tiles.Set(x, y, in tile);
    }

    private static void AssertObjectCell(
        WorldTileStore tiles,
        int x,
        int y,
        TileTypeId type,
        short frameX,
        short frameY)
    {
        WorldTile tile = tiles.Get(x, y);
        Assert.True(tile.IsActive);
        Assert.Equal(type, tile.TileType);
        Assert.Equal(frameX, tile.FrameX);
        Assert.Equal(frameY, tile.FrameY);
    }

    private static void DrainDirty(WorldTileStore tiles)
    {
        var buffer = new WorldSectionId[tiles.Dimensions.SectionCount];
        _ = tiles.DirtySections.Drain(buffer);
        _ = tiles.PersistenceDirtySections.Drain(buffer);
    }

    private sealed class RecordingMetadataLifecycle : IVanillaMultiTileObjectMetadataLifecycle
    {
        public bool AllowCreate { get; set; } = true;
        public bool AllowRemove { get; set; } = true;
        public bool AllowCreateCommit { get; set; } = true;
        public bool AllowRemoveCommit { get; set; } = true;
        public int CreateCount { get; private set; }
        public int RemoveCount { get; private set; }
        public VanillaMultiTileObjectMutationDescriptor LastCreate { get; private set; }
        public VanillaMultiTileObjectMutationDescriptor LastRemove { get; private set; }

        public bool CanCreate(in VanillaMultiTileObjectMutationDescriptor descriptor) => AllowCreate;

        public bool CanRemove(in VanillaMultiTileObjectMutationDescriptor descriptor) => AllowRemove;

        public bool TryCommitCreate(in VanillaMultiTileObjectMutationDescriptor descriptor)
        {
            LastCreate = descriptor;
            CreateCount++;
            return AllowCreateCommit;
        }

        public bool TryCommitRemove(in VanillaMultiTileObjectMutationDescriptor descriptor)
        {
            LastRemove = descriptor;
            RemoveCount++;
            return AllowRemoveCommit;
        }
    }
}
