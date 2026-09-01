using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum VanillaTownNpcProjectileAttackKind1458 : byte
{
    Lobbed = 0,
    Straight = 1
}

internal readonly record struct VanillaTownNpcProjectileAttackProfile1458(
    NpcTypeId NpcType,
    VanillaTownNpcProjectileAttackKind1458 Kind,
    float AttackState,
    int DangerDetectRange,
    int AttackTime,
    int AttackAverageChance,
    ProjectileTypeId NormalProjectile,
    ProjectileTypeId HardModeProjectile,
    float ProjectileSpeed,
    int NormalBaseDamage,
    int HardModeBaseDamage,
    float KnockBack,
    float AimOffsetY,
    float Spread,
    int NormalRecoveryBase,
    int NormalRecoveryRandom,
    int HardModeRecoveryBase,
    int HardModeRecoveryRandom)
{
    public ProjectileTypeId Projectile(bool hardMode) => hardMode ? HardModeProjectile : NormalProjectile;

    public int BaseDamage(bool hardMode) => hardMode ? HardModeBaseDamage : NormalBaseDamage;

    public int RecoveryBase(bool hardMode) => hardMode ? HardModeRecoveryBase : NormalRecoveryBase;

    public int RecoveryRandom(bool hardMode) => hardMode ? HardModeRecoveryRandom : NormalRecoveryRandom;
}

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 AI_007 projectile-combat profiles admitted by TerraRuntime. Only town
/// attacks whose projectile identities already have authoritative runtime lifecycle/behavior are admitted here.
/// Unsupported town attackers remain fail-closed rather than emitting visually plausible but unsimulated shots.
/// </summary>
internal static class VanillaTownNpcProjectileAttackCatalog1458
{
    private static readonly VanillaTownNpcProjectileAttackProfile1458 Merchant = new(
        VanillaNpcIds.Merchant,
        VanillaTownNpcProjectileAttackKind1458.Lobbed,
        AttackState: 10f,
        DangerDetectRange: 320,
        AttackTime: 34,
        AttackAverageChance: 30,
        NormalProjectile: VanillaProjectileIds.ThrowingKnife,
        HardModeProjectile: VanillaProjectileIds.ThrowingKnife,
        ProjectileSpeed: 9f,
        NormalBaseDamage: 12,
        HardModeBaseDamage: 12,
        KnockBack: 1.5f,
        AimOffsetY: 16f,
        Spread: 0f,
        NormalRecoveryBase: 60,
        NormalRecoveryRandom: 60,
        HardModeRecoveryBase: 60,
        HardModeRecoveryRandom: 60);

    private static readonly VanillaTownNpcProjectileAttackProfile1458 Nurse = new(
        VanillaNpcIds.Nurse,
        VanillaTownNpcProjectileAttackKind1458.Lobbed,
        AttackState: 10f,
        DangerDetectRange: 300,
        AttackTime: 34,
        AttackAverageChance: 60,
        NormalProjectile: VanillaProjectileIds.NurseSyringeHurt,
        HardModeProjectile: VanillaProjectileIds.NurseSyringeHurt,
        ProjectileSpeed: 8f,
        NormalBaseDamage: 8,
        HardModeBaseDamage: 8,
        KnockBack: 2f,
        AimOffsetY: 10f,
        Spread: 0f,
        NormalRecoveryBase: 15,
        NormalRecoveryRandom: 10,
        HardModeRecoveryBase: 15,
        HardModeRecoveryRandom: 10);

    private static readonly VanillaTownNpcProjectileAttackProfile1458 ArmsDealer = new(
        VanillaNpcIds.ArmsDealer,
        VanillaTownNpcProjectileAttackKind1458.Straight,
        AttackState: 12f,
        DangerDetectRange: 900,
        AttackTime: 40,
        AttackAverageChance: 30,
        NormalProjectile: VanillaProjectileIds.Bullet,
        HardModeProjectile: VanillaProjectileIds.Bullet,
        ProjectileSpeed: 13f,
        NormalBaseDamage: 24,
        HardModeBaseDamage: 15,
        KnockBack: 3f,
        AimOffsetY: 0f,
        Spread: 0.5f,
        NormalRecoveryBase: 14,
        NormalRecoveryRandom: 4,
        HardModeRecoveryBase: 14,
        HardModeRecoveryRandom: 4);

