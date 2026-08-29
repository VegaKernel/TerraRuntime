using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaDirtWorldItemDropTests
{
    [Fact]
    public void Creates_source_backed_packet21_state_and_requests_rng_in_vanilla_order()
    {
        var random = new RecordingRandom(x: -7, y: -21);

        WorldItemDropStateUpdate drop = VanillaDirtWorldItemDrop.Create(10, 20, random);

        Assert.Equal(162f, drop.PositionX);
        Assert.Equal(322f, drop.PositionY);
        Assert.Equal(-0.7f, drop.VelocityX);
        Assert.Equal(-2.1f, drop.VelocityY);
        Assert.Equal(1, drop.Stack);
        Assert.Equal(0, drop.Prefix);
        Assert.Equal(WorldItemOwnershipMode.None, drop.Ownership);
        Assert.Equal(VanillaItemIds.DirtBlock.Value, drop.ItemNetId);
        Assert.False(drop.Shimmered);
        Assert.Equal(0f, drop.ShimmerTime);
        Assert.Equal(0, drop.EnemyGrabDelayTime);
        Assert.Equal(
            [(-30, 31), (-40, -15)],
            random.Requests);
    }

    private sealed class RecordingRandom(int x, int y) : IWorldItemSpawnRandom
    {
        private readonly int[] values = [x, y];
        private int index;

        public List<(int Min, int Max)> Requests { get; } = [];

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Requests.Add((inclusiveMin, exclusiveMax));
            int value = values[index++];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
