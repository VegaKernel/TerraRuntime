#!/usr/bin/env python3
from pathlib import Path


def read(path: str) -> str:
    return Path(path).read_text(encoding='utf-8-sig')


def write(path: str, text: str) -> None:
    Path(path).write_text(text, encoding='utf-8')


def replace_once(path: str, old: str, new: str, label: str) -> None:
    text = read(path)
    if new in text:
        return
    if text.count(old) != 1:
        raise SystemExit(f'{label}: anchor missing or ambiguous')
    write(path, text.replace(old, new, 1))


def replace_between(path: str, start: str, end: str, replacement: str, label: str) -> None:
    text = read(path)
    a = text.find(start)
    if a < 0:
        raise SystemExit(f'{label}: start anchor missing')
    b = text.find(end, a)
    if b < 0:
        raise SystemExit(f'{label}: end anchor missing')
    write(path, text[:a] + replacement + text[b:])

combat = 'src/TerraRuntime/RuntimeTownNpcCombat1458.cs'

melee_catalog = r'''internal readonly record struct VanillaTownNpcMeleeAttackProfile1458(
    NpcTypeId NpcType,
    int DangerDetectRange,
    int AttackTime,
    int AttackAverageChance,
    int BaseDamage,
    int HitboxWidth,
    int HitboxHeight,
    float KnockBack,
    int RecoveryBase,
    int RecoveryRandom);

/// <summary>
/// TerrariaServer 1.4.5.8 AI_007 AttackType=3 town melee profiles. The current source assigns this start path only
/// to Dye Trader, Tax Collector and Stylist. IsTownPet still has a defensive state-15 body in the source, but all
/// current town-pet identities have AttackType=-1/AttackTime=-1, so TerraRuntime intentionally does not invent a
/// natural pet melee entry that vanilla 1.4.5.8 cannot take.
/// </summary>
internal static class VanillaTownNpcMeleeAttackCatalog1458
{
    private static readonly VanillaTownNpcMeleeAttackProfile1458 DyeTrader = new(
        new NpcTypeId(207), 60, 15, 1, 11, 32, 32, 4.25f, 12, 6);
    private static readonly VanillaTownNpcMeleeAttackProfile1458 TaxCollector = new(
        new NpcTypeId(441), 50, 15, 1, 9, 28, 28, 3.5f, 9, 3);
    private static readonly VanillaTownNpcMeleeAttackProfile1458 Stylist = new(
        new NpcTypeId(353), 60, 12, 1, 10, 32, 32, 5f, 15, 8);

    public static bool TryGet(NpcTypeId type, out VanillaTownNpcMeleeAttackProfile1458 profile)
    {
        if (type.Value == 207)
        {
            profile = DyeTrader;
            return true;
        }
        if (type.Value == 441)
        {
            profile = TaxCollector;
            return true;
        }
        if (type.Value == 353)
        {
            profile = Stylist;
            return true;
        }
        profile = default;
        return false;
    }

    public static bool IsSourceTownPet(NpcTypeId type) => type.Value is
        637 or 638 or 656 or 670 or 678 or 679 or 680 or 681 or 682 or 683 or 684;
}

internal enum RuntimeTownNpcMeleeDamageResult1458 : byte
{
    Rejected = 0,
    Committed = 1,
    Killed = 2
}

internal interface IRuntimeTownNpcMeleeDamageSink1458
{
    RuntimeTownNpcMeleeDamageResult1458 TryStrike(
        NpcHandle attacker,
        NpcHandle target,
        int baseDamage,
        float knockBack,
        int hitDirection);
}

internal readonly record struct VanillaTownNpcSwingRectangle1458(int X, int Y, int Width, int Height)
{
    public bool Intersects(float x, float y, float width, float height) =>
        x < X + Width && x + width > X && y < Y + Height && y + height > Y;
}

'''
replace_once(
    combat,
    'internal readonly record struct RuntimeTownNpcCombatWorldFacts1458(',
    melee_catalog + 'internal readonly record struct RuntimeTownNpcCombatWorldFacts1458(',
    'melee catalog')

