using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

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
/// Ordinary lethal damage commits Life=0 but deliberately does not despawn the NPC or run loot/death
/// side effects; source-backed checkDead death-animation exceptions such as Moon Lord parts/core are prepared
/// atomically here so every combat ingress observes the same surviving revision. Later irreversible death/loot
/// ordering remains in the death pipeline. Runtime-owned
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

        NpcAiState ai = current.Ai;
        bool deathIntercepted = false;
        bool spawnMoonLordTrueEye = false;
        NpcSimulationState simulation = current.Simulation with
        {
            Life = lifeAfter,
            JustHit = true
        };

        // TerrariaServer 1.4.5.8 NPC.checkDead does not actually kill Moon Lord hands/head/core on the first
        // lethal strike. PrepareForDeathAnimation restores life, makes the NPC invulnerable and leaves a special
        // ai[] state for the normal authoritative AI loop. Keep that transition in the shared damage boundary so
        // packet 28, projectile hits and Town-NPC melee cannot disagree or briefly commit a false dead revision.
        if (lifeAfter == 0)
        {
            if ((current.TypeIdentity == VanillaNpcIds.MoonLordHand || current.TypeIdentity == VanillaNpcIds.MoonLordHead) &&
                current.Ai.Ai0 != -2f)
            {
                ai = current.TypeIdentity == VanillaNpcIds.MoonLordHand
                    ? current.Ai with { Ai0 = -2f, Ai1 = 0f }
                    : current.Ai with { Ai0 = -2f, Ai2 = 0f };
                simulation = simulation with
                {
                    Life = current.Simulation.LifeMax,
                    DontTakeDamage = true
                };
                deathIntercepted = true;
                spawnMoonLordTrueEye = true;
            }
            else if (current.TypeIdentity == VanillaNpcIds.MoonLordCore && current.Ai.Ai0 != 2f)
            {
                ai = current.Ai with { Ai0 = 2f };
                simulation = simulation with
                {
                    Life = current.Simulation.LifeMax,
                    DontTakeDamage = true
                };
                deathIntercepted = true;
            }
        }

        var update = new NpcStateUpdate(
            current.Type,
            current.NetId,
            current.PositionX,
            current.PositionY,
            knockback.VelocityX,
            knockback.VelocityY,
            current.Target,
            ai,
            simulation);

        if (!_store.TryUpdate(current.Handle, in update, out NpcSnapshot committed))
        {
            result = default;
            return false;
        }

        if (spawnMoonLordTrueEye)
            TrySpawnMoonLordTrueEye(in committed);

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
            request.Critical,
            DeathIntercepted: deathIntercepted);
        return true;
    }

    private void TrySpawnMoonLordTrueEye(in NpcSnapshot retiredPart)
    {
        if ((retiredPart.TypeIdentity != VanillaNpcIds.MoonLordHand &&
             retiredPart.TypeIdentity != VanillaNpcIds.MoonLordHead) ||
            !VanillaNpcDefinitionCatalog.TryGet(
                retiredPart.TypeIdentity,
                retiredPart.NetIdentity,
                out VanillaNpcDefinition partDefinition))
        {
            return;
        }

        int trueEyeCount = 0;
        float attackClock = retiredPart.Ai.Ai1;
        bool needsHeadClock = retiredPart.TypeIdentity == VanillaNpcIds.MoonLordHand;
        bool foundHead = false;
        for (int slot = 0; slot < _store.Capacity; slot++)
        {
            if (!_store.TryGetActive(checked((byte)slot), out NpcSnapshot peer))
                continue;

            if (peer.TypeIdentity == VanillaNpcIds.MoonLordFreeEye)
                trueEyeCount++;
            if (needsHeadClock && !foundHead && peer.TypeIdentity == VanillaNpcIds.MoonLordHead)
            {
                // NPC.FindFirstNPC(396) is global rather than root-scoped in the pinned helper.
                attackClock = peer.Ai.Ai1;
                foundHead = true;
            }
        }

        // TerrariaServer 1.4.5.8 MoonLord_SpawnTrueEyeOfCthulhu: 1200-tick head loop,
        // first offset 188 + 1200/3 = 588, then another 400 ticks per already-active True Eye.
        const int attackLoop = 1200;
        const int phaseStep = attackLoop / 3;
        int phaseOffset = 188 + phaseStep + phaseStep * trueEyeCount;
        float phase = (attackClock - phaseOffset) % attackLoop;
        if (phase < 0f)
            phase += attackLoop;

        float rootSlot = retiredPart.Ai.Ai3;
        float encodedOwner = float.IsFinite(rootSlot) && rootSlot >= 0f && rootSlot < byte.MaxValue
            ? rootSlot + 1f
            : 0f;
        var intent = new NpcAiSpawnIntent(
            VanillaNpcIds.MoonLordFreeEye,
            (int)(retiredPart.PositionX + partDefinition.Width * .5f),
            (int)(retiredPart.PositionY + partDefinition.Height * .5f),
            0f,
            0f,
            retiredPart.Target)
        {
            InitialAi = new NpcAiState(-2f, phase, 0f, rootSlot),
            InitialLocalAi = new NpcAiState(0f, 0f, 0f, encodedOwner)
        };

        // Vanilla NewNPC is best-effort when the NPC table is full; death animation still proceeds.
        _store.TrySpawnIntent(in intent, out _);
    }
}
