from pathlib import Path

ROOT = Path('.')


def replace_once(path: str, old: str, new: str) -> None:
    p = ROOT / path
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one match, got {count}: {old[:120]!r}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')


def write_new(path: str, content: str) -> None:
    p = ROOT / path
    if p.exists():
        raise SystemExit(f'{path}: already exists')
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(content, encoding='utf-8')

replace_once(
    'src/TerraRuntime.Contracts/Runtime/NpcSnapshot.cs',
    '''    public int? DefenseOverride { get; init; }\n\n    /// <summary>\n    /// Server-owned vanilla NPC.reflectsProjectiles state for the current committed AI revision. Projectile\n''',
    '''    public int? DefenseOverride { get; init; }\n\n    /// <summary>\n    /// Optional live contact damage written by AI when vanilla mutates NPC.damage at runtime. Null means the\n    /// version-pinned definition damage remains authoritative. This is server-owned simulation state, not a\n    /// packet field, and is committed atomically with the AI revision that changed it.\n    /// </summary>\n    public int? DamageOverride { get; init; }\n\n    /// <summary>\n    /// Server-owned vanilla NPC.reflectsProjectiles state for the current committed AI revision. Projectile\n''')
replace_once(
    'src/TerraRuntime.Contracts/Runtime/NpcSnapshot.cs',
    '''        DefenseOverride = null,\n        ReflectsProjectiles = false\n''',
    '''        DefenseOverride = null,\n        DamageOverride = null,\n        ReflectsProjectiles = false\n''')

replace_once(
    'src/TerraRuntime.Core/Npcs/VanillaNpcBehaviorContext.cs',
    '''    public bool ExpertMode { get; private set; }\n\n    public void SetPlayerSnapshotLookup''',
    '''    public bool ExpertMode { get; private set; }\n\n    public bool MasterMode { get; private set; }\n\n    public void SetPlayerSnapshotLookup''')
replace_once(
    'src/TerraRuntime.Core/Npcs/VanillaNpcBehaviorContext.cs',
    '''    public void SetWorldConditions(\n        bool dayTime,\n        bool slimeRainActive,\n        bool goodWorld = false,\n        bool expertMode = false)\n    {\n        DayTime = dayTime;\n        SlimeRainActive = slimeRainActive;\n        GoodWorld = goodWorld;\n        ExpertMode = expertMode;\n    }\n''',
    '''    public void SetWorldConditions(\n        bool dayTime,\n        bool slimeRainActive,\n        bool goodWorld = false,\n        bool expertMode = false,\n        bool masterMode = false)\n    {\n        if (masterMode && !expertMode)\n            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));\n\n        DayTime = dayTime;\n        SlimeRainActive = slimeRainActive;\n        GoodWorld = goodWorld;\n        ExpertMode = expertMode;\n        MasterMode = masterMode;\n    }\n''')
replace_once(
    'src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs',
    '''    public void SetWorldConditions(\n        bool dayTime,\n        bool slimeRainActive,\n        bool goodWorld = false,\n        bool expertMode = false) =>\n        _context.SetWorldConditions(dayTime, slimeRainActive, goodWorld, expertMode);\n''',
    '''    public void SetWorldConditions(\n        bool dayTime,\n        bool slimeRainActive,\n        bool goodWorld = false,\n        bool expertMode = false,\n        bool masterMode = false) =>\n        _context.SetWorldConditions(dayTime, slimeRainActive, goodWorld, expertMode, masterMode);\n''')

replace_once(
    'src/TerraRuntime.Core/Npcs/VanillaEyeOfCthulhuExpertRapidDashNpcBehaviorStrategy.cs',
    '''        int? defenseOverride = source.Simulation.DefenseOverride;\n        if (hasLivingTarget && source.Ai.Ai0 == 3f)\n        {\n            defenseOverride = 0;\n            if (context.ExpertMode && lifeMax > 0)\n            {\n                if ((float)life < lifeMax * LowLifeFraction)\n                    defenseOverride = -15;\n                if ((float)life < lifeMax * CriticalLifeFraction)\n                    defenseOverride = -30;\n            }\n        }\n\n        bool reflectsProjectiles =\n''',
    '''        int? defenseOverride = source.Simulation.DefenseOverride;\n        int? damageOverride = source.Simulation.DamageOverride;\n        if (hasLivingTarget && source.Ai.Ai0 == 3f)\n        {\n            bool criticalLife = context.ExpertMode &&\n                lifeMax > 0 &&\n                (float)life < lifeMax * CriticalLifeFraction;\n            bool lowLife = context.ExpertMode &&\n                lifeMax > 0 &&\n                (float)life < lifeMax * LowLifeFraction;\n\n            defenseOverride = criticalLife ? -30 : lowLife ? -15 : 0;\n            damageOverride = context.MasterMode\n                ? criticalLife ? 60 : 54\n                : context.ExpertMode\n                    ? criticalLife ? 40 : 36\n                    : 23;\n        }\n\n        bool reflectsProjectiles =\n''')
