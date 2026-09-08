using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class OceanTeleport1458Tests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void Literal_shellphone_packet73_reaches_world_composition_and_packet65(bool skyblock, bool mounted, bool fails)
    {
        var registry = new RuntimeConnectionRegistry();
        var state = new ServerRuntimeState(playerEvents: registry, worldTiles: Coast(),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 150, SkyblockWorld = skyblock });
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var source = GameCommandSourceId.FromConnection(886);
        var connection = new ConnectionHandle(source, session.Handle);
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 16384));
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(outbound));
        Assert.True(registry.TryRegister(source, outbound));
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 300, 100, 0, 0, 0, 0, 0)));
        using var bootstrap = new PlayerBootstrapFrameSink(slots, outbound,
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] {3,0,7}, Array.Empty<ReadOnlyMemory<byte>>(), new byte[] {3,0,49}));
        bootstrap.AdoptPlayingSession(session, playerName: null);
        if (mounted)
            state.Apply(new PlayerMovementRuntimeCommand(connection, new PlayerMovementCommitRequest(session.Slot,
                0, 0x90, 0, 0, 0, 4798, 1558, false, 0, 0, true, 1, false, 0, 0, 0, 0, false, 0, 0)));
        var sink = new PlayerTeleportRequestFrameSink(source, bootstrap, new Pass(), new RuntimePlayerTeleportIngress(new Immediate(state)));
        // Player.ItemCheck treats item4263 and ShellphoneOcean5360 identically: packet73 subtype1.
        var bytes = new ReadOnlySequence<byte>(new byte[] {4,0,73,1});
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref bytes, out var frame));
        for (int repeat = 0; repeat < 4; repeat++)
        {
            while (queue.TryRead(out _)) { }
            Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
            Assert.True(queue.TryRead(out var teleport));
            var wire = teleport.Bytes.Span;
            Assert.Equal(15, wire.Length); Assert.Equal(65, wire[2]);
            Assert.Equal(fails ? 4 : 0, wire[3]); Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(wire[4..]));
            Assert.Equal(5, wire[14]);
            if (!fails)
            {
                Assert.Equal((repeat % 2 == 0 ? 800 : 200) * 16 - 2, BinaryPrimitives.ReadSingleLittleEndian(wire[6..]));
                Assert.Equal(1558, BinaryPrimitives.ReadSingleLittleEndian(wire[10..]));
            }
        }
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(2, 200)]
    [InlineData(19, 200)]
    [InlineData(53, 212)]
    [InlineData(48, 212)]
    [InlineData(58, 212)]
    public void Shore_outputs_match_direct_official_1458_invocations(ushort type, int expectedX)
    {
        var tiles = Coast();
        for (int x = 200; x <= 210; x++)
            tiles.SetInitialPopulationTile(x, 100, new WorldTile { Type = type, Flags = WorldTileFlags.Active });
        Assert.True(VanillaOceanLanding1458.TryFind(tiles, 150, 15000, 1, out float px, out float py));
        Assert.Equal(expectedX * 16 - 2, px); Assert.Equal(1558, py);
    }

    private sealed class Immediate(ServerRuntimeState state) : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { state.Apply(command); return true; }
    }
    private sealed class Pass : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }

    [Theory]
    [InlineData(1000, 800)]
    [InlineData(7999, 800)]
    [InlineData(8000, 200)]
    [InlineData(15000, 200)]
    public void Opposite_ocean_crawls_water_surface_to_dry_land(float originX, int expectedTileX)
    {
        WorldTileStore tiles = Coast();
        Assert.True(VanillaOceanLanding1458.TryFind(tiles, 150, originX, 1, out float x, out float y));
        Assert.Equal(expectedTileX * 16 - 2, x);
        Assert.Equal(100 * 16 - 42, y);
        Assert.False(VanillaWorldSolidCollision.Intersects(tiles, x, y, 20, 42));
    }

    [Fact]
    public void Repeated_use_alternates_beaches_without_movement_echo()
    {
        var authority = new PlayerAuthority(null, Coast(), oceanTeleportSurface: 150);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(884), session.Handle);
        authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 300, 100, 0, 0, 0, 0, 0)));
        for (int i = 0; i < 6; i++)
        {
            authority.TryApply(new PlayerTeleportRuntimeCommand(connection, RuntimePlayerTeleportRequestKind.MagicConch));
            Assert.True(authority.TryCapture(session.Handle, out var snapshot));
            Assert.Equal((i % 2 == 0 ? 800 : 200) * 16 - 2, snapshot.PositionX);
            Assert.Equal(1558, snapshot.PositionY);
        }
    }

    [Fact]
    public void Missing_world_surface_refuses_teleport_in_existing_authority()
    {
        var authority = new PlayerAuthority(null, Coast());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(885), session.Handle);
        authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 300, 100, 0, 0, 0, 0, 0)));
        Assert.True(authority.TryCapture(session.Handle, out var before));
        authority.TryApply(new PlayerTeleportRuntimeCommand(connection, RuntimePlayerTeleportRequestKind.MagicConch));
        Assert.True(authority.TryCapture(session.Handle, out var after));
        Assert.Equal(before.PositionX, after.PositionX); Assert.Equal(before.PositionY, after.PositionY);
    }

    [Fact]
    public void Dry_cave_below_seabed_is_not_a_landing_candidate()
    {
        var tiles = Coast();
        for (int x = 41; x < 195; x++)
        for (int y = 170; y < 180; y++)
            tiles.SetInitialPopulationTile(x, y, default);
        Assert.True(VanillaOceanLanding1458.TryFind(tiles, 150, 15000, 1, out float px, out float py));
        Assert.Equal(3198, px); Assert.Equal(1558, py);
    }

    [Theory]
    [InlineData(48)] // Spike
    [InlineData(58)] // Hellstone
    [InlineData(750)] // Source bleeding/hurt set
    public void Hazardous_shore_moves_inland(ushort type)
    {
        var tiles = Coast();
        tiles.SetInitialPopulationTile(200, 100, new WorldTile { Type = type, Flags = WorldTileFlags.Active });
        Assert.True(VanillaOceanLanding1458.TryFind(tiles, 150, 15000, 1, out float px, out _));
        Assert.True(px > 3198);
    }

    [Fact]
    public void Unusable_other_ocean_tries_original_side_as_vanilla_does()
    {
        var tiles = Coast();
        for (int x = 550; x < 999; x++)
        for (int y = 50; y < 259; y++)
            tiles.SetInitialPopulationTile(x, y, new WorldTile { LiquidAmount = 255 });
        Assert.True(VanillaOceanLanding1458.TryFind(tiles, 150, 1000, 1, out float px, out float py));
        Assert.Equal(3198, px); Assert.Equal(1558, py);
    }

    [Fact]
    public void Empty_or_unbounded_world_refuses_instead_of_inventing_floor()
    {
        var empty = new WorldTileStore(new WorldDimensions(1000, 300));
        Assert.False(VanillaOceanLanding1458.TryFind(empty, 150, 1000, 1, out _, out _));
        Assert.False(VanillaOceanLanding1458.TryFind(empty, double.NaN, 1000, 1, out _, out _));
    }

    internal static WorldTileStore Coast()
    {
        var tiles = new WorldTileStore(new WorldDimensions(1000, 300));
        for (int x = 0; x < 1000; x++)
        for (int y = 100; y < 259; y++)
        {
            bool ocean = x < 200 || x > 800;
            tiles.SetInitialPopulationTile(x, y, ocean && y < 160
                ? new WorldTile { LiquidAmount = 255 }
                : new WorldTile { Type = 0, Flags = WorldTileFlags.Active });
        }
        return tiles;
    }
}
