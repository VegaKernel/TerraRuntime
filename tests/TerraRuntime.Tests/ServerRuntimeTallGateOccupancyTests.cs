using TerraRuntime.Gameplay.Npcs;
﻿using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class ServerRuntimeTallGateOccupancyTests
{
    [Fact]
    public void Tile_is_free_when_no_actors()
    {
        var tiles = new WorldTileStore(new WorldDimensions(40, 40));
        var state = new ServerRuntimeState(worldTiles: tiles);
        Assert.True(state.IsTileActorFreeForTesting(15, 12));
    }
    [Fact]
    public async Task Tile_blocked_by_npc_hitbox()
    {
        var tiles = new WorldTileStore(new WorldDimensions(40, 40));
        var state = new ServerRuntimeState(worldTiles: tiles);
        var completion = new TaskCompletionSource<NpcSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var spawn = new NpcStateUpdate(
            Type: VanillaNpcIds.Zombie.Value,
            NetId: (short)VanillaNpcIds.Zombie.Value,
            PositionX: 15f * 16f,
            PositionY: 12f * 16f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with { Scale = 1f });
        state.Apply(new NpcSpawnRuntimeCommand(0, spawn, completion));
        await completion.Task;
        Assert.False(state.IsTileActorFreeForTesting(15, 12));
        Assert.True(state.IsTileActorFreeForTesting(30, 30));
    }
    [Fact]
    public async Task Tile_blocked_by_server_player()
    {
        var tiles = new WorldTileStore(new WorldDimensions(40, 40));
        var pool = new PlayerSlotPool(10);
        var identities = new RuntimeServerPlayerSlotRegistry(pool);
        var store = new RuntimeServerPlayerStateStore(identities, 10);
        var state = new ServerRuntimeState(worldTiles: tiles, serverPlayerStates: store, serverPlayerIdentities: identities);
        PlayerHandle handle = CreateServerPlayer(identities, store, 15f * 16f, 12f * 16f);
        Assert.False(state.IsTileActorFreeForTesting(15, 12));
        Assert.True(state.IsTileActorFreeForTesting(30, 30));
        store.TrySetMotion(handle, 30f * 16f, 30f * 16f, 0f, 0f, out _);
        Assert.True(state.IsTileActorFreeForTesting(15, 12));
        Assert.False(state.IsTileActorFreeForTesting(30, 30));
    }
    [Fact]
    public void Tall_gate_fails_closed_when_occupied_and_succeeds_when_free()
    {
        var tiles = new WorldTileStore(new WorldDimensions(40, 40));
        PlaceClosedTallGate(tiles, 15, 10);
        var occupiedProbe = new RuntimeTallGateOccupancyProbe((x, y) => !(x == 15 && y == 12));
        var occupiedService = new VanillaWorldGroundFighterDoorOpeningService(tiles, occupiedProbe);
        var occupiedIntent = new VanillaGroundFighterDoorOpeningIntent(15, 12, 1, VanillaTileIds.TallGateClosed);
        Assert.False(occupiedService.TryOpen(in occupiedIntent, out _));
        for (int row = 0; row < 5; row++)
            Assert.Equal(VanillaTileIds.TallGateClosed, tiles.Get(15, 10 + row).TileType);
        var freeProbe = new RuntimeTallGateOccupancyProbe((x, y) => true);
        var freeService = new VanillaWorldGroundFighterDoorOpeningService(tiles, freeProbe);
        Assert.True(freeService.TryOpen(in occupiedIntent, out VanillaGroundFighterDoorOpeningMutation mutation));
        Assert.Equal(VanillaGroundFighterDoorOpeningKind.TallGate, mutation.Kind);
        for (int row = 0; row < 5; row++)
            Assert.Equal(VanillaTileIds.TallGateOpen, tiles.Get(15, 10 + row).TileType);
    }
    [Fact]
    public async Task Server_runtime_wires_tall_gate_occupancy_for_zombie()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 0; x < 100; x++)
        {
            WorldTile ground = new() { Type = 1, Flags = WorldTileFlags.Active };
            tiles.Set(x, 80, in ground);
        }
        PlaceClosedTallGate(tiles, 20, 75);
        var state = new ServerRuntimeState(worldTiles: tiles);
        Assert.True(state.IsTileActorFreeForTesting(20, 77));
        var completion = new TaskCompletionSource<NpcSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var zombie = new NpcStateUpdate(
            Type: VanillaNpcIds.Zombie.Value,
            NetId: (short)VanillaNpcIds.Zombie.Value,
            PositionX: 20f * 16f,
            PositionY: 77f * 16f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with { Scale = 1f });
        state.Apply(new NpcSpawnRuntimeCommand(0, zombie, completion));
        await completion.Task;
        Assert.False(state.IsTileActorFreeForTesting(20, 77));
    }
    private static PlayerHandle CreateServerPlayer(RuntimeServerPlayerSlotRegistry identities, RuntimeServerPlayerStateStore store, float x, float y)
    {
        var id = new ServerPlayerId(Guid.NewGuid().ToString("N"));
        ServerPlayerSlotAcquireResult result = identities.TryAcquire(id, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease);
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, result);
        Assert.NotNull(lease);
        PlayerHandle handle = lease!.Player;
        bool spawned = store.TrySpawn(id, x, y, out _);
        Assert.True(spawned);
        return handle;
    }
    private static void PlaceClosedTallGate(WorldTileStore tiles, int x, int topY)
    {
        for (int row = 0; row < 5; row++)
        {
            WorldTile tile = new() { Type = (ushort)VanillaTileIds.TallGateClosed.Value, Flags = WorldTileFlags.Active, FrameY = (short)(row * 18) };
            tiles.Set(x, topY + row, in tile);
        }
    }
}
