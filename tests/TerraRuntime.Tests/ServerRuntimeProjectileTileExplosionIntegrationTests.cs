using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeProjectileTileExplosionIntegrationTests
{
    [Theory]
    [InlineData(718)] // Celebration Rocket IV, aiStyle 147
    [InlineData(796)] // Mini Nuke II rocket, aiStyle 16
    public void Trusted_explosive_projectile_reaches_natural_collision_and_destroys_terrain(int rawType)
    {
        var tiles = new WorldTileStore(new WorldDimensions(120, 100));
        SetActiveTile(tiles, 60, 50, VanillaTileIds.Dirt);
        var projectiles = new RuntimeProjectileStore(capacity: 8);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);

        ProjectileTypeId type = new(rawType);
        ProjectileStateUpdate update = CreateCenteredUpdate(type, playerSlot: 1, tileX: 50, tileY: 50) with
        {
            VelocityX = 8f
        };
        Assert.True(projectiles.TrySpawnVanilla(in update, out ProjectileSnapshot projectile));
        var owner = new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1));
        Assert.True(projectiles.TryMarkCombatTrusted(projectile.Handle, owner));

        for (int tick = 0; tick < 120 && projectiles.TryGet(projectile.Handle, out _); tick++)
            state.Tick();

        Assert.False(projectiles.TryGet(projectile.Handle, out _));
        Assert.False(tiles.Get(60, 50).IsActive);
    }

    [Fact]
    public void Trusted_celebration_rocket_kill_applies_authoritative_radius_five_tile_explosion()
    {
        var tiles = new WorldTileStore(new WorldDimensions(120, 100));
        SetActiveTile(tiles, 60, 50, VanillaTileIds.Dirt);
        SetActiveTile(tiles, 64, 50, VanillaTileIds.Dirt); // strictly inside radius 5
        SetActiveTile(tiles, 65, 50, VanillaTileIds.Dirt); // exactly radius 5: vanilla keeps it
        SetActiveTile(tiles, 61, 50, VanillaTileIds.Containers); // CanExplodeTile protects basic chests
        SetActiveTile(tiles, 60, 51, VanillaTileIds.BlueDungeonBrick);
        SetActiveTile(tiles, 60, 52, VanillaTileIds.LihzahrdBrick);

        var projectiles = new RuntimeProjectileStore(capacity: 8);
        var state = new ServerRuntimeState(
            worldTiles: tiles,
            projectiles: projectiles,
            projectileStepper: new KillOnFirstStep());

        ProjectileSnapshot projectile = SpawnTrustedAtTileCenter(
            projectiles,
            VanillaProjectileIds.CelebrationRocketIV,
            playerSlot: 3,
            tileX: 60,
            tileY: 50);

        state.Tick();

        Assert.False(tiles.Get(60, 50).IsActive);
        Assert.False(tiles.Get(64, 50).IsActive);
        Assert.True(tiles.Get(65, 50).IsActive);
        Assert.Equal(VanillaTileIds.Containers, tiles.Get(61, 50).TileType);
        Assert.Equal(VanillaTileIds.BlueDungeonBrick, tiles.Get(60, 51).TileType);
        Assert.Equal(VanillaTileIds.LihzahrdBrick, tiles.Get(60, 52).TileType);
        Assert.False(projectiles.TryGet(projectile.Handle, out _));
    }

    [Fact]
    public void Trusted_mini_nuke_two_kill_uses_radius_seven_and_non_trusted_projectile_cannot_mutate_terrain()
    {
        var trustedTiles = new WorldTileStore(new WorldDimensions(120, 100));
        SetActiveTile(trustedTiles, 60, 50, VanillaTileIds.Dirt);
        SetActiveTile(trustedTiles, 66, 50, VanillaTileIds.Dirt);
        SetActiveTile(trustedTiles, 67, 50, VanillaTileIds.Dirt);

        var trustedProjectiles = new RuntimeProjectileStore(capacity: 8);
        var trustedState = new ServerRuntimeState(
            worldTiles: trustedTiles,
            projectiles: trustedProjectiles,
            projectileStepper: new KillOnFirstStep());
        _ = SpawnTrustedAtTileCenter(
            trustedProjectiles,
            VanillaProjectileIds.MiniNukeRocketII,
            playerSlot: 2,
            tileX: 60,
            tileY: 50);

        trustedState.Tick();

        Assert.False(trustedTiles.Get(60, 50).IsActive);
        Assert.False(trustedTiles.Get(66, 50).IsActive);
        Assert.True(trustedTiles.Get(67, 50).IsActive);

        var untrustedTiles = new WorldTileStore(new WorldDimensions(120, 100));
        SetActiveTile(untrustedTiles, 60, 50, VanillaTileIds.Dirt);
        var untrustedProjectiles = new RuntimeProjectileStore(capacity: 8);
        var untrustedState = new ServerRuntimeState(
            worldTiles: untrustedTiles,
            projectiles: untrustedProjectiles,
            projectileStepper: new KillOnFirstStep());

        ProjectileStateUpdate update = CreateCenteredUpdate(
            VanillaProjectileIds.MiniNukeRocketII,
            playerSlot: 2,
            tileX: 60,
            tileY: 50);
        Assert.True(untrustedProjectiles.TrySpawnVanilla(in update, out _));

        untrustedState.Tick();

        Assert.True(untrustedTiles.Get(60, 50).IsActive);
    }

    [Fact]
    public void Trusted_explosion_can_remove_supported_wall_but_keeps_unbreakable_temple_wall()
    {
        var tiles = new WorldTileStore(new WorldDimensions(120, 100));
        WorldTile stoneWall = default;
        stoneWall.Wall = checked((ushort)VanillaWallIds.Stone.Value);
        tiles.Set(60, 50, in stoneWall);
        WorldTile templeWall = default;
        templeWall.Wall = checked((ushort)VanillaWallIds.UnbreakableTemple.Value);
        tiles.Set(61, 50, in templeWall);
        WorldTile lockedDungeonWall = default;
        lockedDungeonWall.Wall = checked((ushort)VanillaWallIds.BlueDungeonUnsafe.Value);
        tiles.Set(62, 50, in lockedDungeonWall);

        var projectiles = new RuntimeProjectileStore(capacity: 8);
        var state = new ServerRuntimeState(
            worldTiles: tiles,
            projectiles: projectiles,
            projectileStepper: new KillOnFirstStep());
        _ = SpawnTrustedAtTileCenter(
            projectiles,
            VanillaProjectileIds.CelebrationRocketIV,
            playerSlot: 1,
            tileX: 60,
            tileY: 50);

        state.Tick();

        Assert.Equal(VanillaWallIds.None, tiles.Get(60, 50).WallType);
        Assert.Equal(VanillaWallIds.UnbreakableTemple, tiles.Get(61, 50).WallType);
        Assert.Equal(VanillaWallIds.BlueDungeonUnsafe, tiles.Get(62, 50).WallType);
    }

    [Fact]
    public void Exact_owner_packet17_after_trusted_explosion_is_consumed_once_as_a_convergence_echo()
    {
        var tiles = new WorldTileStore(new WorldDimensions(120, 100));
        SetActiveTile(tiles, 60, 50, VanillaTileIds.Dirt);
        var projectiles = new RuntimeProjectileStore(capacity: 8);
        var state = new ServerRuntimeState(
            worldTiles: tiles,
            projectiles: projectiles,
            projectileStepper: new KillOnFirstStep());

        var slots = new PlayerSlotPool(capacity: 1);
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(44), session.Handle);
        var spawn = new PlayerSpawnCommitRequest(session.Slot, 900, 700, 0, 0, 0, 0, 0);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));
        Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);

        _ = SpawnTrustedAtTileCenter(
            projectiles,
            VanillaProjectileIds.CelebrationRocketIV,
            connection.Player.Slot.Value,
            tileX: 60,
            tileY: 50,
            connection.Player);
        state.Tick();

        var echo = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTile,
            TileX: 60,
            TileY: 50,
            Data: 0,
            Style: 0);
        state.Apply(new ClientTileManipulationRuntimeCommand(connection, echo));

        Assert.Equal(1, state.AcceptedProjectileExplosionEchoes);
        Assert.Equal(1, state.ValidatedClientTileManipulations);
        Assert.Equal(0, state.RejectedClientTileManipulations);

        state.Apply(new ClientTileManipulationRuntimeCommand(connection, echo));
        Assert.Equal(1, state.AcceptedProjectileExplosionEchoes);
        Assert.Equal(1, state.RejectedClientTileManipulations);
    }

    private static ProjectileSnapshot SpawnTrustedAtTileCenter(
        RuntimeProjectileStore projectiles,
        ProjectileTypeId type,
        byte playerSlot,
        int tileX,
        int tileY)
    {
        var owner = new PlayerHandle(new PlayerSlotId(playerSlot), new PlayerSessionGeneration(1));
        return SpawnTrustedAtTileCenter(projectiles, type, playerSlot, tileX, tileY, owner);
    }

    private static ProjectileSnapshot SpawnTrustedAtTileCenter(
        RuntimeProjectileStore projectiles,
        ProjectileTypeId type,
        byte playerSlot,
        int tileX,
        int tileY,
        PlayerHandle owner)
    {
        ProjectileStateUpdate update = CreateCenteredUpdate(type, playerSlot, tileX, tileY);
        Assert.True(projectiles.TrySpawnVanilla(in update, out ProjectileSnapshot projectile));
        Assert.True(projectiles.TryMarkCombatTrusted(projectile.Handle, owner));
        return projectile;
    }

    private static ProjectileStateUpdate CreateCenteredUpdate(
        ProjectileTypeId type,
        byte playerSlot,
        int tileX,
        int tileY)
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(type, out VanillaProjectileDefinition definition));
        float centerX = tileX * 16f;
        float centerY = tileY * 16f;
        return new ProjectileStateUpdate(
            type,
            playerSlot,
            centerX - definition.Width * 0.5f,
            centerY - definition.Height * 0.5f,
            VelocityX: 0f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 100,
            KnockBack: 4f,
            OriginalDamage: 100);
    }

    private static void SetActiveTile(WorldTileStore tiles, int x, int y, TileTypeId type)
    {
        var tile = new WorldTile
        {
            Type = checked((ushort)type.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(x, y, in tile);
    }

    private sealed class KillOnFirstStep : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileSnapshot current = projectile.Projectile;
            var state = new ProjectileStateUpdate(
                current.Type,
                current.Spawner,
                current.PositionX,
                current.PositionY,
                current.VelocityX,
                current.VelocityY,
                current.Ai,
                current.BannerIdToRespondTo,
                current.Damage,
                current.KnockBack,
                current.OriginalDamage);
            next = new ProjectileSimulationStepResult(
                state,
                TimeLeft: 0,
                TerminationReason: ProjectileSimulationTerminationReason.BehaviorKill);
            return true;
        }
    }
}
