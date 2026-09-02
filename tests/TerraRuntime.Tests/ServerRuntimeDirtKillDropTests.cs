using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
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
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 10, 10));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 10, 10);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        Assert.Equal(default, fixture.Tiles.Get(10, 10));
        Assert.Equal(beforeVersion + 2, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(1, fixture.Tiles.DirtySections.DirtyCount);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(1, fixture.Items.ActiveCount);
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot drop));
        Assert.Equal(162f, drop.PositionX);
        Assert.Equal(162f, drop.PositionY);
        Assert.InRange(drop.VelocityX, -3f, 3f);
        Assert.InRange(drop.VelocityY, -4f, -1.6f);
        Assert.Equal(1, drop.Stack);
        Assert.Equal(0, drop.Prefix);
        Assert.Equal(WorldItemOwnershipMode.None, drop.Ownership);
        Assert.Equal(checked((short)VanillaItemIds.DirtBlock.Value), drop.ItemNetId);
        Assert.False(drop.Shimmered);
        Assert.Equal(0f, drop.ShimmerTime);
        Assert.Equal(0, drop.EnemyGrabDelayTime);
    }

    [Fact]
    public void Full_world_item_pool_rejects_kill_without_mutating_dirt()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9302);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 10, 10));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 10, 10);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        WorldTile before = fixture.Tiles.Get(10, 10);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);
        FillWorldItemPool(fixture.Items);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(beforeVersion, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
        Assert.Equal(RuntimeWorldItemStore.VanillaCapacity, fixture.Items.ActiveCount);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(1, fixture.State.RejectedWorldItemAllocations);
    }

    [Fact]
    public void Kill_tile_failed_hit_is_preserved_without_mutation_or_drop()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9303);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 10, 10));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 10, 10);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        WorldTile before = fixture.Tiles.Get(10, 10);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 1,
                Style: 0)));

        Assert.Equal(before, fixture.Tiles.Get(10, 10));
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
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 10, 10));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 10, 10);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        WorldTile before = fixture.Tiles.Get(10, 10);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(beforeVersion, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
    }

    private static void FillWorldItemPool(RuntimeWorldItemStore items)
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

        for (int slot = 0; slot < RuntimeWorldItemStore.VanillaCapacity; slot++)
            Assert.True(items.TryAllocateDrop(in drop, out _));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession? session;

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            Items = new RuntimeWorldItemStore();
            State = new ServerRuntimeState(worldTiles: Tiles, worldItems: Items);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeWorldItemStore Items { get; }
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
            var request = new PlayerSpawnCommitRequest(session.Slot, 20, 20, 0, 0, 0, 0, 0);
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
        }

        public void Dispose() => session?.Dispose();
    }
}
