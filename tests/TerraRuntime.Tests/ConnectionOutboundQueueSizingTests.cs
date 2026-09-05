using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ConnectionOutboundQueueSizingTests
{
    [Fact]
    public void Default_eight_player_budget_matches_the_current_join_and_replication_envelope()
    {
        Assert.Equal(69, ConnectionOutboundQueueSizing.MaximumInitialJoinFrames);
        Assert.Equal(1_657, ConnectionOutboundQueueSizing.MaximumRuntimeEntityBaselineFrames);
        Assert.Equal(394, ConnectionOutboundQueueSizing.MaximumOtherPlayerBaselineFramesPerSlot);
        Assert.Equal(4_484, ConnectionOutboundQueueSizing.DefaultStructuralFrameBudget);

        OutboundQueueOptions options = ConnectionOutboundQueueSizing.Create(ServerHostOptions.DefaultMaxPlayers);

        Assert.Equal(4_484, options.MaxFrames);
        Assert.Equal(16L * 1024 * 1024, options.MaxQueuedBytes);
        Assert.Equal(TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength, options.MaxFrameBytes);
    }

    [Fact]
    public void Configured_player_count_scales_frame_and_byte_budgets_instead_of_reusing_4096()
    {
        OutboundQueueOptions eightPlayers = ConnectionOutboundQueueSizing.Create(8);
        OutboundQueueOptions ninePlayers = ConnectionOutboundQueueSizing.Create(9);
        OutboundQueueOptions maximumPlayers = ConnectionOutboundQueueSizing.Create(byte.MaxValue);

        Assert.Equal(4_484, eightPlayers.MaxFrames);
        Assert.Equal(4_878, ninePlayers.MaxFrames);
        Assert.True(ninePlayers.MaxFrames > 4_096);
        Assert.True(ninePlayers.MaxQueuedBytes > eightPlayers.MaxQueuedBytes);
        Assert.True(maximumPlayers.MaxFrames > ninePlayers.MaxFrames);
        Assert.True(maximumPlayers.MaxQueuedBytes > ninePlayers.MaxQueuedBytes);
    }

    [Fact]
    public void Single_player_configuration_still_covers_join_and_runtime_entity_baselines()
    {
        OutboundQueueOptions options = ConnectionOutboundQueueSizing.Create(1);

        Assert.Equal(
            ConnectionOutboundQueueSizing.MaximumInitialJoinFrames +
            ConnectionOutboundQueueSizing.MaximumRuntimeEntityBaselineFrames,
            options.MaxFrames);
        Assert.True(options.MaxQueuedBytes >= TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Invalid_player_counts_are_rejected(int maxPlayers)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConnectionOutboundQueueSizing.Create(maxPlayers));
    }
}
