using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerBootstrapFrameBudgetTests
{
    [Fact]
    public void Current_bootstrap_maximum_stays_below_live_probe_and_connection_queue_capacity()
    {
        Assert.Equal(200, WorldGlobalTownNpcBootstrapPacketEncoder.MaximumTownNpcs);
        Assert.Equal(63, PlayerBootstrapFrameBudget.MaximumTileSectionFrames);
        Assert.Equal(400, PlayerBootstrapFrameBudget.MaximumGlobalPostSectionFrames);
        Assert.Equal(800, PlayerBootstrapFrameBudget.MaximumDynamicEntityFrames);
        Assert.Equal(1_265, PlayerBootstrapFrameBudget.MaximumFramesBeforeEnterWorld);
        Assert.Equal(1_536, PlayerBootstrapFrameBudget.LiveProbeFrameBudget);
        Assert.True(
            PlayerBootstrapFrameBudget.MaximumFramesBeforeEnterWorld <=
            PlayerBootstrapFrameBudget.LiveProbeFrameBudget);
        Assert.True(PlayerBootstrapFrameBudget.LiveProbeFrameBudget < 4_096);
    }

    [Fact]
    public void Dynamic_entity_bootstrap_rejects_more_than_vanilla_world_item_slots()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RuntimeEntityBootstrapFrameSource(new OversizedWorldItemReader()));
    }

    [Fact]
    public void Global_town_npc_bootstrap_rejects_frames_beyond_vanilla_npc_capacity()
    {
        var npcs = new WorldTownNpc[WorldGlobalTownNpcBootstrapPacketEncoder.MaximumTownNpcs + 1];

        Assert.Equal(
            WorldGlobalTownNpcBootstrapPacketEncodeResult.FrameBudgetExceeded,
            WorldGlobalTownNpcBootstrapPacketEncoder.TryEncode(npcs, out ReadOnlyMemory<byte>[] frames));
        Assert.Empty(frames);
    }

    private sealed class OversizedWorldItemReader : IWorldItemSnapshotReader
    {
        public int Capacity => PlayerBootstrapFrameBudget.MaximumWorldItemSlots + 1;

        public int CopyActive(Span<WorldItemSnapshot> destination) => 0;
    }
}