replace_once(
    'src/TerraRuntime.Core/Npcs/VanillaEyeOfCthulhuExpertRapidDashNpcBehaviorStrategy.cs',
    '''                DefenseOverride = defenseOverride,\n                ReflectsProjectiles = reflectsProjectiles\n''',
    '''                DefenseOverride = defenseOverride,\n                DamageOverride = damageOverride,\n                ReflectsProjectiles = reflectsProjectiles\n''')

replace_once(
    'src/TerraRuntime.Core/Projectiles/RuntimeProjectileStore.cs',
    '''public readonly record struct ProjectileLifecycleState(\n    int TimeLeft,\n    bool NetImportant,\n    ProjectileLiquidState Liquid = default)\n{\n    public bool IsInitialized => TimeLeft > 0;\n}\n''',
    '''public readonly record struct ProjectileLifecycleState(\n    int TimeLeft,\n    bool NetImportant,\n    ProjectileLiquidState Liquid = default)\n{\n    public bool IsInitialized => TimeLeft > 0;\n\n    /// <summary>Projectile.oldVelocity captured at the source-equivalent update boundary.</summary>\n    public float OldVelocityX { get; init; }\n\n    public float OldVelocityY { get; init; }\n\n    /// <summary>Vanilla Projectile.reflected. A reflected generation cannot be reflected again.</summary>\n    public bool Reflected { get; init; }\n\n    /// <summary>Runtime-only authoritative penetrate override written by NPC.ReflectProjectile.</summary>\n    public int? PenetrateOverride { get; init; }\n}\n''')
replace_once(
    'src/TerraRuntime.Core/Projectiles/RuntimeProjectileStore.cs',
    '''        lifecycle = lifecycle with\n        {\n            TimeLeft = timeLeft,\n            Liquid = liquidState ?? lifecycle.Liquid\n        };\n''',
    '''        lifecycle = lifecycle with\n        {\n            TimeLeft = timeLeft,\n            Liquid = liquidState ?? lifecycle.Liquid,\n            OldVelocityX = state.Update.VelocityX,\n            OldVelocityY = state.Update.VelocityY\n        };\n''')
replace_once(
    'src/TerraRuntime.Core/Projectiles/RuntimeProjectileStore.cs',
    '''    public int CopyActive(Span<ProjectileSnapshot> destination)\n    {\n''',
    '''    /// <summary>\n    /// Atomically applies the source-backed NPC.ReflectProjectile mutation to one exact projectile generation.\n    /// Owner/spawner and original damage remain generation identity; reflection only changes current velocity,\n    /// current damage and runtime-only reflected/penetration state.\n    /// </summary>\n    public bool TryReflect(\n        ProjectileHandle handle,\n        float velocityX,\n        float velocityY,\n        short damage,\n        out ProjectileSnapshot snapshot)\n    {\n        if (!IsCurrentHandleCandidate(handle) ||\n            !float.IsFinite(velocityX) ||\n            !float.IsFinite(velocityY) ||\n            damage < 0)\n        {\n            snapshot = default;\n            return false;\n        }\n\n        ref SlotState state = ref _slots[handle.Slot];\n        if (!state.Active ||\n            state.Generation != handle.Generation.Value ||\n            state.Lifecycle.Reflected ||\n            !TryAdvance(ref state.Revision))\n        {\n            snapshot = default;\n            return false;\n        }\n\n        state.Update = state.Update with\n        {\n            VelocityX = velocityX,\n            VelocityY = velocityY,\n            Damage = damage\n        };\n        state.Lifecycle = state.Lifecycle with\n        {\n            Reflected = true,\n            PenetrateOverride = 1\n        };\n        snapshot = Capture(handle.Slot, in state);\n        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in snapshot);\n        return true;\n    }\n\n    public int CopyActive(Span<ProjectileSnapshot> destination)\n    {\n''')