replace_once(
    combat,
    'internal readonly record struct RuntimeTownNpcCombatTickSummary1458(\n    int TownNpcsVisited,\n    int AttacksStarted,\n    int AttackStatesAdvanced,\n    int ProjectilesSpawned,\n    int RejectedCommits,\n    int UnsupportedTargets);',
    'internal readonly record struct RuntimeTownNpcCombatTickSummary1458(\n    int TownNpcsVisited,\n    int AttacksStarted,\n    int AttackStatesAdvanced,\n    int ProjectilesSpawned,\n    int MeleeHits,\n    int RejectedCommits,\n    int UnsupportedTargets);',
    'combat summary')

replace_once(
    combat,
    '    private readonly IRuntimeTownNpcCombatRandom1458 random;\n    private readonly NpcSnapshot[] peers;',
    '    private readonly IRuntimeTownNpcCombatRandom1458 random;\n    private readonly NpcSnapshot[] peers;\n    private readonly ulong[] meleeImmuneGenerations;\n    private readonly int[] meleeImmuneTicks;\n    private IRuntimeTownNpcMeleeDamageSink1458? meleeDamage;',
    'melee fields')

replace_once(
    combat,
    '        this.random = random ?? SharedRuntimeTownNpcCombatRandom1458.Instance;\n        peers = new NpcSnapshot[npcs.Capacity];\n    }',
    '        this.random = random ?? SharedRuntimeTownNpcCombatRandom1458.Instance;\n        peers = new NpcSnapshot[npcs.Capacity];\n        meleeImmuneGenerations = new ulong[npcs.Capacity];\n        meleeImmuneTicks = new int[npcs.Capacity];\n    }\n\n    public void SetMeleeDamageSink(IRuntimeTownNpcMeleeDamageSink1458 sink) =>\n        meleeDamage = sink ?? throw new ArgumentNullException(nameof(sink));',
    'melee constructor state')

tick = r'''    public RuntimeTownNpcCombatTickSummary1458 Tick()
    {
        AdvanceMeleeImmunity();
        int peerCount = npcs.CopyActive(peers);
        int visited = 0;
        int started = 0;
        int advanced = 0;
        int spawned = 0;
        int meleeHits = 0;
        int rejected = 0;
        int unsupportedTargets = 0;

        Span<RuntimeTownNpcHomeCommit> roster = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
        int townCount = townNpcs.CopyHomeBaselines(roster);
        for (int index = 0; index < townCount; index++)
        {
            RuntimeTownNpcHomeCommit resident = roster[index];
            if ((uint)resident.NpcSlot > byte.MaxValue ||
                !npcs.TryGetActive(checked((byte)resident.NpcSlot), out NpcSnapshot source) ||
                !NpcTypeId.TryCreate(source.Type, out NpcTypeId sourceType))
            {
                continue;
            }

            bool projectileAttack = VanillaTownNpcProjectileAttackCatalog1458.TryGet(
                sourceType, out VanillaTownNpcProjectileAttackProfile1458 projectileProfile);
            bool meleeAttack = VanillaTownNpcMeleeAttackCatalog1458.TryGet(
                sourceType, out VanillaTownNpcMeleeAttackProfile1458 meleeProfile);
            if (!projectileAttack && !meleeAttack)
                continue;

            visited++;
            NpcSimulationState simulation = source.Simulation;
            NpcAiState localAi = simulation.LocalAi;
            if (localAi.Ai1 > 0f)
                localAi = localAi with { Ai1 = localAi.Ai1 - 1f };

            int dangerDetectRange = projectileAttack
                ? projectileProfile.DangerDetectRange
                : meleeProfile.DangerDetectRange;
            bool hasTarget = TrySelectTarget(
                in source,
                dangerDetectRange,
                peers.AsSpan(0, peerCount),
                out NpcSnapshot target,
                out int direction);
            if (!hasTarget)
                unsupportedTargets++;

            if (projectileAttack && source.Ai.Ai0 == projectileProfile.AttackState)
            {
                if (!TryAdvanceAttack(
                        in source,
                        sourceType,
                        in projectileProfile,
                        hasTarget ? target : default,
                        hasTarget,
                        direction,
                        localAi,
                        out bool projectileSpawned))
                {
                    rejected++;
                }
                else
                {
                    advanced++;
                    if (projectileSpawned)
                        spawned++;
                }
                continue;
            }

            if (meleeAttack && source.Ai.Ai0 == 15f)
            {
                if (!TryAdvanceMeleeAttack(
                        in source,
                        sourceType,
                        in meleeProfile,
                        peers.AsSpan(0, peerCount),
                        localAi,
                        out int committedHits))
                {
                    rejected++;
                }
                else
                {
                    advanced++;
                    meleeHits += committedHits;
                }
                continue;
            }

            if (localAi != simulation.LocalAi)
            {
                simulation = simulation with { LocalAi = localAi };
                var cooldownUpdate = SnapshotUpdate(in source, source.Ai, simulation, source.VelocityX, source.VelocityY);
                if (!npcs.TryUpdate(source.Handle, in cooldownUpdate, out source))
                {
                    rejected++;
                    continue;
                }
            }

            if (!hasTarget ||
                source.VelocityY != 0f ||
                source.Simulation.Wet ||
                source.Ai.Ai0 is not (0f or 1f or 8f) ||
                source.Simulation.LocalAi.Ai1 > 0f)
            {
                continue;
            }

            if (projectileAttack &&
                projectileProfile.Kind == VanillaTownNpcProjectileAttackKind1458.Straight &&
                !HasStraightAttackAngle(in source, in target))
            {
                continue;
            }

            int averageChance = projectileAttack
                ? projectileProfile.AttackAverageChance
                : meleeProfile.AttackAverageChance;
            int chance = GetAttackChance(averageChance);
            if (random.Next(chance) != 0)
                continue;

            float attackState = projectileAttack ? projectileProfile.AttackState : 15f;
            int attackTime = projectileAttack ? projectileProfile.AttackTime : meleeProfile.AttackTime;
            NpcAiState attackAi = source.Ai with
            {
                Ai0 = attackState,
                Ai1 = attackTime,
                Ai2 = 0f
            };
            NpcSimulationState attackSimulation = source.Simulation with
            {
                DirectionX = direction,
                SpriteDirection = direction,
                LocalAi = source.Simulation.LocalAi with
                {
                    Ai2 = source.Ai.Ai0,
                    Ai3 = 0f
                }
            };
            var attackUpdate = SnapshotUpdate(
                in source,
                attackAi,
                attackSimulation,
                source.VelocityX * 0.8f,
                source.VelocityY);
            if (npcs.TryUpdate(source.Handle, in attackUpdate, out _))
                started++;
            else
                rejected++;
        }

        return new RuntimeTownNpcCombatTickSummary1458(
            visited,
            started,
            advanced,
            spawned,
            meleeHits,
            rejected,
            unsupportedTargets);
    }

'''
replace_between(
    combat,
    '    public RuntimeTownNpcCombatTickSummary1458 Tick()\n',
    '    internal int GetAttackChance',
    tick,
    'combat tick')

