using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class GameplayExtensionRandomTests
{
    [Fact]
    public void Same_identity_produces_same_stream()
    {
        GameplayExtensionId id = new("vega:minigames.ctf");
        GameplayExtensionRandom first = GameplayExtensionRandom.ForEntity(1234, id, entitySlot: 7, entityGeneration: 19, stream: 2);
        GameplayExtensionRandom second = GameplayExtensionRandom.ForEntity(1234, id, entitySlot: 7, entityGeneration: 19, stream: 2);

        for (int index = 0; index < 16; index++)
            Assert.Equal(first.NextUInt64(), second.NextUInt64());
    }

    [Fact]
    public void Extension_entity_generation_and_stream_partition_randomness()
    {
        GameplayExtensionRandom baseline = GameplayExtensionRandom.ForEntity(
            1234,
            new GameplayExtensionId("vega:one"),
            entitySlot: 7,
            entityGeneration: 19,
            stream: 2);

        ulong expected = baseline.NextUInt64();

        Assert.NotEqual(expected, GameplayExtensionRandom.ForEntity(1234, new GameplayExtensionId("vega:two"), 7, 19, 2).NextUInt64());
        Assert.NotEqual(expected, GameplayExtensionRandom.ForEntity(1234, new GameplayExtensionId("vega:one"), 8, 19, 2).NextUInt64());
        Assert.NotEqual(expected, GameplayExtensionRandom.ForEntity(1234, new GameplayExtensionId("vega:one"), 7, 20, 2).NextUInt64());
        Assert.NotEqual(expected, GameplayExtensionRandom.ForEntity(1234, new GameplayExtensionId("vega:one"), 7, 19, 3).NextUInt64());
    }

    [Fact]
    public void Integer_and_single_helpers_stay_inside_requested_ranges()
    {
        GameplayExtensionRandom random = GameplayExtensionRandom.ForEntity(
            42,
            new GameplayExtensionId("test:ranges"),
            entitySlot: 0,
            entityGeneration: 1);

        for (int index = 0; index < 10_000; index++)
        {
            int zeroBased = random.NextInt32(7);
            int signed = random.NextInt32(-5, 9);
            float single = random.NextSingle();

            Assert.InRange(zeroBased, 0, 6);
            Assert.InRange(signed, -5, 8);
            Assert.True(single >= 0f && single < 1f);
        }
    }

    [Fact]
    public void Unassigned_extension_and_zero_generation_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => GameplayExtensionRandom.ForEntity(1, default, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameplayExtensionRandom.ForEntity(1, new GameplayExtensionId("test:id"), 0, 0));
    }
}
