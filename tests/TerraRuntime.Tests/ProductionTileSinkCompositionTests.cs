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


    [Theory]
    [InlineData(3509, false)] // Copper Pickaxe
    [InlineData(385, false)] // Cobalt Drill
    [InlineData(2779, false)] // Nebula Drill
    [InlineData(2784, false)] // Solar Flare Drill
    [InlineData(3464, false)] // Stardust Drill
    [InlineData(2768, true)] // Drill Containment Unit / mount 8
    public void Production_vanilla_join_packet5_packet13_packet17_path_breaks_dirt(
        short miningItemType,
        bool drillMount)
    {
        var tiles = new TerraRuntime.World.WorldTileStore(new TerraRuntime.World.WorldDimensions(200, 150));
        var state = new ServerRuntimeState(worldTiles: tiles);
        var slots = new PlayerSlotPool(1);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(907);
        var immediate = new ApplyingCommandIngress(state);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 8_192, maxFrameBytes: 2_048));
        using var bootstrap = new PlayerBootstrapFrameSink(
            slots,
            outbound,
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            source,
            new RuntimePlayerSpawnCommitIngress(immediate),
            appearanceIngress: null,
            new RuntimePlayerEquipmentIngress(immediate),
            new RuntimePlayerMovementIngress(immediate));
        var gameplayIngress = new RuntimeProjectileNetworkIngress(immediate);
        var projectileSink = new ProjectileLifecycleFrameSink(
            source,
            bootstrap,
            new PassthroughSink(),
            gameplayIngress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Hello()));

        // Vanilla 1.4.5.8 client MessageBuffer case 3 sends all inventory slots 0..58 before packet 6/world request.
        const short miningSlot = 7;
        for (short slot = 0; slot < TerraRuntime.Gameplay.Items.VanillaPlayerItemSlotCatalog.InventoryCount; slot++)
        {
            bool isMiningItem = slot == miningSlot;
            var equipment = new TerrariaPlayerEquipmentState(
                PlayerId: 0,
                SlotId: slot,
                Stack: isMiningItem ? (short)1 : (short)0,
                Prefix: 0,
                ItemNetId: isMiningItem ? miningItemType : (short)0,
                ItemFlags: 0);
            Assert.Equal(
                TerrariaFrameSinkResult.Continue,
                bootstrap.OnFrame(Decode(TerrariaPlayerEquipmentCodec.Encode(in equipment))));
        }
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(PlayerSpawn()));
        Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);

        var movement = new TerrariaPlayerMovementState(
            PlayerId: 0,
            ControlFlags: 1 << 5,
            MovementFlags: drillMount ? (byte)(1 << 7) : (byte)0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: drillMount ? (byte)0 : (byte)miningSlot,
            PositionX: 800f,
            PositionY: 800f,
            HasVelocity: false,
            VelocityX: 0f,
            VelocityY: 0f,
            HasMount: drillMount,
            MountType: drillMount ? (ushort)8 : (ushort)0,
            HasPotionOfReturnPositions: false,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            HasCameraTarget: false,
            CameraTargetX: 0f,
            CameraTargetY: 0f);
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Decode(TerrariaPlayerMovementEncoder.Encode(in movement))));

        PlayerHandle player = Assert.IsType<PlayerHandle>(bootstrap.AssignedPlayerHandle);
        Assert.True(state.TryCapturePlayerSnapshot(player, out PlayerStateSnapshot livePlayer));
        Assert.Equal(drillMount ? (ushort)8 : (ushort)0, livePlayer.MountType);
        Assert.Equal((byte)(1 << 5), livePlayer.ControlFlags);
        Assert.Equal(drillMount ? (byte)0 : (byte)miningSlot, livePlayer.SelectedItem);
        Assert.True(state.TryCapturePlayerInventoryItem(player, miningSlot, out RuntimePlayerInventoryItem liveItem));
        Assert.Equal(miningItemType, liveItem.ItemType.Value);

        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(tiles, 50, 52));
        var kill = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTile,
            TileX: 50,
            TileY: 52,
            Data: 0,
            Style: 0);
        Assert.Equal(TerrariaFrameSinkResult.Continue, projectileSink.OnFrame(Packet17(in kill)));

        Assert.Equal(default, tiles.Get(50, 52));
        Assert.Equal(1, state.AppliedClientTileManipulations);
        Assert.Equal(0, state.RejectedClientTileManipulations);
    }

    [Fact]
    public void Production_chest_outer_sink_routes_packet79_through_projectile_object_composition()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(906);
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
        var state = new TerrariaPlaceObjectState(
            TileX: 40,
            TileY: 50,
            TileType: 21,
            Style: 0,
            Alternate: 0,
            Random: -1,
            Direction: false);

        Assert.Equal(TerrariaFrameSinkResult.Continue, chestSink.OnFrame(Packet79(in state)));

        ClientPlaceObjectRuntimeCommand command =
            Assert.IsType<ClientPlaceObjectRuntimeCommand>(commands.Command);
        Assert.Equal(source, commands.Source);
        Assert.Equal(source, command.Connection.Source);
        Assert.Equal(bootstrap.AssignedPlayerHandle, command.Connection.Player);
        Assert.Equal(state, command.State);
        Assert.Equal(ObjectPlacementFrameStopReason.None, projectileSink.ObjectPlacementStopReason);
        Assert.Equal(TileManipulationFrameStopReason.None, projectileSink.TileStopReason);
        Assert.Equal(ProjectileLifecycleFrameStopReason.None, projectileSink.StopReason);
        Assert.Equal(ChestInteractionFrameStopReason.None, chestSink.StopReason);
    }

    private static TerrariaFrame Packet17(in TerrariaTileManipulationState state)
    {
        Assert.Equal(
            TerrariaTileManipulationEncodeResult.Encoded,
            TerrariaTileManipulationCodec.TryEncode(in state, out byte[] encoded));
        return Decode(encoded);
    }

    private static TerrariaFrame Packet79(in TerrariaPlaceObjectState state)
    {
        Assert.Equal(
            TerrariaPlaceObjectEncodeResult.Encoded,
            TerrariaPlaceObjectCodec.TryEncode(in state, out byte[] encoded));
        return Decode(encoded);
    }

    private static TerrariaFrame Decode(byte[] encoded)
    {
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


    private sealed class ApplyingCommandIngress(ServerRuntimeState state) : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            state.Apply(command);
            return true;
        }
    }

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
