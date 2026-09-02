using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class VanillaServerPlayerLiquidPhysicsTests
{
    [Theory]
    [InlineData(false, false, false, 0.4f, 10.01f, 5.01f, 15)]
    [InlineData(true, false, false, 0.2f, 5.01f, 6.01f, 30)]
    [InlineData(true, true, false, 0.1f, 3.01f, 5.01f, 15)]
    [InlineData(true, false, true, 0.15f, 10.01f, 5.51f, 23)]
    public void Previous_contact_selects_source_backed_player_update_profile(
        bool wet,
        bool honey,
        bool shimmer,
        float gravity,
        float maximumFallSpeed,
        float jumpSpeed,
        int jumpHeight)
    {
        var contacts = new VanillaLiquidContactState(wet, false, honey, shimmer);

        VanillaServerPlayerPhysicsParameters profile =
            VanillaServerPlayerPhysicsProfile.Resolve(in contacts);

        Assert.Equal(gravity, profile.Gravity);
        Assert.Equal(maximumFallSpeed, profile.MaximumFallSpeed);
        Assert.Equal(jumpSpeed, profile.JumpSpeed);
        Assert.Equal(jumpHeight, profile.JumpHeight);
    }

    [Fact]
    public void Runtime_carries_current_water_contact_into_next_tick_gravity()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, new WorldTile
        {
            LiquidAmount = byte.MaxValue,
            LiquidKind = WorldLiquidKind.Water
        });
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:water-physics");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        using (lease)
        {
            var states = new ServerPlayerStateStore(identities, slots.Capacity);
            Assert.True(states.TrySpawn(id, 96f, 80f, out PlayerStateSnapshot spawned));
            var runtime = new ServerRuntimeState(worldTiles: tiles, serverPlayers: new ServerPlayerAuthority(states, worldTiles: tiles));

            runtime.Tick();
            runtime.Tick();

            Assert.True(states.TryGet(spawned.Player, out PlayerStateSnapshot moved));
            Assert.Equal(80.5f, moved.PositionY, 5);
            Assert.Equal(0.6f, moved.VelocityY, 5);
        }
    }

    [Fact]
    public void Water_profile_drives_jump_speed_and_height_while_contact_remains_wet()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, new WorldTile
        {
            LiquidAmount = byte.MaxValue,
            LiquidKind = WorldLiquidKind.Water
        });
        using SpawnedServerPlayer player = Spawn();
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);
        VanillaServerPlayerJumpState jump = VanillaServerPlayerJumpState.Initial;
        var previous = new VanillaLiquidContactState(Wet: true, Lava: false, Honey: false, Shimmer: false);
        PlayerStateSnapshot snapshot = player.Snapshot;

        Assert.True(stepper.TryStep(
            in snapshot,
            ServerPlayerHorizontalIntent.Stop,
            ServerPlayerJumpIntent.Held,
            in jump,
            in previous,
            out ServerPlayerDryPhysicsStepResult next,
            out VanillaServerPlayerJumpState nextJump));

        Assert.Equal(-5.81f, next.VelocityY, 5);
        Assert.Equal(30, nextJump.RemainingTicks);
        Assert.True(next.LiquidContacts.Wet);
    }

    [Fact]
    public void Reused_slot_does_not_inherit_previous_generation_liquid_contact()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, new WorldTile
        {
            LiquidAmount = byte.MaxValue,
            LiquidKind = WorldLiquidKind.Water
        });
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var firstId = new ServerPlayerId("test:wet-generation");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(firstId, out var firstLease));
        Assert.NotNull(firstLease);
        Assert.True(states.TrySpawn(firstId, 96f, 80f, out PlayerStateSnapshot first));
        var runtime = new ServerRuntimeState(worldTiles: tiles, serverPlayers: new ServerPlayerAuthority(states, worldTiles: tiles));
        runtime.Tick();
        Assert.True(states.TryRemove(first.Player, out _));
        firstLease.Dispose();

        var replacementId = new ServerPlayerId("test:dry-generation");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(replacementId, out var replacement));
        Assert.NotNull(replacement);
        using (replacement)
        {
            Assert.True(states.TrySpawn(replacementId, 160f, 80f, out PlayerStateSnapshot spawned));

            runtime.Tick();

            Assert.True(states.TryGet(spawned.Player, out PlayerStateSnapshot moved));
            Assert.Equal(0.4f, moved.VelocityY, 5);
        }
    }

    private static SpawnedServerPlayer Spawn()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var id = new ServerPlayerId("test:liquid-jump");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var lease));
        Assert.NotNull(lease);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        Assert.True(states.TrySpawn(id, 96f, 80f, out PlayerStateSnapshot snapshot));
        return new SpawnedServerPlayer(lease, snapshot);
    }

    private sealed class SpawnedServerPlayer(
        ServerPlayerSlotRegistry.ServerPlayerSlotLease lease,
        PlayerStateSnapshot snapshot) : IDisposable
    {
        public PlayerStateSnapshot Snapshot { get; } = snapshot;

        public void Dispose() => lease.Dispose();
    }
}
