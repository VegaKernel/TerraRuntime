using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldTileChestSaveSnapshotSourceTests
{
    [Fact]
    public void Snapshot_waits_for_tile_bootstrap_and_detaches_tiles_and_chests_from_later_mutation()
    {
        var dimensions = new WorldDimensions(201, 150);
        var tiles = new WorldTileStore(dimensions);
        var originalTile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        tiles.Set(5, 6, in originalTile);

        var chestStore = new RuntimeChestStore(
        [
            new WorldChest(
                0,
                10,
                20,
                "Base",
                [new WorldChestItem(1, 1, 0), default])
        ]);
        var source = new RuntimeWorldTileChestSaveSnapshotSource(
            tiles,
            chestStore,
            dirtyBatchCapacity: 2);

        Assert.False(source.TryCapture(out _));
        Assert.Equal(1, source.CaptureTileBootstrap(1));
        Assert.False(source.TryCapture(out _));
        Assert.Equal(1, source.CaptureTileBootstrap(1));
        Assert.True(source.IsTileShadowReady);

        Assert.True(source.TryCapture(out RuntimeWorldTileChestSaveSnapshot? snapshot));
        Assert.NotNull(snapshot);
        Assert.Equal((ushort)1, snapshot!.Tiles.Get(5, 6).Type);
        Assert.Single(snapshot.Chests);
        Assert.Equal("Base", snapshot.Chests[0].Name);
        Assert.Equal(new WorldChestItem(1, 1, 0), snapshot.Chests[0].Items[0]);

        var updatedTile = new WorldTile { Type = 2, Flags = WorldTileFlags.Active };
        tiles.Set(5, 6, in updatedTile);

        ConnectionHandle owner = Connection(1, 0, 1);
        Assert.True(chestStore.TryOpen(owner, 10, 20, out _));
        var itemUpdate = new TerrariaChestItemState(0, 0, 7, 2, 1);
        Assert.True(chestStore.TrySetItem(owner, in itemUpdate, out _));
        var rename = new TerrariaActiveChestState(0, 10, 20, 4, "Loot");
        Assert.True(chestStore.TryApplyActiveState(owner, in rename, out _, out _));

        Assert.Equal((ushort)1, snapshot.Tiles.Get(5, 6).Type);
        Assert.Equal("Base", snapshot.Chests[0].Name);
        Assert.Equal(new WorldChestItem(1, 1, 0), snapshot.Chests[0].Items[0]);

        Assert.Equal(1, source.CaptureDirtyTiles(2));
        Assert.True(source.TryCapture(out RuntimeWorldTileChestSaveSnapshot? current));
        Assert.NotNull(current);
        Assert.Equal((ushort)2, current!.Tiles.Get(5, 6).Type);
        Assert.Equal("Loot", current.Chests[0].Name);
        Assert.Equal(new WorldChestItem(7, 1, 2), current.Chests[0].Items[0]);
    }

    private static ConnectionHandle Connection(long connectionId, byte playerSlot, ulong generation) =>
        new(
            GameCommandSourceId.FromConnection(connectionId),
            new PlayerHandle(
                new PlayerSlotId(playerSlot),
                new PlayerSessionGeneration(generation)));
}
