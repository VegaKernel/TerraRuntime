using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeSimpleTileKillAuthorityTests
{
    [Theory]
    [InlineData(1, false)] // original defaults: age0, maxStack9999 -> source sentinel400
    [InlineData(2, true)]  // original DirtBlock offset200, full stacks -> first oldest slot0
    public void Full_source_item_table_accepts_mining_without_inventing_guaranteed_drop_delivery(short itemType, bool replaces)
    {
        using var fixture = new Fixture();
        var connection = fixture.SpawnPlayer(145820);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.VortexPickaxe, 1);
        fixture.SetActiveTile(60, 60, 1);
        for (int i = 0; i < 400; i++)
            Assert.True(fixture.Items.TryAllocateDrop(new(1000, 1000, 0, 0, 9999, 0,
                WorldItemOwnershipMode.None, itemType, false, 0, 0), out _));
        Assert.True(fixture.Items.TryGetActive(0, out var before));
        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(connection,
            new((byte)TerrariaTileManipulationAction.KillTile, 60, 60, 0, 0)));
        Assert.False(fixture.Tiles.Get(60, 60).IsActive);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedWorldItemAllocations);
        Assert.Equal(400, fixture.Items.ActiveCount);
        Assert.True(fixture.Items.TryGetActive(0, out var after));
        if (replaces)
        {
            Assert.Equal(before.Handle.Generation.Value + 1, after.Handle.Generation.Value);
            Assert.Equal(3, after.ItemNetId); Assert.Equal(1, after.Stack);
        }
        else Assert.Equal(before, after);
        for (short slot = 1; slot < 400; slot++)
        {
            Assert.True(fixture.Items.TryGetActive(slot, out var retained));
            Assert.Equal(itemType, retained.ItemNetId); Assert.Equal(9999, retained.Stack);
        }
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(53, 169)]
    [InlineData(147, 593)]
    public void Copper_pickaxe_commits_supported_simple_tile_and_drop(int rawTileType, int expectedDropItem)
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9400 + rawTileType);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SetActiveTile(60, 60, rawTileType);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        Assert.False(fixture.Tiles.Get(60, 60).IsActive);
        Assert.Equal(rawTileType == 1 ? 3 : 2, fixture.State.AppliedClientTileManipulations);
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
        for (int y = 59; y <= 61; y++)
        for (int x = 59; x <= 61; x++)
            fixture.SetActiveTile(x, y, rawTileType);

        Assert.True(VanillaTileIds.TryCreate(rawTileType, out TileTypeId tileType));
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(tileType);
        Assert.Equal(VanillaTileDropRuleKind.Fixed, definition.DropRule.Kind);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        Assert.False(fixture.Tiles.Get(60, 60).IsActive);
        for (int y = 59; y <= 61; y++)
        for (int x = 59; x <= 61; x++)
        {
            if (x == 60 && y == 60)
                continue;
            Assert.True(fixture.Tiles.Get(x, y).IsActive, $"Neighbour ({x},{y}) was modified while mining center {rawTileType}.");
            Assert.Equal(tileType, fixture.Tiles.Get(x, y).TileType);
        }

        Assert.Equal(rawTileType == 1 ? 3 : 2, fixture.State.AppliedClientTileManipulations);
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
        fixture.SetActiveTile(60, 60, rawTileType);

        Assert.True(VanillaTileIds.TryCreate(rawTileType, out TileTypeId tileType));
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(tileType);
        Assert.Equal(VanillaTileBreakPath.FrameImportantSingleCell, definition.BreakPath);
        Assert.Equal(VanillaTileDropRuleKind.Fixed, definition.DropRule.Kind);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        Assert.False(fixture.Tiles.Get(60, 60).IsActive);
        Assert.Equal(3, fixture.State.AppliedClientTileManipulations);
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
        fixture.SeedEmptyBaseChest(60, 60);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 61,
                TileY: 61,
                Data: 0,
                Style: 0)));

        for (int y = 60; y <= 61; y++)
        for (int x = 60; x <= 61; x++)
            Assert.False(fixture.Tiles.Get(x, y).IsActive);
        Assert.Empty(fixture.Chests.CaptureSnapshot());
        Assert.Equal(3, fixture.State.AppliedClientTileManipulations);
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
        fixture.SeedEmptyBaseChest(60, 60);
        Assert.True(fixture.Chests.TryOpen(connection, 60, 60, out WorldChest opened));
        var item = new TerrariaChestItemState(opened.SlotId, 0, 1, 0, checked((short)VanillaItemIds.StoneBlock.Value));
        Assert.True(fixture.Chests.TrySetItem(connection, in item, out _));
        Assert.True(fixture.Chests.TryClose(connection, out _));

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        for (int y = 60; y <= 61; y++)
        for (int x = 60; x <= 61; x++)
            Assert.True(fixture.Tiles.Get(x, y).IsActive);
        WorldChest preserved = Assert.Single(fixture.Chests.CaptureSnapshot());
        Assert.Equal(1, preserved.Items[0].Stack);
        Assert.Equal(VanillaItemIds.StoneBlock.Value, preserved.Items[0].ItemType);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        MiningSourceScene1458.AssertRetainedWithoutAuthority(fixture.State, fixture.Tiles, connection, 60, 60);
    }

    [Theory]
    [InlineData(2)]   // Grass must use vanilla failed-pick transform semantics, not direct generic removal.
    [InlineData(226)] // Lihzahrd Brick has a source-backed 210 pick-power gate.
    public void Copper_pickaxe_cannot_bypass_special_mining_semantics(int rawTileType)
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9500 + rawTileType);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SetActiveTile(60, 60, rawTileType);
        WorldTile before = fixture.Tiles.Get(60, 60);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        Assert.Equal(before, fixture.Tiles.Get(60, 60));
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        MiningSourceScene1458.AssertRetainedWithoutAuthority(fixture.State, fixture.Tiles, connection, 60, 60);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(0, fixture.Items.ActiveCount);
    }


    [Fact]
    public void Copper_hammer_commits_housing_wall_break_and_exact_wall_drop()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9901);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperHammer, stack: 1);
        fixture.SetWall(60, 60, VanillaWallIds.Stone);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillWall,
                TileX: 60,
                TileY: 60,
                Data: 0,
                Style: 0)));

        Assert.Equal(VanillaWallIds.None, fixture.Tiles.Get(60, 60).WallType);
        Assert.Equal(2, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot drop));
        Assert.Equal(26, drop.ItemNetId);
    }

    [Fact]
    public void Incomplete_hammer_hit_is_relay_only_and_does_not_remove_wall_or_drop_item()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9902);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperHammer, stack: 1);
        fixture.SetWall(60, 60, VanillaWallIds.Stone);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillWall,
                TileX: 60,
                TileY: 60,
                Data: 1,
                Style: 0)));

        Assert.Equal(VanillaWallIds.Stone, fixture.Tiles.Get(60, 60).WallType);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.Items.ActiveCount);
    }

    [Fact]
    public void Natural_wall_inside_same_wall_mass_is_rejected_until_an_edge_is_exposed()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9903);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperHammer, stack: 1);
        for (int y = 59; y <= 61; y++)
        for (int x = 59; x <= 61; x++)
            fixture.SetWall(x, y, VanillaWallIds.DirtUnsafe);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState((byte)TerrariaTileManipulationAction.KillWall, 60, 60, 0, 0)));
        Assert.Equal(VanillaWallIds.DirtUnsafe, fixture.Tiles.Get(60, 60).WallType);
        MiningSourceScene1458.AssertRetainedWithoutAuthority(fixture.State, fixture.Tiles, connection, 60, 60);

        fixture.SetWall(59, 59, VanillaWallIds.None);
        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState((byte)TerrariaTileManipulationAction.KillWall, 60, 60, 0, 0)));
        Assert.Equal(VanillaWallIds.None, fixture.Tiles.Get(60, 60).WallType);
        Assert.Equal(2, fixture.State.AppliedClientTileManipulations);
    }

    [Fact]
    public void Dungeon_wall_completion_is_gated_by_skeletron_progression()
    {
        using var locked = new Fixture();
        ConnectionHandle lockedConnection = locked.SpawnPlayer(connectionId: 9904);
        locked.SetSelectedInventoryItem(lockedConnection, VanillaItemIds.CopperHammer, stack: 1);
        locked.SetWall(60, 60, VanillaWallIds.BlueDungeonUnsafe);
        locked.SetWall(59, 59, VanillaWallIds.None);
        locked.State.Apply(new ClientTileManipulationRuntimeCommand(
            lockedConnection,
            new TerrariaTileManipulationState((byte)TerrariaTileManipulationAction.KillWall, 60, 60, 0, 0)));
        Assert.Equal(VanillaWallIds.BlueDungeonUnsafe, locked.Tiles.Get(60, 60).WallType);

        using var unlocked = new Fixture(skeletronDownedBaseline: true);
        ConnectionHandle unlockedConnection = unlocked.SpawnPlayer(connectionId: 9905);
        unlocked.SetSelectedInventoryItem(unlockedConnection, VanillaItemIds.CopperHammer, stack: 1);
        unlocked.SetWall(60, 60, VanillaWallIds.BlueDungeonUnsafe);
        unlocked.SetWall(59, 59, VanillaWallIds.None);
        MiningSourceScene1458.Apply(unlocked.State, unlocked.Tiles, new ClientTileManipulationRuntimeCommand(
            unlockedConnection,
            new TerrariaTileManipulationState((byte)TerrariaTileManipulationAction.KillWall, 60, 60, 0, 0)));
        Assert.Equal(VanillaWallIds.None, unlocked.Tiles.Get(60, 60).WallType);
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

        public Fixture(bool skeletronDownedBaseline = false, bool golemDownedBaseline = false)
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            Items = new RuntimeWorldItemStore();
            Chests = new RuntimeChestStore([]);
            RuntimeWorldObjectMetadataRegistry.Bind(Tiles, Chests);
            State = new ServerRuntimeState(
                worldTiles: Tiles,
                worldItems: Items,
                skeletronDownedBaseline: skeletronDownedBaseline,
                golemDownedBaseline: golemDownedBaseline,
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

        public void SetActiveTile(int x, int y, int rawTileType)
        {
            // Actual 1458 CheckOnTable/Check1x1 attachment, not a bare cell
            // that source framing would remove on the first failed pick.
            if (rawTileType is 49 or 136)
                Tiles.Set(x, y + 1, new WorldTile { Type = rawTileType == 49 ? (ushort)19 : (ushort)1,
                    Flags = WorldTileFlags.Active });
            if (rawTileType == 53)
                Tiles.Set(x, y + 1, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Assert.True(VanillaTileIds.TryCreate(rawTileType, out TileTypeId tileType));
            var tile = new WorldTile
            {
                Type = checked((ushort)tileType.Value),
                Flags = WorldTileFlags.Active
            };
            Tiles.Set(x, y, in tile);
        }


        public void SetWall(int x, int y, WallTypeId wallType)
        {
            WorldTile tile = Tiles.Get(x, y);
            Assert.True(tile.TrySetWallType(wallType));
            Tiles.Set(x, y, in tile);
        }

        public void SeedEmptyBaseChest(int left, int top)
        {
            SetActiveTile(left, top + 2, 1);
            SetActiveTile(left + 1, top + 2, 1);
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
