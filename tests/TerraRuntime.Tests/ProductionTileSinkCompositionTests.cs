using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ProductionTileSinkCompositionTests
{
    [Fact]
    public void Production_chest_outer_sink_routes_packet17_through_projectile_tile_composition()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(905);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var commands = new RecordingCommandIngress();
        var gameplayIngress = new RuntimeProjectileNetworkIngress(commands);
        var projectileSink = new ProjectileLifecycleFrameSink(
            source,
            bootstrap,
            new PassthroughSink(),
            gameplayIngress);
        var chestSink = new ChestInteractionFrameSink(
            source,
            bootstrap,
            projectileSink,
            new AcceptingChestIngress());
        var state = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTile,
            40,
            50,
            0,
            0);

        Assert.Equal(TerrariaFrameSinkResult.Continue, chestSink.OnFrame(Packet17(in state)));

        ClientTileManipulationRuntimeCommand command =
            Assert.IsType<ClientTileManipulationRuntimeCommand>(commands.Command);
        Assert.Equal(source, commands.Source);
        Assert.Equal(source, command.Connection.Source);
        Assert.Equal(bootstrap.AssignedPlayerHandle, command.Connection.Player);
        Assert.Equal(state, command.State);
        Assert.Equal(TileManipulationFrameStopReason.None, projectileSink.TileStopReason);
        Assert.Equal(ProjectileLifecycleFrameStopReason.None, projectileSink.StopReason);
        Assert.Equal(ChestInteractionFrameStopReason.None, chestSink.StopReason);
    }

    private static TerrariaFrame Packet17(in TerrariaTileManipulationState state)
    {
        Assert.Equal(
            TerrariaTileManipulationEncodeResult.Encoded,
            TerrariaTileManipulationCodec.TryEncode(in state, out byte[] encoded));
        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        return frame;
    }

    private static PlayerBootstrapFrameSink CreatePlayingBootstrap(GameCommandSourceId source)
    {
        var bootstrap = new PlayerBootstrapFrameSink(
            new PlayerSlotPool(1),
            new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 8_192, maxFrameBytes: 2_048)),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            source,
            new CommittingSpawnIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(PlayerSpawn()));
        Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
        return bootstrap;
    }

    private static TerrariaFrame Hello() =>
        Frame(
            TerrariaMessageId.Hello,
            [
                11,
                (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
                (byte)'3', (byte)'2', (byte)'6'
            ]);

    private static TerrariaFrame PlayerSpawn()
    {
        byte[] payload = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        payload[0] = 0;
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), 100);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 200);
        return Frame(TerrariaMessageId.PlayerSpawn, payload);
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private sealed class RecordingCommandIngress : IGameCommandIngress<RuntimeCommand>
    {
        public GameCommandSourceId Source { get; private set; }
        public RuntimeCommand? Command { get; private set; }

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Source = source;
            Command = command;
            return true;
        }
    }

    private sealed class CommittingSpawnIngress : IPlayerSpawnCommitIngress
    {
        public bool TryPost(
            GameCommandSourceId source,
            PlayerJoinSession session,
            in PlayerSpawnCommitRequest request) =>
            session.TryCommitSpawn(request.ClaimedSlot) == PlayerSpawnCommitResult.Committed;
    }

    private sealed class AcceptingChestIngress : IChestNetworkIngress
    {
        public bool TryPostOpen(ConnectionHandle connection, in TerrariaChestOpenRequest request) => true;

        public bool TryPostItem(ConnectionHandle connection, in TerrariaChestItemState state) => true;

        public bool TryPostActiveState(ConnectionHandle connection, in TerrariaActiveChestState state) => true;

        public bool TryPostNameLookup(ConnectionHandle connection, in TerrariaChestNameLookupRequest request) => true;
    }

    private sealed class PassthroughSink : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }
}