melee_methods = r'''    private bool TryAdvanceMeleeAttack(
        in NpcSnapshot source,
        NpcTypeId sourceType,
        in VanillaTownNpcMeleeAttackProfile1458 profile,
        ReadOnlySpan<NpcSnapshot> candidates,
        NpcAiState localAi,
        out int committedHits)
    {
        committedHits = 0;
        int direction = source.Simulation.SpriteDirection is -1 or 1
            ? source.Simulation.SpriteDirection
            : source.Simulation.DirectionX is -1 or 1 ? source.Simulation.DirectionX : 1;
        float nextAi1 = source.Ai.Ai1 - 1f;
        NpcAiState nextAi = source.Ai with { Ai1 = nextAi1 };
        NpcSimulationState nextSimulation = source.Simulation with
        {
            DirectionX = direction,
            SpriteDirection = direction,
            LocalAi = localAi
        };

        if (nextAi1 <= 0f)
        {
            int nextDelay = profile.RecoveryBase + random.Next(profile.RecoveryRandom);
            int localDelay = profile.RecoveryBase / 2 + random.Next(profile.RecoveryRandom);
            float returnState = localAi.Ai2 == 8f ? 8f : 0f;
            nextAi = nextAi with { Ai0 = returnState, Ai1 = nextDelay, Ai2 = 0f };
            nextSimulation = nextSimulation with
            {
                LocalAi = localAi with { Ai1 = localDelay, Ai3 = localDelay }
            };
        }

        var update = SnapshotUpdate(
            in source,
            nextAi,
            nextSimulation,
            source.VelocityX * 0.8f,
            source.VelocityY);
        if (!npcs.TryUpdate(source.Handle, in update, out NpcSnapshot committedSource))
            return false;

        if (meleeDamage is null ||
            !TryGetSwingRectangle(
                in committedSource,
                profile.AttackTime * 2,
                checked((int)nextAi1),
                direction,
                profile.HitboxWidth,
                profile.HitboxHeight,
                out VanillaTownNpcSwingRectangle1458 swing))
        {
            return true;
        }

        int baseDamage = profile.BaseDamage;
        float knockBack = profile.KnockBack;
        if (sourceType.Value == 441 &&
            townNpcs.TryGet(checked((short)source.Handle.Slot), out WorldTownNpc persisted) &&
            string.Equals(persisted.GivenName, "Andrew", StringComparison.Ordinal))
        {
            baseDamage *= 2;
            knockBack *= 2f;
        }
        int damage = GetAttackDamage(baseDamage);

        foreach (NpcSnapshot candidate in candidates)
        {
            if (!IsEligibleMeleeTarget(in committedSource, in candidate, in swing) || IsMeleeImmune(in candidate))
                continue;

            RuntimeTownNpcMeleeDamageResult1458 result = meleeDamage.TryStrike(
                committedSource.Handle,
                candidate.Handle,
                damage,
                knockBack,
                direction);
            if (result == RuntimeTownNpcMeleeDamageResult1458.Rejected)
                continue;

            int immunity = Math.Max(1, checked((int)nextAi1 + 2));
            SetMeleeImmunity(in candidate, immunity);
            committedHits++;
        }
        return true;
    }

    private bool IsEligibleMeleeTarget(
        in NpcSnapshot source,
        in NpcSnapshot candidate,
        in VanillaTownNpcSwingRectangle1458 swing)
    {
        if (!candidate.IsActive || candidate.Handle == source.Handle ||
            !NpcTypeId.TryCreate(candidate.Type, out NpcTypeId candidateType) ||
            VanillaTownNpcFacts1458.IsHousingEligible(candidateType) ||
            !VanillaNpcDefinitionCatalog.TryGet(candidateType, candidate.NetIdentity, out VanillaNpcDefinition definition) ||
            definition.Damage <= 0 || candidate.Simulation.DontTakeDamage ||
            !definition.TryResolveHitbox(candidate.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            !swing.Intersects(candidate.PositionX, candidate.PositionY, hitbox.Width, hitbox.Height))
        {
            return false;
        }

        if (candidate.Simulation.NoTileCollide)
            return true;
        return VanillaWorldLineOfSight.CanHitLine(
            tiles,
            source.PositionX,
            source.PositionY,
            candidate.PositionX,
            candidate.PositionY);
    }

    internal static bool TryGetSwingRectangle(
        in NpcSnapshot source,
        int swingMax,
        int swingCurrent,
        int aimDir,
        int itemWidth,
        int itemHeight,
        out VanillaTownNpcSwingRectangle1458 rectangle)
    {
        if (swingMax <= 0 || aimDir is not (-1 or 1) || itemWidth <= 0 || itemHeight <= 0 ||
            !VanillaTownNpcDefinitionCatalogBridge.TryGetHitbox(in source, out VanillaNpcHitboxSize hitbox))
        {
            rectangle = default;
            return false;
        }

        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float zeroX;
        float zeroY;
        if ((double)swingCurrent < swingMax * 0.333)
        {
            float offset = itemWidth > 32 ? 14f : 10f;
            if (itemWidth >= 52) offset = 24f;
            if (itemWidth >= 64) offset = 28f;
            if (itemWidth >= 92) offset = 38f;
            zeroX = centerX + (itemWidth * 0.5f - offset) * aimDir;
            zeroY = source.PositionY + 24f;
        }
        else if ((double)swingCurrent < swingMax * 0.666)
        {
            float offset = itemWidth > 32 ? 18f : 10f;
            if (itemWidth >= 52) offset = 24f;
            if (itemWidth >= 64) offset = 28f;
            if (itemWidth >= 92) offset = 38f;
            zeroX = centerX + (itemWidth * 0.5f - offset) * aimDir;
            float yOffset = itemHeight > 32 ? 8f : 10f;
            if (itemHeight > 52) yOffset = 12f;
            if (itemHeight > 64) yOffset = 14f;
            zeroY = source.PositionY + yOffset;
        }
        else
        {
            float offset = itemWidth > 32 ? 14f : 6f;
            if (itemWidth >= 48) offset = 18f;
            if (itemWidth >= 52) offset = 24f;
            if (itemWidth >= 64) offset = 28f;
            if (itemWidth >= 92) offset = 38f;
            zeroX = centerX - (itemWidth * 0.5f - offset) * aimDir;
            float yOffset = 10f;
            if (itemHeight > 52) yOffset = 12f;
            if (itemHeight > 64) yOffset = 14f;
            zeroY = source.PositionY + yOffset;
        }

        int x = (int)zeroX;
        int y = (int)zeroY;
        int width = itemWidth;
        int height = itemHeight;
        if (aimDir == -1)
            x -= itemWidth;
        y -= itemHeight;

        if ((double)swingCurrent < swingMax * 0.333)
        {
            if (aimDir == -1)
                x -= (int)(width * 1.4 - width);
            width = (int)(width * 1.4);
            y += (int)(height * 0.5);
            height = (int)(height * 1.1);
        }
        else if (!((double)swingCurrent < swingMax * 0.666))
        {
            if (aimDir == 1)
                x -= (int)(width * 1.2);
            width *= 2;
            y -= (int)(height * 1.4 - height);
            height = (int)(height * 1.4);
        }

        rectangle = new VanillaTownNpcSwingRectangle1458(x, y, width, height);
        return true;
    }

    private void AdvanceMeleeImmunity()
    {
        for (int slot = 0; slot < meleeImmuneTicks.Length; slot++)
        {
            if (meleeImmuneTicks[slot] > 0)
                meleeImmuneTicks[slot]--;
        }
    }

    private bool IsMeleeImmune(in NpcSnapshot target)
    {
        int slot = target.Handle.Slot;
        ulong generation = target.Handle.Generation.Value;
        if (meleeImmuneGenerations[slot] != generation)
        {
            meleeImmuneGenerations[slot] = generation;
            meleeImmuneTicks[slot] = 0;
            return false;
        }
        return meleeImmuneTicks[slot] > 0;
    }

    private void SetMeleeImmunity(in NpcSnapshot target, int ticks)
    {
        int slot = target.Handle.Slot;
        meleeImmuneGenerations[slot] = target.Handle.Generation.Value;
        meleeImmuneTicks[slot] = ticks;
    }

'''
replace_once(
    combat,
    '    private bool TryBuildProjectileIntent(\n',
    melee_methods + '    private bool TryBuildProjectileIntent(\n',
    'melee methods')