    private static readonly VanillaTownNpcProjectileAttackProfile1458 Guide = new(
        VanillaNpcIds.Guide,
        VanillaTownNpcProjectileAttackKind1458.Straight,
        AttackState: 12f,
        DangerDetectRange: 700,
        AttackTime: 30,
        AttackAverageChance: 30,
        NormalProjectile: VanillaProjectileIds.WoodenArrowFriendly,
        HardModeProjectile: VanillaProjectileIds.FireArrow,
        ProjectileSpeed: 10f,
        NormalBaseDamage: 12,
        HardModeBaseDamage: 18,
        KnockBack: 2.75f,
        AimOffsetY: 4f,
        Spread: 0.7f,
        NormalRecoveryBase: 30,
        NormalRecoveryRandom: 20,
        HardModeRecoveryBase: 15,
        HardModeRecoveryRandom: 10);

    public static bool TryGet(NpcTypeId type, out VanillaTownNpcProjectileAttackProfile1458 profile)
    {
        if (type == VanillaNpcIds.Merchant)
        {
            profile = Merchant;
            return true;
        }
        if (type == VanillaNpcIds.Nurse)
        {
            profile = Nurse;
            return true;
        }
        if (type == VanillaNpcIds.ArmsDealer)
        {
            profile = ArmsDealer;
            return true;
        }
        if (type == VanillaNpcIds.Guide)
        {
            profile = Guide;
            return true;
        }

        profile = default;
        return false;
    }

    public static bool ShouldFire(NpcTypeId type, bool hardMode, int elapsedTick)
    {
        if (elapsedTick <= 0)
            return false;
        if (type == VanillaNpcIds.ArmsDealer)
            return hardMode ? elapsedTick is 1 or 10 or 20 or 30 : elapsedTick == 1;
        if (type == VanillaNpcIds.Merchant)
            return elapsedTick == 10;
        if (type == VanillaNpcIds.Nurse || type == VanillaNpcIds.Guide)
            return elapsedTick == 1;
        return false;
    }
}

