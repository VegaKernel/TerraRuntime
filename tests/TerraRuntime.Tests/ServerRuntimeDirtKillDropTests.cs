using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeDirtKillDropTests
{
    [Fact]
    public void Kill_tile_with_copper_pickaxe_commits_tile_and_one_dirt_drop()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9301);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 60, 60));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 60, 60);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        Assert.Equal(new WorldTile { FrameX = -1, FrameY = -1 }, fixture.Tiles.Get(60, 60));
        Assert.Equal(beforeVersion + 2, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(1, fixture.Tiles.DirtySections.DirtyCount);
        Assert.Equal(2, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(1, fixture.Items.ActiveCount);
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot drop));
        Assert.Equal(960f, drop.PositionX);
        Assert.Equal(960f, drop.PositionY);
        Assert.InRange(drop.VelocityX, -3f, 3f);
        Assert.InRange(drop.VelocityY, -4f, -1.6f);
        Assert.Equal(1, drop.Stack);
        Assert.Equal(0, drop.Prefix);
        // MessageBuffer17 scopes NewItem to the authenticated breaker (1458).
        Assert.Equal(WorldItemOwnershipMode.None, drop.Ownership);
        Assert.Equal(WorldItemOwnershipMode.ReserveForLocalPlayer, Assert.Single(fixture.CreationDrops).Ownership);
        Assert.Equal(connection.Player.Slot.Value, drop.OwnerPlayerId);
        Assert.Equal(checked((short)VanillaItemIds.DirtBlock.Value), drop.ItemNetId);
        Assert.False(drop.Shimmered);
        Assert.Equal(0f, drop.ShimmerTime);
        Assert.Equal(100, drop.EnemyGrabDelayTime);
    }

    [Fact]
    public void Dirt_embedded_in_ordinary_terrain_can_be_mined_and_drops_dirt()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9305);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SetActiveDirt(60, 60);
        fixture.SetActiveDirt(59, 60);
        fixture.SetActiveDirt(61, 60);
        fixture.SetActiveDirt(60, 61);
        fixture.DrainTileDirty();

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        Assert.False(fixture.Tiles.Get(60, 60).IsActive);
        Assert.True(fixture.Tiles.Get(59, 60).IsActive);
        Assert.True(fixture.Tiles.Get(61, 60).IsActive);
        Assert.True(fixture.Tiles.Get(60, 61).IsActive);
        Assert.Equal(2, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot drop));
        Assert.Equal(checked((short)VanillaItemIds.DirtBlock.Value), drop.ItemNetId);
    }

    [Fact]
    public void Unknown_reserved_item_slot_rejects_kill_without_mutating_dirt()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9302);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.VortexPickaxe, stack: 1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 60, 60));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 60, 60);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        WorldTile before = fixture.Tiles.Get(60, 60);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);
        WorldItemDropReservation held = FillWorldItemPool(fixture.Items);
        Assert.True(MiningSourceScene1458.World(fixture.State).TryPrepareWorldProjectileRandom(out var random));

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        Assert.Equal(before, fixture.Tiles.Get(60, 60));
        Assert.Equal(beforeVersion, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
        Assert.Equal(RuntimeWorldItemStore.VanillaCapacity - 1, fixture.Items.ActiveCount);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.True(random!.IsCurrent);
        MiningSourceScene1458.AssertRetainedWithoutAuthority(fixture.State, fixture.Tiles, connection, 60, 60);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        // A speculative candidate is not an adopted allocation failure metric.
        Assert.Equal(0, fixture.State.RejectedWorldItemAllocations);
        Assert.True(fixture.Items.TryReleaseDropReservation(in held));
    }

    [Fact]
    public void Kill_tile_failed_hit_is_preserved_without_mutation_or_drop()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9303);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 60, 60));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 60, 60);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        WorldTile before = fixture.Tiles.Get(60, 60);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 1,
                Style: 0)));

        Assert.Equal(before, fixture.Tiles.Get(60, 60));
        Assert.Equal(beforeVersion, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
    }

    [Fact]
    public void Kill_tile_without_copper_pickaxe_is_rejected_before_drop_reservation()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9304);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.DirtBlock, stack: 1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 60, 60));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 60, 60);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        WorldTile before = fixture.Tiles.Get(60, 60);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        Assert.Equal(before, fixture.Tiles.Get(60, 60));
        Assert.Equal(beforeVersion, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
    }

    private static WorldItemDropReservation FillWorldItemPool(RuntimeWorldItemStore items)
    {
        var drop = new WorldItemDropStateUpdate(
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: 1,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: checked((short)VanillaItemIds.DirtBlock.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);

        for (int slot = 0; slot < RuntimeWorldItemStore.VanillaCapacity - 1; slot++)
            Assert.True(items.TryAllocateDrop(in drop, out _));
        Assert.True(items.TryReserveDropSlot(out var held));
        return held;
    }

    private sealed class DropRecording(List<WorldItemSnapshot> drops) : TerraRuntime.Core.Worlds.IWorldItemStateCommitSink
    {
        public void WorldItemStateCommitted(TerraRuntime.Core.Worlds.WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot,
            int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        {
            if (kind == TerraRuntime.Core.Worlds.WorldItemStateCommitKind.Drop) drops.Add(snapshot);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession? session;

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            Items = new RuntimeWorldItemStore(new DropRecording(CreationDrops));
            State = new ServerRuntimeState(worldTiles: Tiles, worldItems: Items,
                sourcePlayerWorld: MiningSourceScene1458.Facts(Tiles),
                worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
                playerUpdateRandomSeed: new(0),
                townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 });
            ToolPhaseWorldFixture1458.BindFreshInactiveEvent(State, Tiles);
            for (int x = 55; x <= 65; x++) Tiles.Set(x, 63, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }

        public WorldTileStore Tiles { get; }
        public RuntimeWorldItemStore Items { get; }
        public List<WorldItemSnapshot> CreationDrops { get; } = [];
        public ServerRuntimeState State { get; }

        public ConnectionHandle SpawnPlayer(long connectionId)
        {
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
            session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());

            var connection = new ConnectionHandle(
                GameCommandSourceId.FromConnection(connectionId),
                session.Handle);
            var request = new PlayerSpawnCommitRequest(session.Slot, 57, 63, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            return connection;
        }

        public void SetSelectedInventoryItem(ConnectionHandle connection, ItemTypeId itemType, short stack)
        {
            var request = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                SlotId: 0,
                Stack: stack,
                Prefix: 0,
                ItemNetId: checked((short)itemType.Value),
                ItemFlags: 0);
            State.Apply(new PlayerEquipmentRuntimeCommand(connection, request));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
            MiningSourceScene1458.Start(State, connection);
        }

        public void SetActiveDirt(int x, int y)
        {
            var tile = new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.Dirt.Value),
                Flags = WorldTileFlags.Active
            };
            Tiles.Set(x, y, in tile);
        }

        public void DrainTileDirty()
        {
            Span<WorldSectionId> drained = stackalloc WorldSectionId[16];
            _ = Tiles.DirtySections.Drain(drained);
        }

        public void Dispose() => session?.Dispose();
    }
}