replace_once(
    combat,
    '    private bool TrySelectTarget(\n        in NpcSnapshot source,\n        in VanillaTownNpcProjectileAttackProfile1458 profile,\n        ReadOnlySpan<NpcSnapshot> candidates,',
    '    private bool TrySelectTarget(\n        in NpcSnapshot source,\n        int dangerDetectRange,\n        ReadOnlySpan<NpcSnapshot> candidates,',
    'target signature')
replace_once(
    combat,
    '            if (!float.IsFinite(distance) || distance >= profile.DangerDetectRange)',
    '            if (!float.IsFinite(distance) || distance >= dangerDetectRange)',
    'target range')
replace_once(
    combat,
    '        public static bool TryGetCenter(\n            in NpcSnapshot snapshot,\n            out float centerX,\n            out float centerY)\n        {\n            if (!NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type) ||\n                !VanillaNpcDefinitionCatalog.TryGet(type, snapshot.NetIdentity, out VanillaNpcDefinition definition) ||\n                !definition.TryResolveHitbox(snapshot.Simulation.Scale, out VanillaNpcHitboxSize hitbox))',
    '        public static bool TryGetHitbox(in NpcSnapshot snapshot, out VanillaNpcHitboxSize hitbox)\n        {\n            if (!NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type) ||\n                !VanillaNpcDefinitionCatalog.TryGet(type, snapshot.NetIdentity, out VanillaNpcDefinition definition))\n            {\n                hitbox = default;\n                return false;\n            }\n            return definition.TryResolveHitbox(snapshot.Simulation.Scale, out hitbox);\n        }\n\n        public static bool TryGetCenter(\n            in NpcSnapshot snapshot,\n            out float centerX,\n            out float centerY)\n        {\n            if (!TryGetHitbox(in snapshot, out VanillaNpcHitboxSize hitbox))',
    'hitbox bridge')

