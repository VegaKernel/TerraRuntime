using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class PlayerBootstrapFrameSinkTests
{
    [Fact]
    public void Packet8_queues_all_sections_contiguously_before_global_state_and_packet49()
    {
        var slots = new PlayerSlotPool(1);
        var outbound = CreateOutbound();
        ReadOnlyMemory<byte>[] sections =
        [
            new byte[] { 3, 0, (byte)TerrariaMessageId.TileSection },
            new byte[] { 3, 0, (byte)TerrariaMessageId.TileSection }
        ];
        ReadOnlyMemory<byte>[] globalFrames =
        [
            new byte[] { 3, 0, 23 },
            new byte[] { 3, 0, 54 }
        ];
        using var sink = new PlayerBootstrapFrameSink(
            slots,
            outbound,
            PlayerBootstrapPacketSet.CreateForTesting(
                worldInfoFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                baseSectionFrames: sections,
                enterWorldFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf },
                globalPostSectionFrames: globalFrames));

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal((byte)TerrariaMessageId.PlayerInfo, DequeueMessageId(outbound));

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal((byte)TerrariaMessageId.WorldData, DequeueMessageId(outbound));

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));

        Assert.Equal(
            new byte[]
            {
                (byte)TerrariaMessageId.WorldData,
                (byte)TerrariaMessageId.StatusTextSize,
                (byte)TerrariaMessageId.TileSection,
                (byte)TerrariaMessageId.TileSection,
                23,
                54,
                (byte)TerrariaMessageId.PlayerSpawnSelf
            },
            DrainMessageIds(outbound));
        Assert.Equal(PlayerJoinState.AwaitingSpawn, sink.JoinState);
        Assert.Equal(PlayerBootstrapStopReason.None, sink.StopReason);
    }

    [Fact]
    public void Packet12_is_submitted_without_committing_playing_state_on_network_thread()
    {
        var slots = new PlayerSlotPool(1);
        var outbound = CreateOutbound();
        var ingress = new CapturingSpawnIngress();
        using var sink = new PlayerBootstrapFrameSink(
            slots,
            outbound,
            PlayerBootstrapPacketSet.CreateForTesting(
                worldInfoFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                baseSectionFrames: Array.Empty<ReadOnlyMemory<byte>>(),
                enterWorldFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            GameCommandSourceId.FromConnection(42),
            ingress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(PlayerJoinState.AwaitingWorldRequest, sink.JoinState);
        Assert.Equal((byte)0, sink.PlayerSlot);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(PlayerJoinState.AwaitingSectionRequest, sink.JoinState);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));
        Assert.Equal(PlayerJoinState.AwaitingSpawn, sink.JoinState);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(PlayerSpawn(claimedSlot: 0)));
        Assert.Equal(PlayerBootstrapStopReason.None, sink.StopReason);
        Assert.Equal(PlayerJoinState.AwaitingSpawn, sink.JoinState);
        Assert.Equal(1, ingress.PostCount);
        Assert.Equal(GameCommandSourceId.FromConnection(42), ingress.Source);
        Assert.Equal(new PlayerSlotId(0), ingress.Request.ClaimedSlot);
        Assert.Equal((short)100, ingress.Request.SpawnX);
        Assert.Equal((short)200, ingress.Request.SpawnY);

        PlayerJoinSession session = Assert.IsType<PlayerJoinSession>(ingress.Session);
        Assert.Equal(PlayerSpawnCommitResult.Committed, session.TryCommitSpawn(ingress.Request.ClaimedSlot));
        Assert.Equal(PlayerJoinState.Playing, sink.JoinState);
    }

    [Fact]
    public void Packet12_claiming_another_player_slot_is_rejected_before_game_ingress()
    {
        var slots = new PlayerSlotPool(1);
        var ingress = new CapturingSpawnIngress();
        using var sink = new PlayerBootstrapFrameSink(
            slots,
            CreateOutbound(),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            GameCommandSourceId.FromConnection(7),
            ingress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(PlayerSpawn(claimedSlot: 1)));
        Assert.Equal(PlayerBootstrapStopReason.PlayerSlotMismatch, sink.StopReason);
        Assert.Equal(0, ingress.PostCount);
        Assert.Null(sink.JoinState);
        Assert.Equal(0, slots.LeasedCount);
    }

    [Fact]
    public void Packet12_with_invalid_spawn_data_is_rejected_before_game_ingress()
    {
        var slots = new PlayerSlotPool(1);
        var ingress = new CapturingSpawnIngress();
        using var sink = new PlayerBootstrapFrameSink(
            slots,
            CreateOutbound(),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            GameCommandSourceId.FromConnection(7),
            ingress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(PlayerSpawn(claimedSlot: 0, team: 6)));
        Assert.Equal(PlayerBootstrapStopReason.MalformedPlayerSpawn, sink.StopReason);
        Assert.Equal(0, ingress.PostCount);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 4_096, maxFrameBytes: 1_024));

    private static byte DequeueMessageId(TerrariaConnectionOutboundQueue outbound)
    {
        Assert.True(outbound.InnerQueue.TryRead(out OutboundFrame frame));
        Assert.True(frame.Bytes.Length >= TerrariaFrameDecoderOptions.MinimumFrameLength);
        return frame.Bytes.Span[2];
    }

    private static byte[] DrainMessageIds(TerrariaConnectionOutboundQueue outbound)
    {
        var ids = new List<byte>();
        while (outbound.InnerQueue.TryRead(out OutboundFrame frame))
        {
            Assert.True(frame.Bytes.Length >= TerrariaFrameDecoderOptions.MinimumFrameLength);
            ids.Add(frame.Bytes.Span[2]);
        }

        return ids.ToArray();
    }

    private static TerrariaFrame Hello() =>
        Frame(
            TerrariaMessageId.Hello,
            new byte[]
            {
                11,
                (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
                (byte)'3', (byte)'2', (byte)'6'
            });

    private static TerrariaFrame PlayerSpawn(byte claimedSlot, byte team = 0)
    {
        byte[] payload = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        payload[0] = claimedSlot;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), 100);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 200);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5), 0);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(9), 0);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(11), 0);
        payload[13] = team;
        payload[14] = 0;
        return Frame(TerrariaMessageId.PlayerSpawn, payload);
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private sealed class CapturingSpawnIngress : IPlayerSpawnCommitIngress
    {
        public int PostCount { get; private set; }
        public GameCommandSourceId Source { get; private set; }
        public PlayerJoinSession? Session { get; private set; }
        public PlayerSpawnCommitRequest Request { get; private set; }

        public bool TryPost(
            GameCommandSourceId source,
            PlayerJoinSession session,
            in PlayerSpawnCommitRequest request)
        {
            PostCount++;
            Source = source;
            Session = session;
            Request = request;
            return true;
        }
    }
}