replace_once(
    'src/TerraRuntime.Core/Projectiles/RuntimeProjectileStore.cs',
    '''        state.Update = update;\n        state.Lifecycle = lifecycle;\n    }\n''',
    '''        state.Update = update;\n        state.Lifecycle = lifecycle with\n        {\n            OldVelocityX = update.VelocityX,\n            OldVelocityY = update.VelocityY\n        };\n    }\n''')

write_new('src/TerraRuntime.Core/Projectiles/VanillaProjectileReflection1458.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public interface IVanillaProjectileReflectionRandom
{
    int NextInt32(int inclusiveMin, int exclusiveMax);
}

public readonly record struct VanillaProjectileReflectionResult(
    float VelocityX,
    float VelocityY,
    short Damage);

/// <summary>
/// TerrariaServer 1.4.5.8 NPC.ReflectProjectile gameplay mutation without sound/dust presentation effects.
/// The currently admitted projectile catalog can prove reflectability for aiStyle 1/2; unsupported source
/// styles and special type 728/955 remain fail-closed until their definitions are admitted.
/// </summary>
public static class VanillaProjectileReflection1458
{
    public static bool CanBeReflected(
        in ProjectileSnapshot projectile,
        in ProjectileLifecycleState lifecycle,
        in VanillaProjectileDefinition definition) =>
        projectile.IsActive &&
        VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) &&
        projectile.Damage > 0 &&
        !lifecycle.Reflected &&
        (definition.AiStyle == VanillaProjectileAiStyles.Arrow ||
         definition.AiStyle == VanillaProjectileAiStyles.Thrown);

    public static bool TryResolve(
        in ProjectileSnapshot projectile,
        in ProjectileLifecycleState lifecycle,
        float ownerCenterX,
        float ownerCenterY,
        IVanillaProjectileReflectionRandom random,
        out VanillaProjectileReflectionResult result)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!float.IsFinite(ownerCenterX) ||
            !float.IsFinite(ownerCenterY) ||
            !float.IsFinite(lifecycle.OldVelocityX) ||
            !float.IsFinite(lifecycle.OldVelocityY) ||
            projectile.Damage <= 0)
        {
            result = default;
            return false;
        }

        float oldSpeed = Length(lifecycle.OldVelocityX, lifecycle.OldVelocityY);
        if (!float.IsFinite(oldSpeed) || oldSpeed <= float.Epsilon)
        {
            result = default;
            return false;
        }

        if (!VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition))
        {
            result = default;
            return false;
        }

        float projectileCenterX = projectile.PositionX + definition.Width * 0.5f;
        float projectileCenterY = projectile.PositionY + definition.Height * 0.5f;
        float towardOwnerX = ownerCenterX - projectileCenterX;
        float towardOwnerY = ownerCenterY - projectileCenterY;
        if (!Normalize(ref towardOwnerX, ref towardOwnerY))
        {
            result = default;
            return false;
        }
        towardOwnerX *= oldSpeed;
        towardOwnerY *= oldSpeed;

        float velocityX = random.NextInt32(-100, 101);
        float velocityY = random.NextInt32(-100, 101);
        if (!Normalize(ref velocityX, ref velocityY))
        {
            result = default;
            return false;
        }
        velocityX *= oldSpeed;
        velocityY *= oldSpeed;
        velocityX += towardOwnerX * 20f;
        velocityY += towardOwnerY * 20f;
        if (!Normalize(ref velocityX, ref velocityY))
        {
            result = default;
            return false;
        }
        velocityX *= oldSpeed;
        velocityY *= oldSpeed;

        int damage = projectile.Damage;
        damage /= 2;
        damage /= 2;
        result = new VanillaProjectileReflectionResult(
            velocityX,
            velocityY,
            checked((short)damage));
        return true;
    }

    private static float Length(float x, float y) => MathF.Sqrt(x * x + y * y);

    private static bool Normalize(ref float x, ref float y)
    {
        float length = Length(x, y);
        if (!float.IsFinite(length) || length <= float.Epsilon)
            return false;
        float inverse = 1f / length;
        x *= inverse;
        y *= inverse;
        return float.IsFinite(x) && float.IsFinite(y);
    }
}
''')

write_new('src/TerraRuntime/RuntimeNpcProjectileReflectionPass.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Source-shaped projectile/NPC reflection pass for committed projectile positions. Terraria Projectile.Update
/// performs movement before Damage(), and Damage() tests targetNPC.reflectsProjectiles before NPC damage. This
/// pass therefore runs after the authoritative projectile simulation commit for the world tick and applies only
/// the reflection short-circuit. Ordinary projectile-to-NPC damage remains a separate combat slice.
/// </summary>
internal sealed class RuntimeNpcProjectileReflectionPass
{
    private const float PlayerWidth = 20f;
    private const float PlayerHeight = 42f;

    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeProjectileStore projectiles;
    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly IVanillaProjectileReflectionRandom random;
    private readonly NpcSnapshot[] npcScratch;
    private readonly ProjectileSnapshot[] projectileScratch;

    public RuntimeNpcProjectileReflectionPass(
        RuntimeNpcStore npcs,
        RuntimeProjectileStore projectiles,
        IRuntimePlayerSlotSnapshotLookup players,
        IVanillaProjectileReflectionRandom? random = null)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.random = random ?? new SystemProjectileReflectionRandom();
        npcScratch = new NpcSnapshot[npcs.Capacity];
        projectileScratch = new ProjectileSnapshot[projectiles.Capacity];
    }

    public int Tick()
    {
        int npcCount = npcs.CopyActive(npcScratch);
        int projectileCount = projectiles.CopyActive(projectileScratch);
        int reflected = 0;

        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            ProjectileSnapshot projectile = projectileScratch[projectileIndex];
            if (!projectiles.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle) ||
                !VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition projectileDefinition) ||
                !VanillaProjectileReflection1458.CanBeReflected(in projectile, in lifecycle, in projectileDefinition) ||
                !players.TryGetPlayer(new PlayerSlotId(projectile.Spawner), out PlayerStateSnapshot owner) ||
                !owner.Player.IsAssigned)
            {
                continue;
            }

            for (int npcIndex = 0; npcIndex < npcCount; npcIndex++)
            {
                NpcSnapshot npc = npcScratch[npcIndex];
                if (!npc.IsActive ||
                    !npc.Simulation.ReflectsProjectiles ||
                    !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition npcDefinition) ||
                    !npcDefinition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize npcHitbox) ||
                    !Intersects(in npc, in npcHitbox, in projectile, in projectileDefinition))
                {
                    continue;
                }

                if (!VanillaProjectileReflection1458.TryResolve(
                        in projectile,
                        in lifecycle,
                        owner.PositionX + PlayerWidth * 0.5f,
                        owner.PositionY + PlayerHeight * 0.5f,
                        random,
                        out VanillaProjectileReflectionResult mutation))
                {
                    break;
                }

                if (projectiles.TryReflect(
                        projectile.Handle,
                        mutation.VelocityX,
                        mutation.VelocityY,
                        mutation.Damage,
                        out _))
                {
                    reflected++;
                }
                break;
            }
        }

        return reflected;
    }

    private static bool Intersects(
        in NpcSnapshot npc,
        in VanillaNpcHitboxSize npcHitbox,
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition projectileDefinition) =>
        projectile.PositionX < npc.PositionX + npcHitbox.Width &&
        projectile.PositionX + projectileDefinition.Width > npc.PositionX &&
        projectile.PositionY < npc.PositionY + npcHitbox.Height &&
        projectile.PositionY + projectileDefinition.Height > npc.PositionY;

    private sealed class SystemProjectileReflectionRandom : IVanillaProjectileReflectionRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) =>
            Random.Shared.Next(inclusiveMin, exclusiveMax);
    }
}
''')

replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    private readonly RuntimeProjectileStateExecutor _projectileExecutor;\n    private readonly IProjectileStateStepper? _projectileStepper;\n''',
    '''    private readonly RuntimeProjectileStateExecutor _projectileExecutor;\n    private readonly IProjectileStateStepper? _projectileStepper;\n    private readonly RuntimeNpcProjectileReflectionPass _projectileReflections;\n''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    private readonly bool _expertMode;\n    private const int MaxTileEditsPerTickPerPlayer = 8;\n''',
    '''    private readonly bool _expertMode;\n    private readonly bool _masterMode;\n    private const int MaxTileEditsPerTickPerPlayer = 8;\n''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        _expertMode = expertMode;\n        if (masterMode && !expertMode)\n            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));\n''',
    '''        _expertMode = expertMode;\n        _masterMode = masterMode;\n        if (masterMode && !expertMode)\n            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));\n''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        _projectileExecutor = new RuntimeProjectileStateExecutor(_projectiles);\n        _projectileStepper = projectileStepper ??\n''',
    '''        _projectileExecutor = new RuntimeProjectileStateExecutor(_projectiles);\n        _projectileReflections = new RuntimeNpcProjectileReflectionPass(_npcs, _projectiles, this);\n        _projectileStepper = projectileStepper ??\n''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    public long RejectedProjectileDespawns { get; private set; }\n\n    public long RejectedClientProjectileUpdates''',
    '''    public long RejectedProjectileDespawns { get; private set; }\n\n    public long AppliedProjectileReflections { get; private set; }\n\n    public long RejectedClientProjectileUpdates''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''                    _worldClock.GetGoodWorld,\n                    _expertMode);\n''',
    '''                    _worldClock.GetGoodWorld,\n                    _expertMode,\n                    _masterMode);\n''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        if (_projectileStepper is not null)\n            LastProjectileTick = _projectileExecutor.Tick(_projectileStepper);\n        TickInstancedItemLeases();\n''',
    '''        if (_projectileStepper is not null)\n            LastProjectileTick = _projectileExecutor.Tick(_projectileStepper);\n        AppliedProjectileReflections += _projectileReflections.Tick();\n        TickInstancedItemLeases();\n''')

replace_once(
    'tests/TerraRuntime.Tests/VanillaEyeOfCthulhuCombatStateTests.cs',
    '''    [Theory]\n    [InlineData(300, -15)]\n    [InlineData(100, -30)]\n    public void Expert_phase_two_commits_source_negative_defense_bands(int life, int expectedDefense)\n''',
    '''    [Theory]\n    [InlineData(false, false, 1000, 23, 0)]\n    [InlineData(true, false, 1000, 36, 0)]\n    [InlineData(true, false, 300, 36, -15)]\n    [InlineData(true, false, 100, 40, -30)]\n    [InlineData(true, true, 1000, 54, 0)]\n    [InlineData(true, true, 300, 54, -15)]\n    [InlineData(true, true, 100, 60, -30)]\n    public void Phase_two_commits_source_damage_and_defense_difficulty_projection(\n        bool expertMode,\n        bool masterMode,\n        int life,\n        int expectedDamage,\n        int expectedDefense)\n    {\n        VanillaNpcTargetingAiStepper stepper = CreateStepper(\n            goodWorld: false,\n            expertMode: expertMode,\n            masterMode: masterMode);\n        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 0f, 0f, 0f), life);\n\n        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));\n\n        Assert.Equal(expectedDamage, next.Simulation.DamageOverride);\n        Assert.Equal(expectedDefense, next.Simulation.DefenseOverride);\n    }\n\n    [Theory]\n    [InlineData(300, -15)]\n    [InlineData(100, -30)]\n    public void Expert_phase_two_commits_source_negative_defense_bands(int life, int expectedDefense)\n''')
replace_once(
    'tests/TerraRuntime.Tests/VanillaEyeOfCthulhuCombatStateTests.cs',
    '''    private static VanillaNpcTargetingAiStepper CreateStepper(\n        bool goodWorld,\n        FixedEyeEnvironment? environment = null)\n''',
    '''    private static VanillaNpcTargetingAiStepper CreateStepper(\n        bool goodWorld,\n        FixedEyeEnvironment? environment = null,\n        bool expertMode = true,\n        bool masterMode = false)\n''')
replace_once(
    'tests/TerraRuntime.Tests/VanillaEyeOfCthulhuCombatStateTests.cs',
    '''            goodWorld: goodWorld,\n            expertMode: true);\n''',
    '''            goodWorld: goodWorld,\n            expertMode: expertMode,\n            masterMode: masterMode);\n''')

write_new('tests/TerraRuntime.Tests/VanillaProjectileReflection1458Tests.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileReflection1458Tests
{
    [Fact]
    public void Source_mutation_preserves_old_speed_and_quarters_damage()
    {
        ProjectileSnapshot projectile = Projectile(damage: 21, velocityX: 9f, velocityY: 1f);
        var lifecycle = new ProjectileLifecycleState(600, false)
        {
            OldVelocityX = 3f,
            OldVelocityY = 4f
        };
        var random = new SequenceRandom(100, 0);

        Assert.True(VanillaProjectileReflection1458.TryResolve(
            in projectile,
            in lifecycle,
            ownerCenterX: 300f,
            ownerCenterY: 121f,
            random,
            out VanillaProjectileReflectionResult result));

        Assert.Equal((short)5, result.Damage);
        Assert.Equal(5f, MathF.Sqrt(result.VelocityX * result.VelocityX + result.VelocityY * result.VelocityY), 4);
        Assert.Equal(2, random.Calls);
    }

    [Fact]
    public void Current_admitted_arrow_and_thrown_styles_are_reflectable_but_boomerang_and_reflected_state_are_not()
    {
        ProjectileSnapshot arrow = Projectile(damage: 20, velocityX: 3f, velocityY: 4f);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(arrow.Type, out VanillaProjectileDefinition arrowDefinition));
        var lifecycle = new ProjectileLifecycleState(600, false) { OldVelocityX = 3f, OldVelocityY = 4f };
        Assert.True(VanillaProjectileReflection1458.CanBeReflected(in arrow, in lifecycle, in arrowDefinition));

        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(VanillaProjectileIds.EnchantedBoomerang, out VanillaProjectileDefinition boomerangDefinition));
        ProjectileSnapshot boomerang = arrow with { Type = VanillaProjectileIds.EnchantedBoomerang };
        Assert.False(VanillaProjectileReflection1458.CanBeReflected(in boomerang, in lifecycle, in boomerangDefinition));

        ProjectileLifecycleState reflected = lifecycle with { Reflected = true };
        Assert.False(VanillaProjectileReflection1458.CanBeReflected(in arrow, in reflected, in arrowDefinition));
    }

    private static ProjectileSnapshot Projectile(short damage, float velocityX, float velocityY) =>
        new(
            new ProjectileHandle(0, new ProjectileGeneration(1)),
            new ProjectileRevision(1),
            VanillaProjectileIds.WoodenArrowFriendly,
            Spawner: 0,
            PositionX: 110f,
            PositionY: 110f,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: damage,
            KnockBack: 1f,
            OriginalDamage: damage);

    private sealed class SequenceRandom(params int[] values) : IVanillaProjectileReflectionRandom
    {
        private int index;
        public int Calls => index;

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
''')