pipeline = 'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs'
replace_once(
    pipeline,
    'internal sealed class RuntimeNpcNetworkCombatPipeline\n{',
    'internal sealed class RuntimeNpcNetworkCombatPipeline : IRuntimeTownNpcMeleeDamageSink1458\n{',
    'pipeline melee interface')

pipeline_method = r'''
    public RuntimeTownNpcMeleeDamageResult1458 TryStrike(
        NpcHandle attacker,
        NpcHandle target,
        int baseDamage,
        float knockBack,
        int hitDirection)
    {
        if (!attacker.IsAssigned || !target.IsAssigned || baseDamage < 0 ||
            !float.IsFinite(knockBack) || knockBack < 0f || hitDirection is not (-1 or 1) ||
            !npcs.TryGet(attacker, out NpcSnapshot liveAttacker) || !liveAttacker.IsActive ||
            !npcs.TryGet(target, out NpcSnapshot liveTarget) || !liveTarget.IsActive)
        {
            return RuntimeTownNpcMeleeDamageResult1458.Rejected;
        }

        var request = new NpcDamageRequest(
            liveTarget.Handle,
            DamageSource.FromNpcContact(liveAttacker.Handle),
            baseDamage,
            KnockBack: knockBack,
            HitDirection: hitDirection);
        if (!damage.TryApply(in request, out NpcDamageResult result))
            return RuntimeTownNpcMeleeDamageResult1458.Rejected;
        if (!result.Lethal)
            return RuntimeTownNpcMeleeDamageResult1458.Committed;

        if (!npcs.TryGet(liveTarget.Handle, out NpcSnapshot dead))
            throw new InvalidOperationException("A lethal Town NPC melee commit disappeared before death finalization.");

        bool eaterBoss =
            VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity) &&
            VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(npcs, in dead, npcFamilyBuffer);
        if (!TryExecuteImportedLoot(in dead, eaterBoss))
            throw new InvalidOperationException("Imported NPC loot could not be finalized after Town NPC melee.");

        if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
            ApplyKingSlimeDeathEffects(in dead);
        else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
            ApplyEvilBossDeathEffects();

        if (!npcs.TryDespawn(dead.Handle))
            throw new InvalidOperationException("A Town NPC melee kill could not despawn the exact NPC generation.");
        interactions.Forget(dead.Handle);
        npcReplication?.TryPublishDeath(in dead);
        return RuntimeTownNpcMeleeDamageResult1458.Killed;
    }

'''
replace_once(
    pipeline,
    '    private bool TryExecuteImportedLoot(in NpcSnapshot npc, bool eaterBoss)\n',
    pipeline_method + '    private bool TryExecuteImportedLoot(in NpcSnapshot npc, bool eaterBoss)\n',
    'pipeline melee method')

