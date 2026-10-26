using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

// Configured owned-stock integration driver. This is not another original
// capture or a client packet that establishes inventory custody. Source clock,
// payment and world rules are exercised through the real Apply/Tick owners.
internal static class PlacementSourceScene1458
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState state);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);

    internal static ServerRuntimeState Create(WorldTileStore tiles,
        RuntimeTileManipulationReplicationRegistry? replication = null)
    {
        var main = new VanillaUnifiedRandom1458(19);
        var projectiles = new RuntimeProjectileStore();
        var state = new ServerRuntimeState(worldTiles: tiles, playerEvents: replication,
            projectiles: projectiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            playerUpdateRandomSeed: new(7),
            projectileReplication: new RuntimeProjectileReplicationRegistry(),
            tileManipulationReplication: replication,
            naturalSpawnRandom: new SystemVanillaNpcRandom(main),
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(main),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
            sourcePlayerWorld: MiningSourceScene1458.Facts(tiles));
        var composition = Composition(state);
        composition.WorldTileAuthority.BindToolBuildingPermission(RuntimeToolBuildingPermission1458.FromLoadedWorld(tiles));
        composition.Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
        composition.Players.SetNpcHealthWorldFacts(() => new(false, false));
        composition.Players.SetNpcHealthGrapplingFacts(_ => false);
        return state;
    }

    internal static void Start(ServerRuntimeState state, WorldTileStore tiles,
        ConnectionHandle connection, int targetX, int targetY)
    {
        var players = Composition(state).Players;
        Assert.True(players.TryGet(connection, out var member));
        var inventory = Inventory(players);
        Assert.True(inventory.TryGet(connection, member.SelectedItem, out var selected));
        Assert.True(selected.IsCanonical && !selected.IsEmpty);
        var quantities = inventory.CaptureFundedQuantities(connection);
        quantities[member.SelectedItem] = selected.Stack;
        inventory.AdoptFundedQuantities(connection, quantities);
        state.Apply(new PlayerHealthRuntimeCommand(connection, new(connection.Player.Slot, 100, 100)));
        state.Apply(new PlayerManaRuntimeCommand(connection, new(connection.Player.Slot, 0, 20)));
        state.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
        for (int x = 0; x < tiles.Dimensions.WidthTiles; x++)
            tiles.Set(x, targetY + 3, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        tiles.Set(targetX + 1, targetY, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Move(false);
        state.Tick();
        Move(true);
        state.Tick();
        Assert.True(players.TryGet(connection, out member));
        Assert.NotNull(member.ItemPhase);
        Assert.True(member.ItemPhase!.Value.ToolUse is { LocalClocksKnown: true, SourceStagePose: not null });
        void Move(bool use) => state.Apply(new PlayerMovementRuntimeCommand(connection,
            new(connection.Player.Slot, (byte)(64 | (use ? 32 : 0)), 16, 0, 64,
                member.SelectedItem, (targetX - 4) * 16f, targetY * 16f + 6f,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
    }

    internal static PlayerEquipmentCommitRequest AfterOne(ServerRuntimeState state, ConnectionHandle connection)
    {
        var players = Composition(state).Players;
        Assert.True(players.TryGet(connection, out var member));
        Assert.True(Inventory(players).TryGet(connection, member.SelectedItem, out var selected));
        Assert.True(selected.Stack > 0);
        return new(connection.Player.Slot, member.SelectedItem, (short)(selected.Stack - 1),
            checked((byte)selected.Prefix.Value), selected.Stack == 1 ? (short)0 : checked((short)selected.ItemType.Value), 0);
    }

    internal static void AssertFundedStock(ServerRuntimeState state, ConnectionHandle connection, short expected)
    {
        var players = Composition(state).Players;
        Assert.True(players.TryGet(connection, out var member));
        var inventory = Inventory(players);
        Assert.True(inventory.TryGet(connection, member.SelectedItem, out var selected));
        Assert.Equal(expected, selected.Stack);
        Assert.Equal(expected, inventory.CaptureFundedQuantities(connection)[member.SelectedItem]);
    }
}