write_new('tests/TerraRuntime.Tests/RuntimeNpcProjectileReflectionPassTests.cs', r'''using TerraRuntime.Contracts.Gameplay;
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
''')

write_new('tests/TerraRuntime.Tests/RuntimeProjectileReflectionStoreTests.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileReflectionStoreTests
{
    [Fact]
    public void Spawn_initializes_old_velocity_and_reflection_is_generation_safe_one_shot()
    {
        var store = new RuntimeProjectileStore(capacity: 2);
        var update = new ProjectileStateUpdate(
            VanillaProjectileIds.WoodenArrowFriendly,
            Spawner: 0,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 3f,
            VelocityY: 4f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);
        Assert.True(store.TrySpawn(0, in update, out ProjectileSnapshot projectile));
        Assert.True(store.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState initial));
        Assert.Equal(3f, initial.OldVelocityX);
        Assert.Equal(4f, initial.OldVelocityY);

        Assert.True(store.TryReflect(projectile.Handle, -5f, 0f, 5, out ProjectileSnapshot reflected));
        Assert.Equal(projectile.Revision.Value + 1, reflected.Revision.Value);
        Assert.False(store.TryReflect(projectile.Handle, 5f, 0f, 1, out _));
        Assert.True(store.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle));
        Assert.True(lifecycle.Reflected);
        Assert.Equal(1, lifecycle.PenetrateOverride);
    }
}
''')

replace_once(
    'docs/en/npc-behavior-families.md',
    '''These are bounded capabilities (`BossExpertPhaseOneSlice`, `BossExpertTransformationSlice`, `BossExpertPhaseTwoDeterministicSlice` and `BossExpertRapidDashSlice`), not a full difficulty claim. Master damage scaling and `getGoodWorld` reflection/re-entry effects remain outside the admitted slice; Good World still fails closed instead of silently inheriting Expert values. Classic behavior remains unchanged.\n''',
    '''These are bounded capabilities (`BossExpertPhaseOneSlice`, `BossExpertTransformationSlice`, `BossExpertPhaseTwoDeterministicSlice` and `BossExpertRapidDashSlice`), not a full difficulty claim. The live phase-two combat projection now commits source `NPC.damage`/`NPC.defense` values for Classic, Expert and Master, including the `<12%` and `<4%` bands. Good World transformation state drives an authoritative projectile/NPC reflection short-circuit after projectile movement: admitted aiStyle 1/2 player projectiles preserve `oldVelocity` speed, quarter current damage, become one-shot reflected state with penetrate `1`, and keep their original owner. Sound, dust, gore and other presentation-only effects remain outside the server gameplay claim.\n''')
replace_once(
    'docs/ru/npc-behavior-families.md',
    '''Это ограниченные возможности (`BossExpertPhaseOneSlice`, `BossExpertTransformationSlice`, `BossExpertPhaseTwoDeterministicSlice` и `BossExpertRapidDashSlice`), а не заявление о полной difficulty-parity. Master damage scaling и эффекты `getGoodWorld` reflection/re-entry остаются вне допущенного slice; Good World по-прежнему fail-closed и не наследует Expert-значения молча. Classic-поведение не изменено.\n''',
    '''Это ограниченные возможности (`BossExpertPhaseOneSlice`, `BossExpertTransformationSlice`, `BossExpertPhaseTwoDeterministicSlice` и `BossExpertRapidDashSlice`), а не заявление о полной difficulty-parity. Live phase-two combat projection теперь атомарно коммитит source-значения `NPC.damage`/`NPC.defense` для Classic, Expert и Master, включая пороги `<12%` и `<4%`. Good World transformation state запускает authoritative projectile/NPC reflection short-circuit после движения projectile: допущенные player-projectile с aiStyle 1/2 сохраняют скорость `oldVelocity`, получают четверть текущего damage, становятся одноразово reflected с penetrate `1` и сохраняют исходного owner. Звук, dust, gore и прочие presentation-only эффекты остаются вне server gameplay claim.\n''')
replace_once(
    'docs/roadmap/npc-ai-parity.md',
    '''- [ ] finish Eye of Cthulhu Good World reflection/re-entry, damage/defense difficulty projection and remaining irreversible/cosmetic effects;\n''',
    '''- [x] finish Eye of Cthulhu Good World gameplay reflection/re-entry and Classic/Expert/Master phase-two damage/defense projection;\n- [ ] finish Eye of Cthulhu remaining presentation-only sound/dust/gore effects and unsupported projectile-style reflection identities;\n''')
replace_once(
    'docs/roadmap/npc-ai-parity.md',
    '''Eye of Cthulhu still intentionally reports `FullVanillaAiParity = false`. Expert rapid dashes now consume the source RNG sequence through the injected authoritative NPC random stream and read live target velocity through the player-slot snapshot boundary. Good World reflection/re-entry and combat-stat difficulty projection remain separate open work, so the coverage catalog advertises `BossExpertRapidDashSlice` rather than full parity.\n''',
    '''Eye of Cthulhu still intentionally reports `FullVanillaAiParity = false`. Expert rapid dashes consume the source RNG sequence through the injected authoritative NPC random stream and read live target velocity through the player-slot snapshot boundary. Good World re-entry, transformation projectile reflection and phase-two Classic/Expert/Master damage/defense projection are now authoritative gameplay state. Reflection is bounded to currently admitted aiStyle 1/2 player projectile identities; source-special 728/955 and presentation-only sound/dust/gore remain open, so the coverage catalog still advertises bounded boss slices rather than full parity.\n''')

print('Eye of Cthulhu Good World combat/reflection block applied.')
