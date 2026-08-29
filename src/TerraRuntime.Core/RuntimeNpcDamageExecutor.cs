using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Deterministic vanilla-oriented NPC damage resolver for the verified combat slice.
/// Source-specific damage scaling/variation is upstream; this stage applies NPC defense,
/// flat armor penetration and the ordinary critical multiplier.
/// </summary>
public static class VanillaNpcDamageResolver
{
    public const float DefenseEffectiveness = 0.5f;
    public const float CriticalDamageMultiplier = 2f;

    public static bool TryResolve(
        in VanillaNpcDefinition definition,
        in NpcDamageRequest request,
        out int effectiveDefense,
        out int resolvedDamage)
    {
        if (!request.IsValid)
        {
            effectiveDefense = 0;
            resolvedDamage = 0;
            return false;
        }

        int defense = Math.Max(definition.Defense, 0);
        effectiveDefense = Math.Max(defense - request.ArmorPenetration, 0);
        float damage = Math.Max(
            request.BaseDamage - effectiveDefense * DefenseEffectiveness,
            1f);

        if (request.Critical)
            damage *= CriticalDamageMultiplier;

        resolvedDamage = damage >= int.MaxValue
            ? int.MaxValue
            : Math.Max((int)damage, 1);
        return true;
    }
}

/// <summary>
/// Applies one generation-safe NPC damage transition to the authoritative runtime store.
/// Lethal damage commits Life=0 but deliberately does not despawn the NPC or run loot/death
/// side effects; those observable ordering rules belong to the later death pipeline.
/// </summary>
public sealed class RuntimeNpcDamageExecutor
{
    private readonly RuntimeNpcStore _store;

    public RuntimeNpcDamageExecutor(RuntimeNpcStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public bool TryApply(in NpcDamageRequest request, out NpcDamageResult result)
    {
        if (!request.IsValid ||
            !_store.TryGet(request.Target, out NpcSnapshot current) ||
            current.Simulation.LifeMax <= 0 ||
            current.Simulation.Life <= 0 ||
            !VanillaNpcDefinitionCatalog.TryGet(current.Type, out VanillaNpcDefinition definition) ||
            !VanillaNpcDamageResolver.TryResolve(
                in definition,
                in request,
                out int effectiveDefense,
                out int damage))
        {
            result = default;
            return false;
        }

        int lifeBefore = current.Simulation.Life;
        int lifeAfter = Math.Max(0, lifeBefore - damage);
        NpcSimulationState simulation = current.Simulation with { Life = lifeAfter };

        var update = new NpcStateUpdate(
            current.Type,
            current.NetId,
            current.PositionX,
            current.PositionY,
            current.VelocityX,
            current.VelocityY,
            current.Target,
            current.Ai,
            simulation);

        if (!_store.TryUpdate(current.Handle, in update, out NpcSnapshot committed))
        {
            result = default;
            return false;
        }

        result = new NpcDamageResult(
            committed.Handle,
            committed.Revision,
            request.Source,
            request.BaseDamage,
            Math.Max(definition.Defense, 0),
            effectiveDefense,
            damage,
            lifeBefore,
            lifeAfter,
            request.Critical);
        return true;
    }
}
