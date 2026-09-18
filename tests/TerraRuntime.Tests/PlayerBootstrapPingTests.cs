using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

/// <summary>
/// The client's latency probe, packet <c>154</c>.
/// </summary>
/// <remarks>
/// <c>Terraria.Net.Ping</c> sends exactly one probe and then waits: while it waits it keeps raising the number
/// it displays and never sends another. A server that does not echo the packet therefore shows a latency that
/// climbs forever off a single unanswered frame, with no traffic at all to explain it. TerrariaServer 1.4.5.8
/// replies with <c>NetMessage.TrySendData(154, whoAmI)</c>, which is an empty packet 154 straight back.
/// </remarks>
public sealed class PlayerBootstrapPingTests
{
    [Fact]
    public void Ping_is_echoed_back_unchanged()
    {
        var slots = new PlayerSlotPool(4);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 4_096, maxFrameBytes: 1_024));
        using var sink = new PlayerBootstrapFrameSink(
            slots,
            outbound,
            PlayerBootstrapPacketSet.CreateForTesting(
                worldInfoFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                baseSectionFrames: [],
                enterWorldFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf },
                globalPostSectionFrames: []));

        TerrariaFrame hello = Decode(CurrentHelloPacket());
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in hello));
        int afterHello = outbound.QueuedFrames;

        TerrariaFrame ping = Decode([3, 0, (byte)TerrariaMessageId.Ping]);
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in ping));

        Assert.Equal(afterHello + 1, outbound.QueuedFrames);
        Assert.Equal(PlayerBootstrapStopReason.None, sink.StopReason);
    }

    private static TerrariaFrame Decode(byte[] bytes)
    {
        var sequence = new System.Buffers.ReadOnlySequence<byte>(bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        return frame;
    }

    private static byte[] CurrentHelloPacket() =>
    [
        15, 0,
        (byte)TerrariaMessageId.Hello,
        11,
        (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
        (byte)'3', (byte)'2', (byte)'6'
    ];
}
