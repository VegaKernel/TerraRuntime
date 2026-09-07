using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeLarvaQueenBeeTests
{
    [Fact]
    public void Pick_breaks_complete_generated_larva_and_spawns_one_authoritative_queen_bee()
    {
        using var fixture = new Fixture(new WorldDimensions(500, 300));
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 40_001, spawnX: 20, spawnY: 20);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe);
        fixture.PlaceLarva(left: 30, top: 30);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, Kill(31, 31)));

        fixture.AssertLarvaCleared(30, 30);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedNpcSpawns);
        Assert.Equal(1, fixture.Npcs.ActiveCount);
        Span<NpcSnapshot> active = stackalloc NpcSnapshot[1];
        Assert.Equal(1, fixture.Npcs.CopyActive(active));
        Assert.Equal(VanillaNpcIds.QueenBee, active[0].TypeIdentity);
        Assert.Equal(connection.Player.Slot.Value, active[0].Target);
    }

    [Fact]
    public void Malformed_larva_is_fail_closed_and_cannot_spawn_a_boss()
    {
        using var fixture = new Fixture(new WorldDimensions(500, 300));
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 40_002, spawnX: 20, spawnY: 20);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe);
        fixture.PlaceLarva(left: 30, top: 30);
        WorldTile corrupt = fixture.Tiles.Get(32, 32);
        corrupt.FrameY = 18;
        fixture.Tiles.Set(32, 32, in corrupt);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, Kill(31, 31)));

        Assert.True(fixture.Tiles.Get(31, 31).IsActive);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedNpcSpawns);
        Assert.Equal(0, fixture.Npcs.ActiveCount);
    }

    [Fact]
    public void Larva_still_breaks_when_no_living_player_is_within_vanilla_spawn_distance()
    {
        using var fixture = new Fixture(new WorldDimensions(700, 300));
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 40_003, spawnX: 20, spawnY: 20);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe);
        fixture.PlaceLarva(left: 400, top: 30);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, Kill(401, 31)));

        fixture.AssertLarvaCleared(400, 30);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedNpcSpawns);
        Assert.Equal(0, fixture.Npcs.ActiveCount);
    }

    [Fact]
    public void Tile_definition_routes_larva_to_its_source_backed_object_path()
    {
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Larva);

        Assert.True(definition.IsFrameImportant);
        Assert.Equal(VanillaTileBreakPath.LarvaObject, definition.BreakPath);
    }

    private static TerrariaTileManipulationState Kill(int x, int y) =>
        new(
            (byte)TerrariaTileManipulationAction.KillTile,
            checked((short)x),
            checked((short)y),
            Data: 0,
            Style: 0);

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession? session;

        public Fixture(WorldDimensions dimensions)
        {
            Tiles = new WorldTileStore(dimensions);
            Npcs = new RuntimeNpcStore();
            State = new ServerRuntimeState(npcs: Npcs, worldTiles: Tiles);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeNpcStore Npcs { get; }
        public ServerRuntimeState State { get; }

        public ConnectionHandle SpawnPlayer(long connectionId, short spawnX, short spawnY)
        {
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
            session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(connectionId), session.Handle);
            State.Apply(new PlayerSpawnRuntimeCommand(
                connection,
                session,
                new PlayerSpawnCommitRequest(session.Slot, spawnX, spawnY, 0, 0, 0, 0, 0)));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            return connection;
        }

        public void SetSelectedInventoryItem(ConnectionHandle connection, ItemTypeId itemType)
        {
            State.Apply(new PlayerEquipmentRuntimeCommand(
                connection,
                new PlayerEquipmentCommitRequest(
                    connection.Player.Slot,
                    SlotId: 0,
                    Stack: 1,
                    Prefix: 0,
                    ItemNetId: checked((short)itemType.Value),
                    ItemFlags: 0)));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
        }

        public void PlaceLarva(int left, int top)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    var tile = new WorldTile
                    {
                        Type = checked((ushort)VanillaTileIds.Larva.Value),
                        FrameX = checked((short)(x * 18)),
                        FrameY = checked((short)(y * 18)),
                        Flags = WorldTileFlags.Active
                    };
                    Tiles.Set(left + x, top + y, in tile);
                }
            }
        }

        public void AssertLarvaCleared(int left, int top)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                    Assert.False(Tiles.Get(left + x, top + y).IsActive);
            }
        }

        public void Dispose() => session?.Dispose();
    }
}