server = 'src/TerraRuntime/ServerRuntimeState.cs'
replace_once(
    server,
    '            expertMode,\n            masterMode);\n\n        if (npcAiStepper is null)',
    '            expertMode,\n            masterMode);\n        _townCombat?.SetMeleeDamageSink(_npcCombat);\n\n        if (npcAiStepper is null)',
    'production melee sink wiring')

# Source contract: pin AttackType=3 profiles, state-15 damage/swing/immunity and the source-dead pet setup.
checker = 'tools/ci/check_town_combat_source.py'
replace_once(
    checker,
    '    state12 = slice_between(ai7, "else if (ai[0] == 12f)", "else if (ai[0] == 13f)", "AI_007 state 12")',
    '    state12 = slice_between(ai7, "else if (ai[0] == 12f)", "else if (ai[0] == 13f)", "AI_007 state 12")\n    state15 = slice_between(ai7, "else if (ai[0] == 15f)", "else if (ai[0] == 16f)", "AI_007 state 15")',
    'state15 source slice')
replace_once(
    checker,
    '    require(npcid, r"AttackType\\s*=\\s*Factory\\.CreateIntSet\\([^;]*17,\\s*0[^;]*19,\\s*1[^;]*22,\\s*1[^;]*18,\\s*0", "AI_007 attack types")',
    '    require(npcid, r"AttackType\\s*=\\s*Factory\\.CreateIntSet\\([^;]*17,\\s*0[^;]*19,\\s*1[^;]*22,\\s*1[^;]*18,\\s*0", "AI_007 attack types")\n    require(npcid, r"DangerDetectRange\\s*=\\s*Factory\\.CreateIntSet\\([^;]*207,\\s*60[^;]*441,\\s*50[^;]*353,\\s*60", "AI_007 melee danger ranges")\n    require(npcid, r"AttackTime\\s*=\\s*Factory\\.CreateIntSet\\([^;]*207,\\s*15[^;]*441,\\s*15[^;]*353,\\s*12", "AI_007 melee attack times")\n    require(npcid, r"AttackAverageChance\\s*=\\s*Factory\\.CreateIntSet\\([^;]*207,\\s*1[^;]*441,\\s*1[^;]*353,\\s*1", "AI_007 melee attack chances")\n    require(npcid, r"AttackType\\s*=\\s*Factory\\.CreateIntSet\\([^;]*207,\\s*3[^;]*441,\\s*3[^;]*353,\\s*3", "AI_007 melee attack types")\n    require(npcid, r"IsTownPet\\s*=\\s*Factory\\.CreateBoolSet\\(637,\\s*638,\\s*656,\\s*670,\\s*678,\\s*679,\\s*680,\\s*681,\\s*682,\\s*683,\\s*684\\)", "Town pet identity set")\n    require(npcid, r"AttackType\\s*=\\s*Factory\\.CreateIntSet\\([^;]*638,\\s*-1[^;]*637,\\s*-1[^;]*656,\\s*-1[^;]*670,\\s*-1", "Town pets do not enter melee attack naturally")',
    'melee set contracts')
