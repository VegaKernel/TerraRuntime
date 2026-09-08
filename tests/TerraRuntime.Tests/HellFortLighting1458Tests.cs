using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class HellFortLighting1458Tests
{
    [Theory]
    [InlineData(4200, 200)]
    [InlineData(6400, 200)]
    [InlineData(8400, 400)]
    public void Source_count_uses_integer_division_before_float_conversion(int width, int count) =>
        Assert.Equal(count, HellFortLighting1458.AttemptCount(width));

    [Theory]
    [InlineData(-1, 44)]
    [InlineData(1, 22)]
    public void Torch_attaches_to_brick_with_source_frame_and_cleans_only_block_presentation(int side, int frameX)
    {
        WorldTileStore store = Site(side);
        ref WorldTile target = ref At(store, 100 + side, 200);
        target.Flags = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.WireBlue | WorldTileFlags.Inactive;
        target.TileColor = 7; target.Shape = 1; target.WallColor = 9;
        Assert.True(HellFortLighting1458.TryPlaceCandidate(store, 100, 200, out bool placed));
        Assert.True(placed);
        Assert.True(target.IsActive); Assert.Equal(4, target.Type);
        Assert.Equal(frameX, target.FrameX); Assert.Equal(154, target.FrameY);
        Assert.Equal(0, target.TileColor); Assert.Equal(0, target.Shape);
        Assert.False(target.IsBlockInvisible); Assert.False(target.IsBlockFullbright);
        Assert.Equal(14, target.Wall); Assert.Equal(9, target.WallColor);
        Assert.True(target.IsActuated); Assert.True((target.Flags & WorldTileFlags.WireBlue) != 0);
    }

    [Theory]
    [InlineData("wrong-brick")]
    [InlineData("inactive-brick")]
    [InlineData("no-wall")]
    [InlineData("target-blocked")]
    [InlineData("below-blocked")]
    [InlineData("left-wins")]
    public void Invalid_sample_does_not_write_or_complete_attempt(string reason)
    {
        WorldTileStore store = Site(1);
        switch (reason)
        {
            case "wrong-brick": At(store, 100, 200).Type = 57; break;
            case "inactive-brick": At(store, 100, 200).Flags = 0; break;
            case "no-wall": At(store, 101, 200).Wall = 0; break;
            case "target-blocked": At(store, 101, 200).Flags |= WorldTileFlags.Active; break;
            case "below-blocked": At(store, 101, 201).Flags |= WorldTileFlags.Active; break;
            case "left-wins": At(store, 99, 200) = new WorldTile { Wall = 13, Flags = WorldTileFlags.Active }; break;
        }
        WorldTile[] before = store.Tiles.ToArray();
        Assert.False(HellFortLighting1458.TryPlaceCandidate(store, 100, 200, out bool placed));
        Assert.False(placed); Assert.Equal(before, store.Tiles.ToArray());
    }

    [Theory]
    [InlineData(-8, true, false)]
    [InlineData(7, true, false)]
    [InlineData(8, true, true)]
    [InlineData(-9, true, true)]
    [InlineData(0, false, true)]
    public void Nearby_torch_exclusion_is_half_open_and_centered_on_sampled_brick(int offset, bool active, bool expected)
    {
        WorldTileStore store = Site(1);
        At(store, 100 + offset, 194) = new WorldTile { Type = 4, Flags = active ? WorldTileFlags.Active : 0 };
        Assert.Equal(expected, HellFortLighting1458.TryPlaceCandidate(store, 100, 200, out bool placed));
        Assert.Equal(expected, placed);
    }

    [Theory]
    [InlineData(2, 0)] // Vanilla left-support slope1 rejects the attachment; background wall remains.
    [InlineData(3, 22)]
    [InlineData(4, 0)]
    [InlineData(5, 22)]
    [InlineData(1, 22)] // Half brick still supports a side torch.
    public void CheckTorch_runs_during_generation_and_uses_side_slope_parity(byte shape, int frameX)
    {
        WorldTileStore store = Site(1);
        At(store, 100, 200).Shape = shape;
        Assert.True(HellFortLighting1458.TryPlaceCandidate(store, 100, 200, out bool placed));
        Assert.True(placed); Assert.Equal(frameX, store.Get(101, 200).FrameX);
    }

    [Theory]
    [InlineData(10, 44)] // tileNoAttach door cannot override the right-hand brick.
    [InlineData(75, 22)]
    [InlineData(124, 22)] // Beam is admitted independently of solidity.
    [InlineData(19, 44)]
    public void CheckTorch_prefers_eligible_left_support_over_selected_right_support(ushort leftType, int frameX)
    {
        WorldTileStore store = Site(-1);
        At(store, 98, 200) = new WorldTile { Type = leftType, Flags = WorldTileFlags.Active };
        Assert.True(HellFortLighting1458.TryPlaceCandidate(store, 100, 200, out bool placed));
        Assert.True(placed); Assert.Equal(frameX, store.Get(99, 200).FrameX);
    }

    [Fact]
    public void Wet_candidate_finishes_each_attempt_without_placement_or_cleanup()
    {
        var store = new WorldTileStore(new WorldDimensions(4200, 400));
        At(store, 1000, 200) = new WorldTile { Type = 76, Flags = WorldTileFlags.Active };
        At(store, 1001, 200) = new WorldTile { Wall = 13, LiquidAmount = 1, TileColor = 8 };
        var random = new RepeatedSample(840, 3360, 1000, 200);
        Assert.Equal(0, HellFortLighting1458.Generate(store, random, TestContext.Current.CancellationToken));
        Assert.Equal(400, random.Calls);
        Assert.Equal(8, store.Get(1001, 200).TileColor); Assert.Equal(1, store.Get(1001, 200).LiquidAmount);
    }

    [Fact]
    public void Empty_world_uses_exact_1001_samples_per_attempt_without_inventing_a_torch()
    {
        var store = new WorldTileStore(new WorldDimensions(4200, 400));
        var random = new RepeatedSample(840, 3360, 1000, 200);
        Assert.Equal(0, HellFortLighting1458.Generate(store, random, TestContext.Current.CancellationToken));
        Assert.Equal(200 * 1001 * 2, random.Calls);
    }

    private static WorldTileStore Site(int side)
    {
        var store = new WorldTileStore(new WorldDimensions(200, 400));
        At(store, 100, 200) = new WorldTile { Type = 75, Flags = WorldTileFlags.Active };
        At(store, 100 + side, 200).Wall = 14;
        return store;
    }
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

    private sealed class RepeatedSample(int minX, int maxX, int x, int y) : IWorldGenerationVanillaRandom
    {
        public int Calls { get; private set; }
        public int Next(int minValue, int maxValue)
        {
            bool isX = Calls++ % 2 == 0;
            Assert.Equal(isX ? (minX, maxX) : (100, 380), (minValue, maxValue));
            return isX ? x : y;
        }
        public int Next(int maxValue) => throw new NotSupportedException();
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
    }
}
