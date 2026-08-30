using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeChestObjectMetadataLifecycleTests
{
    [Fact]
    public void Container_placement_creates_runtime_chest_and_break_removes_both_sides()
    {
        WorldTileStore tiles = CreateSupportedContainerWorld();
        var chests = new RuntimeChestStore([]);
        var metadata = new RuntimeChestObjectMetadataLifecycle(chests);
        var objects = new VanillaMultiTileObjectMutationService(tiles);

        VanillaMultiTileObjectMutationResult placed = objects.TryPlaceAtOrigin(
            VanillaTileIds.Containers,
            originX: 210,
            originY: 161,
            metadata);

        Assert.True(placed.Applied);
        WorldChest created = Assert.Single(chests.CaptureSnapshot());
        Assert.Equal((short)0, created.SlotId);
        Assert.Equal(210, created.X);
        Assert.Equal(160, created.Y);
        Assert.Empty(created.Name);
        Assert.Equal(VanillaChestStorageFacts1458.DefaultItemSlots, created.Items.Length);
        Assert.All(created.Items, item => Assert.True(item.IsEmpty));

        VanillaMultiTileObjectMutationResult broken = objects.TryBreakAt(211, 161, metadata);

        Assert.True(broken.Applied);
        Assert.Empty(chests.CaptureSnapshot());
        Assert.False(tiles.Get(210, 160).IsActive);
        Assert.False(tiles.Get(211, 160).IsActive);
        Assert.False(tiles.Get(210, 161).IsActive);
        Assert.False(tiles.Get(211, 161).IsActive);
    }

    [Fact]
    public void Existing_metadata_at_anchor_vetoes_placement_before_any_tile_commit()
    {
        WorldTileStore tiles = CreateSupportedContainerWorld();
        var existing = new WorldChest(
            7,
            210,
            160,
            "Existing",
            new WorldChestItem[VanillaChestStorageFacts1458.DefaultItemSlots]);
        var chests = new RuntimeChestStore([existing]);
        var metadata = new RuntimeChestObjectMetadataLifecycle(chests);
        var objects = new VanillaMultiTileObjectMutationService(tiles);
        DrainDirty(tiles);

        VanillaMultiTileObjectMutationResult result = objects.TryPlaceAtOrigin(
            VanillaTileIds.Containers,
            originX: 210,
            originY: 161,
            metadata);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.MetadataRejected, result.Status);
        Assert.False(tiles.Get(210, 160).IsActive);
        Assert.False(tiles.Get(211, 161).IsActive);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
        WorldChest unchanged = Assert.Single(chests.CaptureSnapshot());
        Assert.Equal((short)7, unchanged.SlotId);
        Assert.Equal("Existing", unchanged.Name);
    }

    [Fact]
    public void Open_runtime_chest_vetoes_object_break_until_exact_session_closes_it()
    {
        WorldTileStore tiles = CreateSupportedContainerWorld();
        var chests = new RuntimeChestStore([]);
        var metadata = new RuntimeChestObjectMetadataLifecycle(chests);
        var objects = new VanillaMultiTileObjectMutationService(tiles);
        Assert.True(objects.TryPlaceAtOrigin(VanillaTileIds.Containers, 210, 161, metadata).Applied);
        DrainDirty(tiles);

        ConnectionHandle owner = Connection(11, 0, 1);
        Assert.True(chests.TryOpen(owner, 210, 160, out WorldChest opened));

        VanillaMultiTileObjectMutationResult rejected = objects.TryBreakAt(210, 160, metadata);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.MetadataRejected, rejected.Status);
        Assert.True(tiles.Get(210, 160).IsActive);
        Assert.True(tiles.Get(211, 161).IsActive);
        Assert.Single(chests.CaptureSnapshot());
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);

        Assert.True(chests.TryClose(owner, out short closed));
        Assert.Equal(opened.SlotId, closed);
        Assert.True(objects.TryBreakAt(210, 160, metadata).Applied);
        Assert.Empty(chests.CaptureSnapshot());
    }

    [Fact]
    public void Nonempty_closed_chest_vetoes_object_break_without_losing_items()
    {
        WorldTileStore tiles = CreateSupportedContainerWorld();
        var chests = new RuntimeChestStore([]);
        var metadata = new RuntimeChestObjectMetadataLifecycle(chests);
        var objects = new VanillaMultiTileObjectMutationService(tiles);
        Assert.True(objects.TryPlaceAtOrigin(VanillaTileIds.Containers, 210, 161, metadata).Applied);

        ConnectionHandle owner = Connection(12, 0, 1);
        Assert.True(chests.TryOpen(owner, 210, 160, out WorldChest opened));
        var update = new TerrariaChestItemState(opened.SlotId, 0, 5, 0, 1);
        Assert.True(chests.TrySetItem(owner, in update, out _));
        Assert.True(chests.TryClose(owner, out _));
        DrainDirty(tiles);

        VanillaMultiTileObjectMutationResult rejected = objects.TryBreakAt(211, 161, metadata);

        Assert.Equal(VanillaMultiTileObjectMutationStatus.MetadataRejected, rejected.Status);
        Assert.True(tiles.Get(210, 160).IsActive);
        WorldChest preserved = Assert.Single(chests.CaptureSnapshot());
        Assert.Equal(5, preserved.Items[0].Stack);
        Assert.Equal(1, preserved.Items[0].ItemType);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Runtime_store_allocates_lowest_free_slot_and_reuses_it_only_after_safe_removal()
    {
        var slotZero = new WorldChest(0, 10, 20, string.Empty, new WorldChestItem[1]);
        var slotTwo = new WorldChest(2, 30, 40, string.Empty, new WorldChestItem[1]);
        var chests = new RuntimeChestStore([slotZero, slotTwo]);

        Assert.True(chests.CanCreateAt(50, 60));
        Assert.True(chests.TryCreate(50, 60, 40, out WorldChest first));
        Assert.Equal((short)1, first.SlotId);
        Assert.False(chests.CanCreateAt(50, 60));

        Assert.True(chests.CanRemoveAt(50, 60));
        Assert.True(chests.TryRemoveAt(50, 60, out WorldChest removed));
        Assert.Equal((short)1, removed.SlotId);

        Assert.True(chests.TryCreate(70, 80, 40, out WorldChest reused));
        Assert.Equal((short)1, reused.SlotId);
        Assert.Equal(70, reused.X);
        Assert.Equal(80, reused.Y);
    }

    [Fact]
    public void Runtime_store_rejects_invalid_variable_item_slot_counts_without_allocating_metadata()
    {
        var chests = new RuntimeChestStore([]);

        Assert.False(chests.TryCreate(10, 20, 0, out _));
        Assert.False(chests.TryCreate(
            10,
            20,
            VanillaChestStorageFacts1458.MaximumProtocolItemSlots + 1,
            out _));
        Assert.Empty(chests.CaptureSnapshot());

        Assert.True(chests.TryCreate(
            10,
            20,
            VanillaChestStorageFacts1458.MaximumProtocolItemSlots,
            out WorldChest maximum));
        Assert.Equal(VanillaChestStorageFacts1458.MaximumProtocolItemSlots, maximum.Items.Length);
    }

    [Fact]
    public void Chest_name_does_not_block_break_when_storage_is_empty_and_closed()
    {
        WorldTileStore tiles = CreateSupportedContainerWorld();
        var named = new WorldChest(
            3,
            210,
            160,
            "Named but empty",
            new WorldChestItem[VanillaChestStorageFacts1458.DefaultItemSlots]);
        var chests = new RuntimeChestStore([named]);
        var metadata = new RuntimeChestObjectMetadataLifecycle(chests);
        var objects = new VanillaMultiTileObjectMutationService(tiles);
        SeedContainerFootprint(tiles);
        DrainDirty(tiles);

        VanillaMultiTileObjectMutationResult result = objects.TryBreakAt(210, 160, metadata);

        Assert.True(result.Applied);
        Assert.Empty(chests.CaptureSnapshot());
    }

    private static WorldTileStore CreateSupportedContainerWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        SetActiveTile(tiles, 210, 162, VanillaTileIds.Stone);
        SetActiveTile(tiles, 211, 162, VanillaTileIds.Stone);
        DrainDirty(tiles);
        return tiles;
    }

    private static void SeedContainerFootprint(WorldTileStore tiles)
    {
        SetObjectCell(tiles, 210, 160, 0, 0);
        SetObjectCell(tiles, 211, 160, 18, 0);
        SetObjectCell(tiles, 210, 161, 0, 18);
        SetObjectCell(tiles, 211, 161, 18, 18);
    }

    private static void SetObjectCell(WorldTileStore tiles, int x, int y, short frameX, short frameY)
    {
        WorldTile tile = tiles.Get(x, y);
        Assert.True(tile.TrySetTileType(VanillaTileIds.Containers));
        tile.FrameX = frameX;
        tile.FrameY = frameY;
        tile.Flags |= WorldTileFlags.Active;
        tiles.Set(x, y, in tile);
    }

    private static void SetActiveTile(WorldTileStore tiles, int x, int y, TileTypeId type)
    {
        WorldTile tile = tiles.Get(x, y);
        Assert.True(tile.TrySetTileType(type));
        tile.Flags |= WorldTileFlags.Active;
        tiles.Set(x, y, in tile);
    }

    private static void DrainDirty(WorldTileStore tiles)
    {
        var buffer = new WorldSectionId[tiles.Dimensions.SectionCount];
        _ = tiles.DirtySections.Drain(buffer);
        _ = tiles.PersistenceDirtySections.Drain(buffer);
    }

    private static ConnectionHandle Connection(long connectionId, byte playerSlot, ulong generation) =>
        new(
            GameCommandSourceId.FromConnection(connectionId),
            new PlayerHandle(
                new PlayerSlotId(playerSlot),
                new PlayerSessionGeneration(generation)));
}