replace_once(
    checker,
    '    require(state12, r"localAI\\[1\\] = \\(localAI\\[3\\] = num55 / 2 \\+ Main\\.rand\\.Next\\(maxValue2\\)\\)", "state 12 recovery ordering")',
    '    require(state12, r"localAI\\[1\\] = \\(localAI\\[3\\] = num55 / 2 \\+ Main\\.rand\\.Next\\(maxValue2\\)\\)", "state 12 recovery ordering")\n\n    require(ai7, r"AttackType\\[type\\] == 3.*?ai\\[0\\] = 15f.*?ai\\[1\\] = num132", "attack type three enters state 15")\n    require(state15, r"type == 207.*?num81 = 11;.*?num83 = \\(num84 = 32\\);.*?num80 = 12;.*?maxValue4 = 6;.*?num82 = 4\\.25f;", "Dye Trader melee profile")\n    require(state15, r"type == 441.*?num81 = 9;.*?num83 = \\(num84 = 28\\);.*?num80 = 9;.*?maxValue4 = 3;.*?num82 = 3\\.5f;.*?GivenName == \\\"Andrew\\\".*?num81 \\*= 2;.*?num82 \\*= 2f;", "Tax Collector Andrew melee profile")\n    require(state15, r"type == 353.*?num81 = 10;.*?num83 = \\(num84 = 32\\);.*?num80 = 15;.*?maxValue4 = 8;.*?num82 = 5f;", "Stylist melee profile")\n    require(state15, r"NPCID\\.Sets\\.IsTownPet\\[type\\].*?num81 = 10;.*?num83 = \\(num84 = 32\\);.*?num80 = 15;.*?maxValue4 = 8;.*?num82 = 3f;", "source-dead town pet state 15 body")\n    require(state15, r"GetSwingStats\\(NPCID\\.Sets\\.AttackTime\\[type\\] \\* 2, \\(int\\)ai\\[1\\], spriteDirection, num83, num84\\)", "state 15 swing geometry")\n    require(state15, r"TweakSwingStats\\(NPCID\\.Sets\\.AttackTime\\[type\\] \\* 2, \\(int\\)ai\\[1\\], spriteDirection, ref itemRectangle\\)", "state 15 swing rectangle tweak")\n    require(state15, r"immune\\[myPlayer\\] == 0.*?!nPC2\\.dontTakeDamage.*?!nPC2\\.friendly.*?nPC2\\.damage > 0.*?itemRectangle\\.Intersects\\(nPC2\\.Hitbox\\)", "state 15 hostile immunity gate")\n    require(state15, r"StrikeNPCNoInteraction\\(num81, num82, spriteDirection\\).*?immune\\[myPlayer\\] = \\(int\\)ai\\[1\\] \\+ 2", "state 15 hit and immunity ordering")\n    require(npc, r"public Tuple<Vector2, float> GetSwingStats\\(int swingMax, int swingCurrent, int aimDir, int itemWidth, int itemHeight\\).*?swingMax \\* 0\\.333.*?swingMax \\* 0\\.666", "GetSwingStats three phases")\n    require(npc, r"public void TweakSwingStats\\(int swingMax, int swingCurrent, int aimDir, ref Rectangle itemRectangle\\).*?itemRectangle\\.Width = \\(int\\)\\(\\(double\\)itemRectangle\\.Width \\* 1\\.4\\).*?itemRectangle\\.Width \\*= 2", "TweakSwingStats widening")',
    'melee state contracts')

