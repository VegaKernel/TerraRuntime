using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringAnimatedDevice1458Tests
{
    public static IEnumerable<object[]> DamagedFootprints()
    {
        foreach (ushort type in new ushort[] { 105, 349, 593, 594 })
        {
            int cells = type == 593 ? 1 : type == 594 ? 4 : 6;
            for (int cell = 0; cell < cells; cell++)
                foreach (string damage in new[] { "type", "frame-x", "frame-y", "inactive", "actuator", "wet" })
                    yield return [type, cell, damage];
        }
    }

    [Theory, MemberData(nameof(DamagedFootprints))]
    public void Malformed_footprints_refuse_before_writing_or_emitting_animation(ushort type, int cell, string damage)
    {
        var tiles = new WorldTileStore(new(80, 100)); var size = PlaceDevice(tiles, type);
        int x = 30 + cell / size.Height, y = 30 + cell % size.Height;
        var broken = tiles.Get(x, y);
        if (damage == "type") broken.Type = 1;
        if (damage == "frame-x") broken.FrameX++;
        if (damage == "frame-y") broken.FrameY++;
        if (damage == "inactive") broken.Flags |= WorldTileFlags.Inactive;
        if (damage == "actuator") broken.Flags |= WorldTileFlags.Actuator;
        if (damage == "wet") broken.LiquidAmount = 1;
        tiles.Set(x, y, in broken);
        var before = Cells(tiles, size.Width, size.Height); int published = 0, skipped = 0;
        var executor = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (_, _, _, _) => published++,
            applyActuatorTransaction: (_, _) => false, publishTemporaryAnimation: (_, _, _, _) => published++);
        Assert.False(executor.Hit(30, 30, WorldWireChannel.Red, (_, _) => skipped++));
        Assert.Equal(before, Cells(tiles, size.Width, size.Height)); Assert.Equal(0, published); Assert.Equal(0, skipped);
    }

    public static IEnumerable<object[]> ReentrantFootprints()
    {
        foreach (ushort type in new ushort[] { 105, 349, 593, 594 })
            foreach (string mutation in new[] { "cell", "aba", "section" }) yield return [type, mutation];
    }

    [Theory, MemberData(nameof(ReentrantFootprints))]
    public void Skip_bookkeeping_cannot_overwrite_a_changed_preimage_or_section(ushort type, string mutation)
    {
        var tiles = new WorldTileStore(new(80, 100)); var size = PlaceDevice(tiles, type);
        int published = 0, skipped = 0; var before = Cells(tiles, size.Width, size.Height);
        var executor = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (_, _, _, _) => published++,
            publishTemporaryAnimation: (_, _, _, _) => published++);
        Assert.False(executor.Hit(30, 30, WorldWireChannel.Red, (_, _) =>
        {
            if (++skipped != size.Width * size.Height) return;
            int x = mutation == "section" ? 10 : 30, y = mutation == "section" ? 10 : 30;
            var old = tiles.Get(x, y); var changed = old; changed.Wall = 2;
            tiles.Set(x, y, in changed);
            if (mutation == "aba") tiles.Set(x, y, in old);
        }));
        var expected = before.ToArray(); if (mutation == "cell") expected[0].Wall = 2;
        Assert.Equal(expected, Cells(tiles, size.Width, size.Height)); Assert.Equal(0, published);
        Assert.Equal(size.Width * size.Height, skipped);
    }

    [Theory]
    [InlineData(105)] [InlineData(349)] [InlineData(593)] [InlineData(594)]
    public void Every_publication_observes_the_complete_committed_object_with_source_event_order(ushort type)
    {
        var tiles = new WorldTileStore(new(80, 100)); var size = PlaceDevice(tiles, type); var events = new List<int>();
        var before = Cells(tiles, size.Width, size.Height);
        void Observe(int id)
        {
            events.Add(id);
            for (int dx = 0; dx < size.Width; dx++) for (int dy = 0; dy < size.Height; dy++)
            {
                var tile = tiles.Get(30 + dx, 30 + dy);
                Assert.Equal(type == 105 ? (ushort)349 : type, tile.Type);
                Assert.Equal((type is 105 or 349 ? 216 : size.Width * 18) + dx * 18, tile.FrameX);
                Assert.Equal(before[dx * size.Height + dy].Flags, tile.Flags);
            }
        }
        var executor = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (_, _, _, _) => Observe(20),
            publishTemporaryAnimation: (_, _, _, _) => Observe(77));
        Assert.True(executor.Hit(30, 30, WorldWireChannel.Red, (_, _) => { }));
        Assert.Equal(type == 105 ? new[] { 77, 20 } : type == 349 ? new[] { 20, 77 } : new[] { 20, 77, 77 }, events);
    }

    [Theory]
    [InlineData(105)] [InlineData(349)] [InlineData(593)] [InlineData(594)]
    public void Unrepresentable_animation_coordinates_cannot_leave_a_material_change(ushort type)
    {
        var tiles = new WorldTileStore(new(32771, 3)); PlaceDevice(tiles, type, left: 32768, top: 0);
        var before = tiles.Get(32768, 0); int published = 0;
        var executor = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (_, _, _, _) => published++,
            publishTemporaryAnimation: (_, _, _, _) => published++);
        Assert.False(executor.Hit(32768, 0, WorldWireChannel.Red, (_, _) => { }));
        Assert.Equal(before, tiles.Get(32768, 0)); Assert.Equal(0, published);
        Assert.False(TerrariaTemporaryAnimationCodec1458.TryEncode(0, 349, 32768, 0, out var frame)); Assert.Empty(frame);
    }

    internal static (int Width, int Height) PlaceDevice(WorldTileStore tiles, ushort type, int state = 0, int left = 30, int top = 30)
    {
        int width = type == 593 ? 1 : 2, height = type == 593 ? 1 : type == 594 ? 2 : 3;
        int frame = type == 105 ? 34 * 36 : state * (type == 349 ? 216 : width * 18);
        for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++)
            tiles.Set(left + dx, top + dy, new WorldTile { Type = type, FrameX = (short)(frame + dx * 18),
                FrameY = (short)(dy * 18), Wall = 1, TileColor = 3, WallColor = 2,
                Flags = WorldTileFlags.Active | WorldTileFlags.WireRed | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock });
        return (width, height);
    }

    private static WorldTile[] Cells(WorldTileStore tiles, int width, int height) =>
        Enumerable.Range(0, width * height).Select(i => tiles.Get(30 + i / height, 30 + i % height)).ToArray();
}
