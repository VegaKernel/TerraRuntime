using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeSimpleTileKillAuthorityTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(53, 169)]
    [InlineData(147, 593)]
    public void Copper_pickaxe_commits_supported_simple_tile_and_drop(int rawTileType, int expectedDropItem)
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9400 + rawTileType);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SetActiveTile(10, 10, rawTileType);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        Assert.False(fixture.Tiles.Get(10, 10).IsActive);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(1, fixture.Items.ActiveCount);
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot drop));
        Assert.Equal(expectedDropItem, drop.ItemNetId);
    }

    [Theory]
    [InlineData(0)] // Dirt
    [InlineData(1)] // Stone
    public void Common_simple_cells_break_inside_solid_mass_without_touching_neighbours(int rawTileType)
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9600 + rawTileType);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        for (int y = 9; y <= 11; y++)
        for (int x = 9; x <= 11; x++)
            fixture.SetActiveTile(x, y, rawTileType);

        Assert.True(VanillaTileIds.TryCreate(rawTileType, out TileTypeId tileType));
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(tileType);
        Assert.Equal(VanillaTileDropRuleKind.Fixed, definition.DropRule.Kind);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        Assert.False(fixture.Tiles.Get(10, 10).IsActive);
        for (int y = 9; y <= 11; y++)
        for (int x = 9; x <= 11; x++)
        {
            if (x == 10 && y == 10)
                continue;
            Assert.True(fixture.Tiles.Get(x, y).IsActive, $"Neighbour ({x},{y}) was modified while mining center {rawTileType}.");
            Assert.Equal(tileType, fixture.Tiles.Get(x, y).TileType);
        }

        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.Items.ActiveCount);
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot drop));
        Assert.Equal(definition.DropRule.PrimaryItem.Value, drop.ItemNetId);
    }


    [Theory]
    [InlineData(49, 148)]   // Water Candle -> Water Candle
    [InlineData(136, 538)]  // Switch -> Switch
    [InlineData(427, 3622)] // Red Team Platform -> Red Team Platform
    [InlineData(439, 3642)] // White Team Platform -> White Team Platform
    public void Source_pinned_frame_important_single_cells_break_and_drop_without_object_footprint_guessing(
        int rawTileType,
        int expectedDropItem)
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9800 + rawTileType);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SetActiveTile(10, 10, rawTileType);

        Assert.True(VanillaTileIds.TryCreate(rawTileType, out TileTypeId tileType));
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(tileType);
        Assert.Equal(VanillaTileBreakPath.FrameImportantSingleCell, definition.BreakPath);
        Assert.Equal(VanillaTileDropRuleKind.Fixed, definition.DropRule.Kind);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        Assert.False(fixture.Tiles.Get(10, 10).IsActive);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(1, fixture.Items.ActiveCount);
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot drop));
        Assert.Equal(expectedDropItem, drop.ItemNetId);
    }

    [Fact]
    public void Empty_base_chest_breaks_as_one_object_and_emits_authoritative_chest_drop()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9700);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SeedEmptyBaseChest(10, 10);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 11,
                TileY: 11,
                Data: 0,
                Style: 0)));

        for (int y = 10; y <= 11; y++)
        for (int x = 10; x <= 11; x++)
            Assert.False(fixture.Tiles.Get(x, y).IsActive);
        Assert.Empty(fixture.Chests.CaptureSnapshot());
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot drop));
        Assert.Equal(VanillaItemIds.Chest.Value, drop.ItemNetId);
    }

    [Fact]
    public void Nonempty_base_chest_break_is_rejected_without_losing_geometry_or_items()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9701);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SeedEmptyBaseChest(10, 10);
        Assert.True(fixture.Chests.TryOpen(connection, 10, 10, out WorldChest opened));
        var item = new TerrariaChestItemState(opened.SlotId, 0, 1, 0, checked((short)VanillaItemIds.StoneBlock.Value));
        Assert.True(fixture.Chests.TrySetItem(connection, in item, out _));
        Assert.True(fixture.Chests.TryClose(connection, out _));

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        for (int y = 10; y <= 11; y++)
        for (int x = 10; x <= 11; x++)
            Assert.True(fixture.Tiles.Get(x, y).IsActive);
        WorldChest preserved = Assert.Single(fixture.Chests.CaptureSnapshot());
        Assert.Equal(1, preserved.Items[0].Stack);
        Assert.Equal(VanillaItemIds.StoneBlock.Value, preserved.Items[0].ItemType);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
    }

    [Theory]
    [InlineData(2)]   // Grass must use vanilla failed-pick transform semantics, not direct generic removal.
    [InlineData(226)] // Lihzahrd Brick has a source-backed 210 pick-power gate.
    public void Copper_pickaxe_cannot_bypass_special_mining_semantics(int rawTileType)
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9500 + rawTileType);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SetActiveTile(10, 10, rawTileType);
        WorldTile before = fixture.Tiles.Get(10, 10);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(0, fixture.Items.ActiveCount);
    }

    [Fact]
    public void Failed_pick_transform_is_an_explicit_typed_mutation()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var grass = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Grass.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(20, 20, in grass);
        var service = new VanillaWorldTileMutationService(tiles);
        var request = new WorldTileMutationRequest(
            WorldTileMutationKind.TransformTile,
            20,
            20,
            TileType: VanillaTileIds.Dirt);

        WorldTileMutationResult result = service.Apply(in request);

        Assert.True(result.Applied);
        Assert.True(tiles.Get(20, 20).IsActive);
        Assert.Equal(VanillaTileIds.Dirt, tiles.Get(20, 20).TileType);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession? session;

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            Items = new RuntimeWorldItemStore();
            Chests = new RuntimeChestStore([]);
            RuntimeWorldObjectMetadataRegistry.Bind(Tiles, Chests);
            State = new ServerRuntimeState(worldTiles: Tiles, worldItems: Items);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeWorldItemStore Items { get; }
        public RuntimeChestStore Chests { get; }
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

        public void SetActiveTile(int x, int y, int rawTileType)
        {
            Assert.True(VanillaTileIds.TryCreate(rawTileType, out TileTypeId tileType));
            var tile = new WorldTile
            {
                Type = checked((ushort)tileType.Value),
                Flags = WorldTileFlags.Active
            };
            Tiles.Set(x, y, in tile);
        }

        public void SeedEmptyBaseChest(int left, int top)
        {
            SetObjectCell(left, top, frameX: 0, frameY: 0);
            SetObjectCell(left + 1, top, frameX: 18, frameY: 0);
            SetObjectCell(left, top + 1, frameX: 0, frameY: 18);
            SetObjectCell(left + 1, top + 1, frameX: 18, frameY: 18);
            Assert.True(Chests.TryCreate(
                left,
                top,
                VanillaChestStorageFacts1458.DefaultItemSlots,
                out _));
        }

        private void SetObjectCell(int x, int y, short frameX, short frameY)
        {
            WorldTile tile = Tiles.Get(x, y);
            Assert.True(tile.TrySetTileType(VanillaTileIds.Containers));
            tile.FrameX = frameX;
            tile.FrameY = frameY;
            tile.Flags |= WorldTileFlags.Active;
            Tiles.Set(x, y, in tile);
        }

        public void Dispose() => session?.Dispose();
    }
}
