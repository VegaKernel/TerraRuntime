using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeSignStoreTests
{
    [Fact]
    public void Loaded_sign_reads_and_snapshot_is_detached()
    {
        var store = new RuntimeSignStore([new WorldSign(0, "first", 10, 20)]);

        Assert.True(store.TryRead(10, 20, out WorldSign first));
        Assert.Equal("first", first.Text);
        Assert.True(store.TryCaptureCanonicalSnapshot(out WorldSign[] snapshot));

        var update = new TerrariaSignState(0, 10, 20, "changed", 99, 1);
        Assert.True(store.TryApply(in update, out WorldSign? committed, out bool textChanged));
        Assert.True(textChanged);
        Assert.NotNull(committed);
        Assert.Equal("changed", committed!.Text);
        Assert.Equal("first", snapshot[0].Text);
    }

    [Fact]
    public void Read_normalizes_sign_frame_and_allocates_first_free_slot()
    {
        var tiles = CreateTiles();
        SetSignTile(tiles, 10, 20, frameX: 0, frameY: 0);
        SetSignTile(tiles, 11, 21, frameX: 18, frameY: 18);
        var store = new RuntimeSignStore([new WorldSign(0, "occupied", 30, 40)], tiles);

        Assert.True(store.TryRead(11, 21, out WorldSign sign));
        Assert.Equal((short)1, sign.SlotId);
        Assert.Equal(10, sign.X);
        Assert.Equal(20, sign.Y);
        Assert.Equal(string.Empty, sign.Text);

        Assert.True(store.TryRead(10, 20, out WorldSign secondRead));
        Assert.Equal(sign.SlotId, secondRead.SlotId);
    }

    [Fact]
    public void Packet47_can_create_sparse_slot_and_snapshot_compacts_runtime_ids()
    {
        var tiles = CreateTiles();
        SetSignTile(tiles, 10, 20);
        SetSignTile(tiles, 30, 40);
        var store = new RuntimeSignStore([new WorldSign(0, "zero", 10, 20)], tiles);
        var update = new TerrariaSignState(7, 30, 40, "seven", 99, 0x7F);

        Assert.True(store.TryApply(in update, out WorldSign? committed, out bool textChanged));
        Assert.True(textChanged);
        Assert.NotNull(committed);
        Assert.Equal((short)7, committed!.SlotId);

        Assert.True(store.TryCaptureCanonicalSnapshot(out WorldSign[] snapshot));
        Assert.Equal(2, snapshot.Length);
        Assert.Equal((short)0, snapshot[0].SlotId);
        Assert.Equal("zero", snapshot[0].Text);
        Assert.Equal((short)1, snapshot[1].SlotId);
        Assert.Equal("seven", snapshot[1].Text);
    }

    [Fact]
    public void Packet47_replaces_coordinates_and_old_active_tile_can_be_recreated_on_read()
    {
        var tiles = CreateTiles();
        SetSignTile(tiles, 10, 20);
        SetSignTile(tiles, 30, 40);
        var store = new RuntimeSignStore([new WorldSign(0, "same", 10, 20)], tiles);
        var update = new TerrariaSignState(0, 30, 40, "same", 0, 0);

        Assert.True(store.TryApply(in update, out WorldSign? committed, out bool textChanged));
        Assert.False(textChanged);
        Assert.NotNull(committed);
        Assert.Equal(30, committed!.X);
        Assert.Equal(40, committed.Y);

        Assert.True(store.TryCaptureCanonicalSnapshot(out WorldSign[] movedSnapshot));
        WorldSign movedBeforeRead = Assert.Single(movedSnapshot);
        Assert.Equal((short)0, movedBeforeRead.SlotId);
        Assert.Equal(30, movedBeforeRead.X);
        Assert.Equal(40, movedBeforeRead.Y);

        Assert.True(store.TryRead(10, 20, out WorldSign recreated));
        Assert.Equal((short)1, recreated.SlotId);
        Assert.Equal(string.Empty, recreated.Text);
        Assert.True(store.TryRead(30, 40, out WorldSign moved));
        Assert.Equal((short)0, moved.SlotId);
    }

    [Fact]
    public void Packet47_invalid_sign_tile_clears_slot_and_old_active_tile_can_be_recreated_on_read()
    {
        var tiles = CreateTiles();
        SetSignTile(tiles, 10, 20);
        tiles.Set(30, 40, new WorldTile { Type = 54, Flags = WorldTileFlags.Active });
        var store = new RuntimeSignStore([new WorldSign(0, "before", 10, 20)], tiles);
        var update = new TerrariaSignState(0, 30, 40, "after", 0, 0);

        Assert.True(store.TryApply(in update, out WorldSign? committed, out bool textChanged));
        Assert.True(textChanged);
        Assert.Null(committed);
        Assert.True(store.TryCaptureCanonicalSnapshot(out WorldSign[] clearedSnapshot));
        Assert.Empty(clearedSnapshot);

        Assert.True(store.TryRead(10, 20, out WorldSign recreated));
        Assert.Equal((short)0, recreated.SlotId);
        Assert.Equal(string.Empty, recreated.Text);
        Assert.True(store.TryCaptureCanonicalSnapshot(out WorldSign[] recreatedSnapshot));
        Assert.Single(recreatedSnapshot);
    }

    [Fact]
    public void Duplicate_runtime_coordinates_are_allowed_and_read_uses_lowest_slot()
    {
        var tiles = CreateTiles();
        SetSignTile(tiles, 10, 20);
        var store = new RuntimeSignStore(
        [
            new WorldSign(0, "first", 10, 20),
            new WorldSign(2, "second", 10, 20)
        ], tiles);

        Assert.True(store.TryRead(10, 20, out WorldSign sign));
        Assert.Equal((short)0, sign.SlotId);
        Assert.Equal("first", sign.Text);
        Assert.True(store.TryCaptureCanonicalSnapshot(out WorldSign[] snapshot));
        Assert.Equal(2, snapshot.Length);
        Assert.Equal("first", snapshot[0].Text);
        Assert.Equal("second", snapshot[1].Text);
    }

    private static WorldTileStore CreateTiles() => new(new WorldDimensions(50, 50));

    private static void SetSignTile(WorldTileStore tiles, int x, int y, short frameX = 0, short frameY = 0)
    {
        tiles.Set(x, y, new WorldTile
        {
            Type = 55,
            FrameX = frameX,
            FrameY = frameY,
            Flags = WorldTileFlags.Active
        });
    }
}
