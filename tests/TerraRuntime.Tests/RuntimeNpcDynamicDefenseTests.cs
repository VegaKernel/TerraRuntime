using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcDynamicDefenseTests
{
    [Fact]
    public void Negative_defense_override_increases_damage_and_is_reported_without_clamping()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        var spawn = new NpcStateUpdate(
            Type: VanillaNpcIds.EyeOfCthulhu.Value,
            NetId: checked((short)VanillaNpcIds.EyeOfCthulhu.Value),
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: new NpcAiState(3f, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in spawn, out NpcSnapshot eye));

        var combatState = new NpcStateUpdate(
            eye.Type,
            eye.NetId,
            eye.PositionX,
            eye.PositionY,
            eye.VelocityX,
            eye.VelocityY,
            eye.Target,
            eye.Ai,
            eye.Simulation with { DefenseOverride = -30 });
        Assert.True(store.TryUpdate(eye.Handle, in combatState, out NpcSnapshot vulnerable));

        var executor = new RuntimeNpcDamageExecutor(store, expertMode: true);
        var request = new NpcDamageRequest(
            vulnerable.Handle,
            DamageSource.Server,
            BaseDamage: 100,
            ArmorPenetration: 20);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));

        Assert.Equal(-30, result.Defense);
        Assert.Equal(-30, result.EffectiveDefense);
        Assert.Equal(115, result.ResolvedDamage);
        Assert.Equal(vulnerable.Simulation.Life - 115, result.LifeAfter);
    }

    [Fact]
    public void Positive_definition_defense_keeps_existing_armor_penetration_semantics()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(
            VanillaNpcIds.Zombie,
            out VanillaNpcDefinition zombie));
        var request = new NpcDamageRequest(
            new NpcHandle(0, new NpcGeneration(1)),
            DamageSource.Server,
            BaseDamage: 10,
            ArmorPenetration: 2);

        Assert.True(VanillaNpcDamageResolver.TryResolve(
            in zombie,
            in request,
            out int effectiveDefense,
            out int damage));

        Assert.Equal(4, effectiveDefense);
        Assert.Equal(8, damage);
    }
}
