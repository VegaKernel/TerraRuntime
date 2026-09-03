using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Deterministic vanilla-oriented NPC damage resolver for the verified combat slice.
/// Source-specific damage scaling/variation is upstream; this stage applies NPC defense,
/// flat armor penetration and the ordinary critical multiplier. Runtime AI may supply a negative
/// defense value for source-backed boss phases and that value must remain negative through damage math.
/// </summary>
public static class VanillaNpcDamageResolver
{
    public const float DefenseEffectiveness = 0.5f;
    public const float CriticalDamageMultiplier = 2f;

    public static bool TryResolve(
        in VanillaNpcDefinition definition,
        in NpcDamageRequest request,
        out int effectiveDefense,
        out int resolvedDamage) =>
        TryResolve(definition.Defense, in request, out effectiveDefense, out resolvedDamage);

    public static bool TryResolve(
        int defense,
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

        var attack = new AuthoritativeAttackDamage(
            request.Source,
            request.BaseDamage,
            request.ArmorPenetration,
            request.Critical,
            request.KnockBack,
            request.HitDirection);
        if (!attack.IsValid)
        {
            effectiveDefense = 0;
            resolvedDamage = 0;
            return false;
        }

        // Existing flat armor-penetration semantics are retained for non-negative defense. Vanilla Eye AI can
        // intentionally write negative defense; checkArmorPenetration treats defense <= 0 as zero penetration,
        // so the negative value reaches CalculateDamageNPCsTake unchanged and increases incoming damage.
        effectiveDefense = defense <= 0
            ? defense
            : Math.Max(defense - attack.ArmorPenetration, 0);
        var mitigation = new TargetMitigation(
            defense,
            effectiveDefense,
            Endurance: 0f,
            Immune: false,
            Dodged: false,
            NoKnockback: false);
        float damage = Math.Max(
            attack.Damage - effectiveDefense * DefenseEffectiveness,
            1f);

        if (attack.Critical)
            damage *= CriticalDamageMultiplier;

        int hpDamage = damage >= int.MaxValue
            ? int.MaxValue
            : Math.Max((int)damage, 1);
        var final = new FinalDamageToHp(hpDamage, mitigation);
        if (!final.IsValid)
        {
            resolvedDamage = 0;
            return false;
        }
        resolvedDamage = final.Damage;
        return true;
    }
}

/// <summary>
/// Applies one generation-safe NPC damage transition to the authoritative runtime store.
/// Lethal damage commits Life=0 but deliberately does not despawn the NPC or run loot/death
/// side effects; those observable ordering rules belong to the later death pipeline. Runtime-owned
/// invulnerability and dynamic defense are checked from the same NPC revision as life/AI state so transient boss
/// phases cannot race separate combat flags. When an interaction ledger is supplied, a generation-valid player
/// item/projectile attack records the player slot before later strike rejection, matching TerrariaServer packet-28
/// ordering where NPC.PlayerInteraction runs after the NPC-generation check and before StrikeNPC.
/// </summary>
public sealed class RuntimeNpcDamageExecutor
{
    private readonly RuntimeNpcStore _store;
    private readonly bool _expertMode;
    private readonly RuntimeNpcPlayerInteractionLedger? _interactions;

    public RuntimeNpcDamageExecutor(
        RuntimeNpcStore store,
        bool expertMode = false,
        RuntimeNpcPlayerInteractionLedger? interactions = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _expertMode = expertMode;
        _interactions = interactions;
    }

    public bool TryApply(in NpcDamageRequest request, out NpcDamageResult result)
    {
        if (!request.IsValid || !_store.TryGet(request.Target, out NpcSnapshot current))
        {
            result = default;
            return false;
        }

        // MessageBuffer packet 28 calls NPC.PlayerInteraction after validating the exact NPC generation and before
        // StrikeNPC. Keep that observable ordering: invulnerable/rejected strikes may still grant interaction credit,
        // while stale generations and malformed requests never do.
        if (request.Source.Kind is DamageSourceKind.PlayerItem or DamageSourceKind.PlayerProjectile)
            _interactions?.TryMark(current.Handle, request.Source.Player);

        if (current.Simulation.DontTakeDamage ||
            current.Simulation.LifeMax <= 0 ||
            current.Simulation.Life <= 0 ||
            !VanillaNpcDefinitionCatalog.TryGet(
                current.TypeIdentity,
                current.NetIdentity,
                out VanillaNpcDefinition definition))
        {
            result = default;
            return false;
        }

        int defense = current.Simulation.DefenseOverride ?? definition.Defense;
        if (!VanillaNpcDamageResolver.TryResolve(
                defense,
                in request,
                out int effectiveDefense,
                out int damage))
        {
            result = default;
            return false;
        }

        int lifeBefore = current.Simulation.Life;
        int lifeAfter = Math.Max(0, lifeBefore - damage);
        VanillaNpcKnockbackResult knockback = VanillaNpcKnockbackResolver.Resolve(
            current.VelocityX,
            current.VelocityY,
            current.Simulation.NoGravity,
            current.Simulation.LifeMax,
            definition.KnockBackResist,
            request.KnockBack,
            request.HitDirection,
            damage,
            request.Critical,
            _expertMode);

        NpcSimulationState simulation = current.Simulation with
        {
            Life = lifeAfter,
            JustHit = true
        };

        var update = new NpcStateUpdate(
            current.Type,
            current.NetId,
            current.PositionX,
            current.PositionY,
            knockback.VelocityX,
            knockback.VelocityY,
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
            defense,
            effectiveDefense,
            damage,
            lifeBefore,
            lifeAfter,
            request.Critical);
        return true;
    }
}
