using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class PlayerMovementAuthorityTests
{
    [Fact]
    public void Movement_uses_assigned_slot_and_can_queue_immediately_after_spawn_submission()
    {
        var slots = new PlayerSlotPool(1);
        var spawnIngress = new AcceptingSpawnIngress();
        var movementIngress = new CapturingMovementIngress();
        using var sink = new PlayerBootstrapFrameSink(
            slots,
            CreateOutbound(),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            GameCommandSourceId.FromConnection(42),
            spawnIngress,
            movementIngress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(PlayerSpawn(claimedSlot: 0)));
        Assert.Equal(PlayerJoinState.AwaitingSpawn, sink.JoinState);
        Assert.Equal(1, spawnIngress.PostCount);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(PlayerMovement(claimedSlot: 99, x: 123.5f, y: 456.25f)));

        Assert.Equal(PlayerBootstrapStopReason.None, sink.StopReason);
        Assert.Equal(1, movementIngress.PostCount);
        Assert.Equal(GameCommandSourceId.FromConnection(42), movementIngress.Connection.Source);
        Assert.True(movementIngress.Connection.Player.Generation.IsAssigned);
        Assert.Equal(new PlayerSlotId(0), movementIngress.Request.PlayerSlot);
        Assert.Equal(123.5f, movementIngress.Request.PositionX);
        Assert.Equal(456.25f, movementIngress.Request.PositionY);
    }

    [Fact]
    public void Movement_before_spawn_submission_is_ignored()
    {
        var movementIngress = new CapturingMovementIngress();
        using var sink = new PlayerBootstrapFrameSink(
            new PlayerSlotPool(1),
            CreateOutbound(),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            GameCommandSourceId.FromConnection(7),
            new AcceptingSpawnIngress(),
            movementIngress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(PlayerMovement(claimedSlot: 0, x: 1f, y: 2f)));
        Assert.Equal(0, movementIngress.PostCount);
        Assert.Equal(PlayerBootstrapStopReason.None, sink.StopReason);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 8_192, maxFrameBytes: 2_048));

    private static TerrariaFrame Hello() =>
        Frame(
            TerrariaMessageId.Hello,
            [
                11,
                (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
                (byte)'3', (byte)'2', (byte)'6'
            ]);

    private static TerrariaFrame PlayerSpawn(byte claimedSlot)
    {
        byte[] payload = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        payload[0] = claimedSlot;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), 100);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 200);
        return Frame(TerrariaMessageId.PlayerSpawn, payload);
    }

    private static TerrariaFrame PlayerMovement(byte claimedSlot, float x, float y)
    {
        byte[] payload = new byte[TerrariaPlayerMovementDecoder.MinimumPayloadLength];
        payload[0] = claimedSlot;
        payload[5] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(6), BitConverter.SingleToInt32Bits(x));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(10), BitConverter.SingleToInt32Bits(y));
        return Frame(TerrariaMessageId.PlayerControls, payload);
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private sealed class AcceptingSpawnIngress : IPlayerSpawnCommitIngress
    {
        public int PostCount { get; private set; }

        public bool TryPost(
            GameCommandSourceId source,
            PlayerJoinSession session,
            in PlayerSpawnCommitRequest request)
        {
            PostCount++;
            return true;
        }
    }

    private sealed class CapturingMovementIngress : IPlayerMovementIngress
    {
        public int PostCount { get; private set; }
        public ConnectionHandle Connection { get; private set; }
        public PlayerMovementCommitRequest Request { get; private set; }

        public bool TryPost(ConnectionHandle connection, in PlayerMovementCommitRequest request)
        {
            PostCount++;
            Connection = connection;
            Request = request;
            return true;
        }
    }
}