# Documentation ownership expansion.
for path, marker, text in [
    ('docs/en/town-npc-combat.md', '# Town NPC projectile combat ownership', '''# Town NPC projectile and melee combat ownership'''),
    ('docs/ru/town-npc-combat.md', '# Владение боевым поведением Town NPC', '''# Владение боевым поведением Town NPC''')
]:
    content = read(path)
    if path.endswith('/en/town-npc-combat.md') and marker in content:
        content = content.replace(marker, text, 1)
    append_en = '''\n\n## AI_007 melee slice\n\nThe same runtime owner now admits the source `AttackType == 3` branch for Dye Trader (207), Tax Collector (441), and Stylist (353). It preserves the pinned danger ranges, attack times/chances, state-15 entry, three-phase `GetSwingStats`/`TweakSwingStats` rectangle geometry, source-shaped per-target server immunity, recovery cadence, progression/Combat Book/difficulty damage scaling, and the Tax Collector `GivenName == "Andrew"` double damage/knockback easter egg. Hits cross a generation-safe NPC-contact damage sink; lethal hits continue through the existing imported-loot/progression/despawn/death-replication pipeline rather than leaving `Life == 0` occupants in the NPC table.\n\nTerrariaServer 1.4.5.8 still contains an `IsTownPet[type]` case inside state 15, but every current town-pet identity in the pinned `NPCID.Sets` has `AttackType = -1` and `AttackTime = -1`. TerraRuntime keeps that fact explicit and does not manufacture a natural pet melee entry.\n'''
    append_ru = '''\n\n## Ближний бой AI_007\n\nТот же runtime-владелец теперь поддерживает исходную ветку `AttackType == 3` для Красильщика (207), Сборщика налогов (441) и Стилиста (353). Сохраняются закреплённые дальности обнаружения, времена/шансы атаки, переход в state 15, трёхфазная геометрия прямоугольника `GetSwingStats`/`TweakSwingStats`, серверный immunity на конкретное поколение цели, recovery cadence, масштабирование урона progression/Combat Book/difficulty и пасхалка Сборщика налогов `GivenName == "Andrew"` с двойным уроном/отбрасыванием. Удары проходят через generation-safe NPC-contact damage sink; смертельный удар продолжает существующий pipeline loot/progression/despawn/death replication, а не оставляет NPC с `Life == 0` висеть в таблице.\n\nВ TerrariaServer 1.4.5.8 внутри state 15 всё ещё есть ветка `IsTownPet[type]`, но у всех текущих town-pet идентичностей в закреплённом `NPCID.Sets` стоят `AttackType = -1` и `AttackTime = -1`. TerraRuntime это не додумывает и не создаёт для питомцев несуществующий естественный вход в melee-атаку.\n'''
    addition = append_en if '/en/' in path else append_ru
    if addition.strip() not in content:
        content += addition
    write(path, content)

roadmap = 'docs/roadmap/npc-ai-parity.md'
text = read(roadmap)
old_note = '  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, and an authoritative projectile-combat slice for Merchant/Nurse/Arms Dealer/Guide are implemented; social/emote/melee/special town branches remain open;'
new_note = '  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, projectile combat for Merchant/Nurse/Arms Dealer/Guide, and melee state 15 for Dye Trader/Tax Collector/Stylist are authoritative; social/emote and remaining special town branches remain open;'
if old_note in text:
    text = text.replace(old_note, new_note, 1)
elif new_note not in text:
    anchor = '- [ ] town AI, housing and schedules;'
    if text.count(anchor) != 1:
        raise SystemExit('roadmap anchor missing or ambiguous')
    text = text.replace(anchor, anchor + '\n' + new_note, 1)
write(roadmap, text)

print('N4 Town NPC melee block applied')