internal readonly record struct VanillaTownNpcMeleeAttackProfile1458(
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

internal readonly record struct RuntimeTownNpcCombatWorldFacts1458(
    VanillaWorldProgressionState BaselineProgression,
    bool CombatBookWasUsed,
    bool CombatBookVolumeTwoWasUsed)
{
    public static RuntimeTownNpcCombatWorldFacts1458 FromMetadata(WorldFileRuntimeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RuntimeTownNpcCombatWorldFacts1458(
            metadata.Progression,
            metadata.CombatBookWasUsed,
            metadata.CombatBookVolumeTwoWasUsed);
    }
}

internal interface IRuntimeTownNpcCombatRandom1458
{
    int Next(int exclusiveMax);
    float NextFloat(float inclusiveMin, float exclusiveMax);
}

internal sealed class SharedRuntimeTownNpcCombatRandom1458 : IRuntimeTownNpcCombatRandom1458
{
    public static SharedRuntimeTownNpcCombatRandom1458 Instance { get; } = new();

    private SharedRuntimeTownNpcCombatRandom1458()
    {
    }

    public int Next(int exclusiveMax) => Random.Shared.Next(exclusiveMax);

    public float NextFloat(float inclusiveMin, float exclusiveMax) =>
        Random.Shared.NextSingle() * (exclusiveMax - inclusiveMin) + inclusiveMin;
}

internal readonly record struct RuntimeTownNpcCombatTickSummary1458(
    int TownNpcsVisited,
    int AttacksStarted,
    int AttackStatesAdvanced,
    int ProjectilesSpawned,
    int MeleeHits,
    int RejectedCommits,
    int UnsupportedTargets);

/// <summary>
/// Authoritative AI_007 projectile-combat slice for the admitted TerrariaServer 1.4.5.8 town attackers. Target
/// discovery follows the source's nearest-left/nearest-right danger scan over active NPC slots, requires an admitted
/// hostile definition and source-shaped line of sight for colliding targets, and preserves localAI[1]/localAI[2]/
/// localAI[3] attack cooldown/state. Projectile side effects are applied only after the generation-safe source NPC
/// update commits, matching the runtime's existing speculative-side-effect ordering.
/// </summary>
internal sealed class RuntimeTownNpcCombat1458
{
    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeProjectileStore projectiles;
    private readonly WorldTileStore tiles;
    private readonly RuntimeTownNpcCombatWorldFacts1458 world;
    private readonly RuntimeWorldProgressionMutations progression;
    private readonly bool expertMode;
    private readonly bool masterMode;
    private readonly IRuntimeTownNpcCombatRandom1458 random;
    private readonly NpcSnapshot[] peers;
    private readonly ulong[] meleeImmuneGenerations;
    private readonly int[] meleeImmuneTicks;
    private IRuntimeTownNpcMeleeDamageSink1458? meleeDamage;

    public RuntimeTownNpcCombat1458(
        RuntimeTownNpcStateStore townNpcs,
        RuntimeNpcStore npcs,
        RuntimeProjectileStore projectiles,
        WorldTileStore tiles,
        in RuntimeTownNpcCombatWorldFacts1458 world,
        RuntimeWorldProgressionMutations progression,
        bool expertMode,
        bool masterMode,
        IRuntimeTownNpcCombatRandom1458? random = null)
    {
        this.townNpcs = townNpcs ?? throw new ArgumentNullException(nameof(townNpcs));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.world = world;
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
        this.expertMode = expertMode;
        this.masterMode = masterMode;
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));
        this.random = random ?? SharedRuntimeTownNpcCombatRandom1458.Instance;
        peers = new NpcSnapshot[npcs.Capacity];
        meleeImmuneGenerations = new ulong[npcs.Capacity];
        meleeImmuneTicks = new int[npcs.Capacity];
    }

    public void SetMeleeDamageSink(IRuntimeTownNpcMeleeDamageSink1458 sink) =>
        meleeDamage = sink ?? throw new ArgumentNullException(nameof(sink));

    public RuntimeTownNpcCombatTickSummary1458 Tick()
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

    internal int GetAttackChance(int averageChance)
    {
        float scale = 2f;
        if (world.CombatBookWasUsed)
            scale *= 0.8f;
        if (world.CombatBookVolumeTwoWasUsed)
            scale *= 0.8f;

        ReadOnlySpan<VanillaWorldProgressionId> milestones =
        [
            VanillaWorldProgressionId.KingSlime,
            VanillaWorldProgressionId.EyeOfCthulhu,
            VanillaWorldProgressionId.Deerclops,
            VanillaWorldProgressionId.EvilBoss,
            VanillaWorldProgressionId.Skeletron,
            VanillaWorldProgressionId.QueenBee,
            VanillaWorldProgressionId.Hardmode,
            VanillaWorldProgressionId.QueenSlime,
            VanillaWorldProgressionId.Destroyer,
            VanillaWorldProgressionId.Twins,
            VanillaWorldProgressionId.SkeletronPrime,
            VanillaWorldProgressionId.Plantera,
            VanillaWorldProgressionId.EmpressOfLight,
            VanillaWorldProgressionId.DukeFishron,
            VanillaWorldProgressionId.Golem,
            VanillaWorldProgressionId.LunaticCultist
        ];
        foreach (VanillaWorldProgressionId milestone in milestones)
        {
            if (IsComplete(milestone))
                scale *= 0.985f;
        }

        return Math.Max(1, (int)(averageChance * scale));
    }

    internal int GetAttackDamage(int baseDamage)
    {
        float strength = 1f;
        if (world.CombatBookWasUsed)
            strength += 0.25f;
        if (world.CombatBookVolumeTwoWasUsed)
            strength += 0.25f;
        if (IsComplete(VanillaWorldProgressionId.KingSlime)) strength += 0.05f;
        if (IsComplete(VanillaWorldProgressionId.EyeOfCthulhu)) strength += 0.05f;
        if (IsComplete(VanillaWorldProgressionId.Deerclops)) strength += 0.1f;
        if (IsComplete(VanillaWorldProgressionId.EvilBoss)) strength += 0.1f;
        if (IsComplete(VanillaWorldProgressionId.Skeletron)) strength += 0.1f;
        if (IsComplete(VanillaWorldProgressionId.QueenBee)) strength += 0.1f;
        if (IsComplete(VanillaWorldProgressionId.Hardmode)) strength += 0.4f;
        if (IsComplete(VanillaWorldProgressionId.QueenSlime)) strength += 0.15f;
        if (IsComplete(VanillaWorldProgressionId.Destroyer)) strength += 0.15f;
        if (IsComplete(VanillaWorldProgressionId.Twins)) strength += 0.15f;
        if (IsComplete(VanillaWorldProgressionId.SkeletronPrime)) strength += 0.15f;
        if (IsComplete(VanillaWorldProgressionId.Plantera)) strength += 0.15f;
        if (IsComplete(VanillaWorldProgressionId.EmpressOfLight)) strength += 0.15f;
        if (IsComplete(VanillaWorldProgressionId.DukeFishron)) strength += 0.15f;
        if (IsComplete(VanillaWorldProgressionId.Golem)) strength += 0.15f;
        if (IsComplete(VanillaWorldProgressionId.LunaticCultist)) strength += 0.15f;

        float difficultyMultiplier = masterMode ? 1.75f : expertMode ? 1.5f : 1f;
        return (int)(baseDamage * strength * difficultyMultiplier);
    }

    private bool TryAdvanceAttack(
        in NpcSnapshot source,
        NpcTypeId sourceType,
        in VanillaTownNpcProjectileAttackProfile1458 profile,
        in NpcSnapshot target,
        bool hasTarget,
        int selectedDirection,
        NpcAiState localAi,
        out bool projectileSpawned)
    {
        projectileSpawned = false;
        bool hardMode = IsComplete(VanillaWorldProgressionId.Hardmode);
        int direction = source.Simulation.SpriteDirection is -1 or 1
            ? source.Simulation.SpriteDirection
            : selectedDirection is -1 or 1 ? selectedDirection : 1;
        int elapsed = checked((int)localAi.Ai3 + 1);
        float nextAi1 = source.Ai.Ai1 - 1f;
        localAi = localAi with { Ai3 = elapsed };
        NpcAiState nextAi = source.Ai with { Ai1 = nextAi1 };
        NpcSimulationState nextSimulation = source.Simulation with
        {
            DirectionX = direction,
            SpriteDirection = direction,
            LocalAi = localAi
        };
        float nextVelocityX = source.VelocityX * 0.8f;

        if (nextAi1 <= 0f)
        {
            int recoveryBase = profile.RecoveryBase(hardMode);
            int recoveryRandom = profile.RecoveryRandom(hardMode);
            int nextDelay = recoveryBase + (recoveryRandom > 0 ? random.Next(recoveryRandom) : 0);
            int localDelay = recoveryBase / 2 + (recoveryRandom > 0 ? random.Next(recoveryRandom) : 0);
            float returnState = localAi.Ai2 == 8f && hasTarget ? 8f : 0f;
            nextAi = nextAi with { Ai0 = returnState, Ai1 = nextDelay, Ai2 = 0f };
            nextSimulation = nextSimulation with
            {
                LocalAi = localAi with { Ai1 = localDelay, Ai3 = localDelay }
            };
        }

        var update = SnapshotUpdate(in source, nextAi, nextSimulation, nextVelocityX, source.VelocityY);
        if (!npcs.TryUpdate(source.Handle, in update, out NpcSnapshot committed))
            return false;

        if (!VanillaTownNpcProjectileAttackCatalog1458.ShouldFire(sourceType, hardMode, elapsed))
            return true;

        if (!TryBuildProjectileIntent(
                in committed,
                sourceType,
                in profile,
                hardMode,
                hasTarget ? target : default,
                hasTarget,
                direction,
                out NpcAiProjectileIntent intent))
        {
            return true;
        }

        projectileSpawned = RuntimeNpcProjectileIntentApplier.TryApply(projectiles, in intent, out _);
        return true;
    }

    private bool TryAdvanceMeleeAttack(
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

    private bool TryBuildProjectileIntent(
        in NpcSnapshot source,
        NpcTypeId sourceType,
        in VanillaTownNpcProjectileAttackProfile1458 profile,
        bool hardMode,
        in NpcSnapshot target,
        bool hasTarget,
        int direction,
        out NpcAiProjectileIntent intent)
    {
        if (!VanillaTownNpcFacts1458.TryGetDefinition(sourceType, out VanillaNpcDefinition sourceDefinition) ||
            !sourceDefinition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize sourceHitbox))
        {
            intent = default;
            return false;
        }

        float centerX = source.PositionX + sourceHitbox.Width * 0.5f;
        float centerY = source.PositionY + sourceHitbox.Height * 0.5f;
        float aimX;
        float aimY;
        if (hasTarget &&
            NpcTypeId.TryCreate(target.Type, out NpcTypeId targetType) &&
            VanillaNpcDefinitionCatalog.TryGet(targetType, target.NetIdentity, out VanillaNpcDefinition targetDefinition) &&
            targetDefinition.TryResolveHitbox(target.Simulation.Scale, out VanillaNpcHitboxSize targetHitbox))
        {
            aimX = target.PositionX + targetHitbox.Width * 0.5f;
            aimY = target.PositionY + targetHitbox.Height * 0.5f;
            if (profile.Kind == VanillaTownNpcProjectileAttackKind1458.Lobbed)
            {
                float dx = aimX - centerX;
                float dy = aimY - centerY;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                float fraction = Math.Clamp(distance / profile.DangerDetectRange, 0f, 1f);
                aimY -= profile.AimOffsetY * fraction;
            }
            else
            {
                aimY -= profile.AimOffsetY;
            }
        }
        else
        {
            aimX = centerX + direction;
            aimY = centerY + (profile.Kind == VanillaTownNpcProjectileAttackKind1458.Lobbed ? -1f : 0f);
        }

        float vectorX = aimX - centerX;
        float vectorY = aimY - centerY;
        float length = MathF.Sqrt(vectorX * vectorX + vectorY * vectorY);
        if (float.IsFinite(length) && length > float.Epsilon)
        {
            vectorX /= length;
            vectorY /= length;
        }
        else
        {
            vectorX = direction;
            vectorY = profile.Kind == VanillaTownNpcProjectileAttackKind1458.Lobbed ? -1f : 0f;
        }

        if (!float.IsFinite(vectorX) || !float.IsFinite(vectorY) || Math.Sign(vectorX) != direction)
        {
            vectorX = direction;
            vectorY = profile.Kind == VanillaTownNpcProjectileAttackKind1458.Lobbed ? -1f : 0f;
        }

        vectorX *= profile.ProjectileSpeed;
        vectorY *= profile.ProjectileSpeed;
        if (profile.Spread > 0f)
        {
            vectorX += random.NextFloat(-profile.Spread, profile.Spread);
            vectorY += random.NextFloat(-profile.Spread, profile.Spread);
        }

        intent = new NpcAiProjectileIntent(
            profile.Projectile(hardMode),
            centerX + direction * 16f,
            centerY - 2f,
            vectorX,
            vectorY,
            GetAttackDamage(profile.BaseDamage(hardMode)),
            profile.KnockBack);
        return true;
    }

    private static bool HasStraightAttackAngle(in NpcSnapshot source, in NpcSnapshot target)
    {
        if (!VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in source, out float sourceX, out float sourceY) ||
            !VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in target, out float targetX, out float targetY))
        {
            return false;
        }

        float dx = targetX - sourceX;
        float dy = targetY - sourceY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (!float.IsFinite(length) || length <= float.Epsilon)
            return false;
        float normalizedY = dy / length;
        return normalizedY is >= -0.5f and <= 0.5f;
    }

    private bool TrySelectTarget(
        in NpcSnapshot source,
        int dangerDetectRange,
        ReadOnlySpan<NpcSnapshot> candidates,
        out NpcSnapshot target,
        out int direction)
    {
        target = default;
        direction = source.Simulation.DirectionX is -1 or 1 ? source.Simulation.DirectionX : 1;
        if (!VanillaTownNpcFacts1458.TryGetDefinition(source.TypeIdentity, out VanillaNpcDefinition sourceDefinition) ||
            !sourceDefinition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize sourceHitbox))
        {
            return false;
        }

        float sourceCenterX = source.PositionX + sourceHitbox.Width * 0.5f;
        float sourceCenterY = source.PositionY + sourceHitbox.Height * 0.5f;
        float leftDelta = float.NegativeInfinity;
        float rightDelta = float.PositiveInfinity;
        NpcSnapshot left = default;
        NpcSnapshot right = default;
        bool hasLeft = false;
        bool hasRight = false;

        foreach (NpcSnapshot candidate in candidates)
        {
            if (!candidate.IsActive || candidate.Handle == source.Handle ||
                !NpcTypeId.TryCreate(candidate.Type, out NpcTypeId candidateType) ||
                VanillaTownNpcFacts1458.IsHousingEligible(candidateType) ||
                !VanillaNpcDefinitionCatalog.TryGet(candidateType, candidate.NetIdentity, out VanillaNpcDefinition definition) ||
                definition.Damage <= 0 ||
                candidate.Simulation.DontTakeDamage ||
                !definition.TryResolveHitbox(candidate.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
            {
                continue;
            }

            float candidateCenterX = candidate.PositionX + hitbox.Width * 0.5f;
            float candidateCenterY = candidate.PositionY + hitbox.Height * 0.5f;
            float dx = candidateCenterX - sourceCenterX;
            float dy = candidateCenterY - sourceCenterY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (!float.IsFinite(distance) || distance >= dangerDetectRange)
                continue;
            if (!candidate.Simulation.NoTileCollide &&
                !VanillaWorldLineOfSight.CanHitLine(tiles, sourceCenterX, sourceCenterY, candidateCenterX, candidateCenterY))
            {
                continue;
            }

            if (dx < 0f && (!hasLeft || dx > leftDelta))
            {
                leftDelta = dx;
                left = candidate;
                hasLeft = true;
            }
            else if (dx > 0f && (!hasRight || dx < rightDelta))
            {
                rightDelta = dx;
                right = candidate;
                hasRight = true;
            }
        }

        if (!hasLeft && !hasRight)
            return false;

        if (!hasLeft)
        {
            direction = 1;
            target = right;
            return true;
        }
        if (!hasRight)
        {
            direction = -1;
            target = left;
            return true;
        }

        if (rightDelta < -leftDelta)
        {
            direction = 1;
            target = right;
        }
        else
        {
            direction = -1;
            target = left;
        }
        return true;
    }

    private static class VanillaTownNpcDefinitionCatalogBridge
    {
        public static bool TryGetHitbox(in NpcSnapshot snapshot, out VanillaNpcHitboxSize hitbox)
        {
            if (!NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type) ||
                !VanillaNpcDefinitionCatalog.TryGet(type, snapshot.NetIdentity, out VanillaNpcDefinition definition))
            {
                hitbox = default;
                return false;
            }
            return definition.TryResolveHitbox(snapshot.Simulation.Scale, out hitbox);
        }

        public static bool TryGetCenter(
            in NpcSnapshot snapshot,
            out float centerX,
            out float centerY)
        {
            if (!TryGetHitbox(in snapshot, out VanillaNpcHitboxSize hitbox))
            {
                centerX = 0f;
                centerY = 0f;
                return false;
            }

            centerX = snapshot.PositionX + hitbox.Width * 0.5f;
            centerY = snapshot.PositionY + hitbox.Height * 0.5f;
            return true;
        }
    }

    private bool IsComplete(VanillaWorldProgressionId milestone) =>
        world.BaselineProgression.IsComplete(milestone) || progression.IsCompleted(milestone);

    private static NpcStateUpdate SnapshotUpdate(
        in NpcSnapshot snapshot,
        NpcAiState ai,
        NpcSimulationState simulation,
        float velocityX,
        float velocityY) =>
        new(
            snapshot.Type,
            snapshot.NetId,
            snapshot.PositionX,
            snapshot.PositionY,
            velocityX,
            velocityY,
            snapshot.Target,
            ai,
            simulation);
}
