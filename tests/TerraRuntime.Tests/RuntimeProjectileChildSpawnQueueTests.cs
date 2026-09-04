using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileChildSpawnQueueTests
{
    [Fact]
    public void Sharknado_bolt_kill_queues_only_generation_safe_npc_owned_child_work()
    {
        var queue = new RuntimeProjectileChildSpawnQueue(4);
        ProjectileSnapshot projectile = CreateBolt(ai1: 0f);
        var sourceNpc = new NpcHandle(17, new NpcGeneration(9));
        var termination = new ProjectileTerminationCommit(
            projectile,
            projectile,
            ProjectileSimulationTerminationReason.BehaviorKill,
            CombatTrusted: false,
            TrustedOwner: default,
            SourceNpc: sourceNpc);

        queue.ProjectileTerminated(in termination);
        Assert.Single(queue.Events.ToArray());
        Assert.Equal(projectile.Handle, queue.Events[0].Projectile.Handle);
        Assert.Equal(sourceNpc, queue.Events[0].SourceNpc);

        queue.Reset();
        ProjectileTerminationCommit worldBounds = termination with { Reason = ProjectileSimulationTerminationReason.WorldBounds };
        ProjectileTerminationCommit unowned = termination with { SourceNpc = default };
        queue.ProjectileTerminated(in worldBounds);
        queue.ProjectileTerminated(in unowned);
        Assert.Empty(queue.Events.ToArray());
    }

    [Theory]
    [InlineData(false, 40)]
    [InlineData(true, 25)]
    public void Ordinary_sharknado_bolt_kill_creates_source_384_with_vanilla_ai_and_damage(bool expertMode, int damage)
    {
        var sourceNpc = new NpcHandle(17, new NpcGeneration(9));
        var child = new RuntimeProjectileChildSpawnEvent(CreateBolt(ai1: 0f), sourceNpc);

        Assert.True(RuntimeSharknadoChildSpawn1458.TryCreateIntent(in child, null, expertMode, out NpcAiProjectileIntent intent));

        Assert.Equal(VanillaProjectileIds.Sharknado, intent.Type);
        Assert.Equal(10f, intent.PositionX, 5);
        Assert.Equal(190f, intent.PositionY, 5);
        Assert.Equal(-0.01f, intent.VelocityX, 5);
        Assert.Equal(0f, intent.VelocityY, 5);
        Assert.Equal(damage, intent.Damage);
        Assert.Equal(4f, intent.KnockBack, 5);
        Assert.Equal(new ProjectileAiState(16f, 15f, 0f), intent.InitialAi);
    }

    [Fact]
    public void Targeted_sharknado_bolt_kill_places_cthulunado_on_first_active_solid_or_liquid_tile()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 180));
        WorldTile ground = default;
        ground.Flags = WorldTileFlags.Active;
        ground.Type = 0; // Dirt is vanilla-solid.
        tiles.Set(10, 30, in ground);

        var sourceNpc = new NpcHandle(17, new NpcGeneration(9));
        ProjectileSnapshot bolt = CreateBolt(ai1: 6f) with { PositionX = 145f, PositionY = 225f };
        var child = new RuntimeProjectileChildSpawnEvent(bolt, sourceNpc);

        Assert.True(RuntimeSharknadoChildSpawn1458.TryCreateIntent(in child, tiles, expertMode: false, out NpcAiProjectileIntent intent));

        Assert.Equal(VanillaProjectileIds.Cthulunado, intent.Type);
        Assert.Equal(93f, intent.PositionX, 5);
        Assert.Equal(435f, intent.PositionY, 5);
        Assert.Equal(80, intent.Damage);
        Assert.Equal(4f, intent.KnockBack, 5);
        Assert.Equal(new ProjectileAiState(16f, 24f, 0f), intent.InitialAi);
    }

    [Fact]
    public void Spawned_sharknado_child_inherits_exact_npc_generation_provenance()
    {
        var store = new RuntimeProjectileStore(16);
        var sourceNpc = new NpcHandle(17, new NpcGeneration(9));
        var child = new RuntimeProjectileChildSpawnEvent(CreateBolt(ai1: 0f), sourceNpc);
        Assert.True(RuntimeSharknadoChildSpawn1458.TryCreateIntent(in child, null, expertMode: false, out NpcAiProjectileIntent intent));

        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(store, sourceNpc, in intent, out ProjectileSnapshot spawned));
        Assert.True(store.TryGetServerNpcSource(spawned.Handle, out NpcHandle actual));
        Assert.Equal(sourceNpc, actual);
    }

    private static ProjectileSnapshot CreateBolt(float ai1) =>
        new(
            new ProjectileHandle(8, new ProjectileGeneration(3)),
            new ProjectileRevision(4),
            VanillaProjectileIds.SharknadoBolt,
            Spawner: byte.MaxValue,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 2f,
            VelocityY: 8f,
            Ai: new ProjectileAiState(0f, ai1, 0f),
            BannerIdToRespondTo: 0,
            Damage: 0,
            KnockBack: 0f,
            OriginalDamage: 0);
}
