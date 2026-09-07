using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcChaseabilityTests
{
    [Theory]
    [InlineData(3, false, true, false)]
    [InlineData(22, true, true, false)]
    [InlineData(440, false, false, false)]
    [InlineData(523, false, false, false)]
    public void Spawn_materializes_verified_instance_flags(int type, bool friendly, bool chaseable, bool immortal)
    {
        var store = new RuntimeNpcStore(2);
        Assert.True(store.TrySpawn(0, Create(type), out NpcSnapshot npc));
        Assert.Equal(friendly, npc.Simulation.Friendly);
        Assert.Equal(chaseable, npc.Simulation.Chaseable);
        Assert.Equal(immortal, npc.Simulation.Immortal);
    }

    [Fact]
    public void State_only_updates_preserve_flags_and_type_replacement_materializes_new_defaults()
    {
        var store = new RuntimeNpcStore(2);
        Assert.True(store.TrySpawn(0, Create(3), out NpcSnapshot npc));
        NpcStateUpdate changed = Create(3) with
        {
            Simulation = npc.Simulation with { Friendly = true, Chaseable = false, Immortal = true }
        };
        Assert.True(store.TryUpdate(npc.Handle, changed, out _));
        Assert.True(store.TryUpdate(npc.Handle, Create(3), out NpcSnapshot preserved));
        Assert.True(preserved.Simulation.Friendly);
        Assert.False(preserved.Simulation.Chaseable);
        Assert.True(preserved.Simulation.Immortal);
        Assert.True(store.TryUpdate(npc.Handle, Create(523), out NpcSnapshot transformed));
        Assert.False(transformed.Simulation.Friendly);
        Assert.False(transformed.Simulation.Chaseable);
        Assert.False(transformed.Simulation.Immortal);
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, true)]
    public void Controlled_magic_rejects_each_live_gate_then_accepts_restored_instance(
        bool friendly, bool chaseable, bool immortal, bool dontTakeDamage)
    {
        var store = new RuntimeNpcStore(2);
        var resolver = new VanillaProjectileNpcTargetResolver(store, new WorldTileStore(new WorldDimensions(100, 100)));
        Assert.True(store.TrySpawn(0, Create(3), out NpcSnapshot npc));
        Assert.True(resolver.TryGetChaseableTargetCenter(0, out _, out _));
        NpcStateUpdate changed = Create(3) with
        {
            Simulation = npc.Simulation with
            {
                Friendly = friendly, Chaseable = chaseable, Immortal = immortal, DontTakeDamage = dontTakeDamage
            }
        };
        Assert.True(store.TryUpdate(npc.Handle, changed, out _));
        Assert.False(resolver.TryGetChaseableTargetCenter(0, out _, out _));
        Assert.True(store.TryUpdate(npc.Handle, changed with { Simulation = npc.Simulation }, out _));
        Assert.True(resolver.TryGetChaseableTargetCenter(0, out _, out _));
    }

    [Theory]
    [InlineData(440)]
    [InlineData(523)]
    public void Controlled_magic_does_not_lock_clone_or_ancient_doom(int type)
    {
        var store = new RuntimeNpcStore(2);
        Assert.True(store.TrySpawn(0, Create(type), out _));
        var resolver = new VanillaProjectileNpcTargetResolver(store, new WorldTileStore(new WorldDimensions(100, 100)));
        Assert.False(resolver.TryGetChaseableTargetCenter(0, out _, out _));
        Assert.False(resolver.TryGetCelebrationRocketTargetCenter(0, 20, 20, 800, out _, out _));
    }

    [Fact]
    public void Unmaterialized_flags_fail_closed_and_ignore_damage_gate_does_not_bypass_other_flags()
    {
        var npc = new NpcSnapshot(new NpcHandle(0, new NpcGeneration(1)), new NpcRevision(1),
            3, 3, 0, 0, 0, 0, 0, default, NpcSimulationState.Initial with { Life = 45, LifeMax = 45 });
        Assert.False(VanillaNpcChaseability1458.CanBeChasedBy(in npc));
        npc = npc with { Simulation = npc.Simulation with { Friendly = false, Chaseable = true, Immortal = false, DontTakeDamage = true } };
        Assert.False(VanillaNpcChaseability1458.CanBeChasedBy(in npc));
        Assert.True(VanillaNpcChaseability1458.CanBeChasedBy(in npc, ignoreDontTakeDamage: true));
        npc = npc with { Simulation = npc.Simulation with { Immortal = true } };
        Assert.False(VanillaNpcChaseability1458.CanBeChasedBy(in npc, ignoreDontTakeDamage: true));
    }

    private static NpcStateUpdate Create(int type) => new(type, checked((short)type), 32, 32, 0, 0,
        VanillaNpcDefinitionCatalog.DefaultTarget, default, NpcSimulationState.Initial);
}
