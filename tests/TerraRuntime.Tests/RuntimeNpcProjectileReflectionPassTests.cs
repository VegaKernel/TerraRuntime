using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcProjectileReflectionPassTests
{
    [Fact]
    public void Overlapping_good_world_eye_reflects_player_arrow_once_and_commits_source_runtime_state()
    {
        var npcs = new RuntimeNpcStore(capacity: 4);
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        NpcSnapshot eye = SpawnReflectingEye(npcs);
        ProjectileSnapshot arrow = SpawnArrow(projectiles);
        var pass = new RuntimeNpcProjectileReflectionPass(
            npcs,
            projectiles,
            new FixedPlayerLookup(),
            new SequenceRandom(100, 0));

        Assert.Equal(1, pass.Tick());
        Assert.True(projectiles.TryGet(arrow.Handle, out ProjectileSnapshot reflected));
        Assert.Equal((short)5, reflected.Damage);
        Assert.Equal(5f, MathF.Sqrt(reflected.VelocityX * reflected.VelocityX + reflected.VelocityY * reflected.VelocityY), 4);
        Assert.Equal(arrow.Spawner, reflected.Spawner);
        Assert.True(projectiles.TryGetLifecycle(arrow.Handle, out ProjectileLifecycleState lifecycle));
        Assert.True(lifecycle.Reflected);
        Assert.Equal(1, lifecycle.PenetrateOverride);
        Assert.Equal(3f, lifecycle.OldVelocityX);
        Assert.Equal(4f, lifecycle.OldVelocityY);

        Assert.Equal(0, pass.Tick());
        Assert.True(npcs.TryGet(eye.Handle, out _));
    }

    [Fact]
    public void Non_overlapping_or_non_reflecting_eye_does_not_mutate_projectile()
    {
        var npcs = new RuntimeNpcStore(capacity: 4);
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        NpcSnapshot eye = SpawnReflectingEye(npcs);
        var disabled = new NpcStateUpdate(
            eye.Type, eye.NetId, eye.PositionX, eye.PositionY, eye.VelocityX, eye.VelocityY,
            eye.Target, eye.Ai, eye.Simulation with { ReflectsProjectiles = false });
        Assert.True(npcs.TryUpdate(eye.Handle, in disabled, out _));
        ProjectileSnapshot arrow = SpawnArrow(projectiles);
        var pass = new RuntimeNpcProjectileReflectionPass(
            npcs,
            projectiles,
            new FixedPlayerLookup(),
            new SequenceRandom(100, 0));

        Assert.Equal(0, pass.Tick());
        Assert.True(projectiles.TryGet(arrow.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal((short)20, unchanged.Damage);
        Assert.True(projectiles.TryGetLifecycle(arrow.Handle, out ProjectileLifecycleState lifecycle));
        Assert.False(lifecycle.Reflected);
    }

    private static NpcSnapshot SpawnReflectingEye(RuntimeNpcStore store)
    {
        var update = new NpcStateUpdate(
            VanillaNpcIds.EyeOfCthulhu.Value,
            checked((short)VanillaNpcIds.EyeOfCthulhu.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: new NpcAiState(2f, 50f, 0f, 0f),
            Simulation: NpcSimulationState.Initial with { ReflectsProjectiles = true });
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot eye));
        return eye;
    }

    private static ProjectileSnapshot SpawnArrow(RuntimeProjectileStore store)
    {
        var update = new ProjectileStateUpdate(
            VanillaProjectileIds.WoodenArrowFriendly,
            Spawner: 0,
            PositionX: 120f,
            PositionY: 120f,
            VelocityX: 3f,
            VelocityY: 4f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);
        Assert.True(store.TrySpawn(0, in update, out ProjectileSnapshot arrow));
        Assert.True(store.TryGetLifecycle(arrow.Handle, out ProjectileLifecycleState lifecycle));
        Assert.True(store.TryCommitSimulationStep(
            arrow.Handle,
            in update,
            lifecycle.TimeLeft,
            out arrow,
            out bool expired));
        Assert.False(expired);
        return arrow;
    }

    private sealed class FixedPlayerLookup : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            if (slot.Value != 0)
            {
                snapshot = default;
                return false;
            }

            snapshot = new PlayerStateSnapshot(
                new PlayerHandle(slot, new PlayerSessionGeneration(1)),
                new PlayerStateRevision(1),
                Team: 0,
                ControlFlags: 0,
                MovementFlags: 0,
                MiscFlags1: 0,
                MiscFlags2: 0,
                SelectedItem: 0,
                PositionX: 300f,
                PositionY: 100f,
                VelocityX: 0f,
                VelocityY: 0f,
                MountType: 0,
                PotionOfReturnOriginalPositionX: 0f,
                PotionOfReturnOriginalPositionY: 0f,
                PotionOfReturnHomePositionX: 0f,
                PotionOfReturnHomePositionY: 0f,
                CameraTargetX: 0f,
                CameraTargetY: 0f);
            return true;
        }
    }

    private sealed class SequenceRandom(params int[] values) : IVanillaProjectileReflectionRandom
    {
        private int index;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            if (index >= values.Length)
                throw new Xunit.Sdk.XunitException("Reflection RNG consumed more values than expected.");
            int value = values[index++];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
