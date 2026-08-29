using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeSignStoreTests
{
    [Fact]
    public void Canonical_store_reads_exact_coordinates_and_captures_detached_snapshot()
    {
        var store = new RuntimeSignStore(
        [
            new WorldSign(0, "first", 10, 20),
            new WorldSign(1, "second", 30, 40)
        ]);

        Assert.True(store.CanPersistMutations);
        Assert.True(store.TryRead(10, 20, out WorldSign first));
        Assert.Equal(0, first.SlotId);
        Assert.Equal("first", first.Text);
        Assert.False(store.TryRead(9, 20, out _));
        Assert.False(store.TryRead(10, 19, out _));

        Assert.True(store.TryCaptureCanonicalSnapshot(out WorldSign[] snapshot));
        Assert.Equal(2, snapshot.Length);
        Assert.Equal("first", snapshot[0].Text);
        Assert.Equal("second", snapshot[1].Text);

        var update = new TerrariaSignState(0, 10, 20, "changed", 99, 1);
        Assert.True(store.TryApply(in update, out WorldSign committed, out bool changed));
        Assert.True(changed);
        Assert.Equal("changed", committed.Text);
        Assert.Equal("first", snapshot[0].Text);
    }

    [Fact]
    public void Tile_backed_store_normalizes_bottom_right_sign_frame_to_loaded_top_left_slot()
    {
        var tiles = new WorldTileStore(new WorldDimensions(50, 50));
        tiles.Set(10, 20, new WorldTile
        {
            Type = 55,
            FrameX = 0,
            FrameY = 0,
            Flags = WorldTileFlags.Active
        });
        tiles.Set(11, 21, new WorldTile
        {
            Type = 55,
            FrameX = 18,
            FrameY = 18,
            Flags = WorldTileFlags.Active
        });
        var store = new RuntimeSignStore([new WorldSign(0, "framed", 10, 20)], tiles);

        Assert.True(store.TryRead(11, 21, out WorldSign sign));
        Assert.Equal((short)0, sign.SlotId);
        Assert.Equal(10, sign.X);
        Assert.Equal(20, sign.Y);
        Assert.Equal("framed", sign.Text);
    }

    [Fact]
    public void Tile_backed_store_rejects_non_sign_tiles_even_when_coordinates_match_loaded_sign()
    {
        var tiles = new WorldTileStore(new WorldDimensions(50, 50));
        tiles.Set(10, 20, new WorldTile
        {
            Type = 54,
            FrameX = 0,
            FrameY = 0,
            Flags = WorldTileFlags.Active
        });
        var store = new RuntimeSignStore([new WorldSign(0, "stale", 10, 20)], tiles);

        Assert.False(store.TryRead(10, 20, out _));
    }

    [Fact]
    public void Sparse_source_remains_readable_but_rejects_mutation_and_semantic_persistence()
    {
        var store = new RuntimeSignStore(
        [
            new WorldSign(0, "zero", 10, 20),
            new WorldSign(2, "two", 30, 40)
        ]);

        Assert.False(store.CanPersistMutations);
        Assert.True(store.TryRead(30, 40, out WorldSign sign));
        Assert.Equal(2, sign.SlotId);

        var update = new TerrariaSignState(2, 30, 40, "changed", 0, 0);
        Assert.False(store.TryApply(in update, out _, out bool changed));
        Assert.False(changed);
        Assert.False(store.TryCaptureCanonicalSnapshot(out WorldSign[] snapshot));
        Assert.Empty(snapshot);
    }

    [Fact]
    public void Update_requires_matching_slot_and_coordinates()
    {
        var store = new RuntimeSignStore([new WorldSign(0, "old", 12, 34)]);

        var wrongSlot = new TerrariaSignState(1, 12, 34, "new", 0, 0);
        var wrongX = new TerrariaSignState(0, 13, 34, "new", 0, 0);
        var wrongY = new TerrariaSignState(0, 12, 35, "new", 0, 0);

        Assert.False(store.TryApply(in wrongSlot, out _, out _));
        Assert.False(store.TryApply(in wrongX, out _, out _));
        Assert.False(store.TryApply(in wrongY, out _, out _));
        Assert.True(store.TryRead(12, 34, out WorldSign unchanged));
        Assert.Equal("old", unchanged.Text);
    }

    [Fact]
    public void Identical_text_is_accepted_without_reporting_a_change()
    {
        var store = new RuntimeSignStore([new WorldSign(0, "same", 12, 34)]);
        var update = new TerrariaSignState(0, 12, 34, "same", 123, 1);

        Assert.True(store.TryApply(in update, out WorldSign committed, out bool changed));
        Assert.False(changed);
        Assert.Equal("same", committed.Text);
    }

    [Fact]
    public void Duplicate_runtime_coordinates_are_rejected_at_construction()
    {
        Assert.Throws<InvalidOperationException>(() => new RuntimeSignStore(
        [
            new WorldSign(0, "a", 1, 2),
            new WorldSign(1, "b", 1, 2)
        ]));
    }
}
