from pathlib import Path


def write(path: str, content: str) -> None:
    Path(path).write_text(content, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"marker changed in {path}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


write("src/TerraRuntime.Core/Npcs/RuntimeNpcAiProjectileIntents.cs", r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// One speculative server-owned projectile requested by an NPC AI transition. The intent contains gameplay
/// facts only; physical projectile slot allocation remains owned by RuntimeProjectileStore after the source
/// NPC generation-safe transition commits.
/// </summary>
public readonly record struct NpcAiProjectileIntent(
    ProjectileTypeId Type,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    int Damage,
    float KnockBack);

/// <summary>
/// Optional capability exposed by an NPC AI composition. Implementations may inspect the proposed NPC state,
/// but must not publish projectiles directly: RuntimeNpcAiStateExecutor applies returned intents only after the
/// exact source NPC generation has committed successfully.
/// </summary>
public interface INpcAiProjectileIntentPlanner
{
    int PlanProjectileSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileIntent> destination);
}

public static class RuntimeNpcProjectileIntentApplier
{
    public const byte ServerSpawner = byte.MaxValue;

    public static bool TryApply(
        RuntimeProjectileStore projectiles,
        in NpcAiProjectileIntent intent,
        out ProjectileSnapshot spawned)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        if (!VanillaProjectileIds.IsLiveWireType(intent.Type) ||
            !float.IsFinite(intent.PositionX) ||
            !float.IsFinite(intent.PositionY) ||
            !float.IsFinite(intent.VelocityX) ||
            !float.IsFinite(intent.VelocityY) ||
            !float.IsFinite(intent.KnockBack) ||
            intent.KnockBack < 0f ||
            intent.Damage < 0 ||
            intent.Damage > short.MaxValue)
        {
            spawned = default;
            return false;
        }

        short damage = checked((short)intent.Damage);
        var update = new ProjectileStateUpdate(
            intent.Type,
            ServerSpawner,
            intent.PositionX,
            intent.PositionY,
            intent.VelocityX,
            intent.VelocityY,
            default,
            BannerIdToRespondTo: 0,
            Damage: damage,
            KnockBack: intent.KnockBack,
            OriginalDamage: damage);
        return projectiles.TrySpawnVanilla(in update, out spawned);
    }
}
''')

write("src/TerraRuntime.Core/Npcs/VanillaFlyerProjectileAttack.cs", r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>World LOS query consumed by source-backed ordinary AI_005 projectile attacks.</summary>
public interface IVanillaNpcProjectileEnvironment
{
    bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight);
}

/// <summary>
/// TerrariaServer 1.4.5.8 NPC.AI_GlobalFiringDistanceCheck. The source uses Main.MaxWorldViewSize 1920x1200,
/// centers that rectangle on the target Point, then inflates it by -50 pixels on both axes. Rectangle right and
/// bottom edges are exclusive, matching XNA Rectangle.Contains.
/// </summary>
public static class VanillaNpcGlobalFiringDistance
{
    public const int MaxWorldViewWidth = 1920;
    public const int MaxWorldViewHeight = 1200;
    public const int EdgeInset = 50;
    public const int HorizontalReach = MaxWorldViewWidth / 2 - EdgeInset;
    public const int VerticalReach = MaxWorldViewHeight / 2 - EdgeInset;

    public static bool Contains(float shootX, float shootY, float targetX, float targetY)
    {
        if (!float.IsFinite(shootX) ||
            !float.IsFinite(shootY) ||
            !float.IsFinite(targetX) ||
            !float.IsFinite(targetY))
        {
            return false;
        }

        int sx = (int)shootX;
        int sy = (int)shootY;
        int tx = (int)targetX;
        int ty = (int)targetY;
        return sx >= tx - HorizontalReach &&
               sx < tx + HorizontalReach &&
               sy >= ty - VerticalReach &&
               sy < ty + VerticalReach;
    }
}

public readonly record struct VanillaFlyerProjectileAttackResult(
    NpcAiState LocalAi,
    float VelocityX,
    float VelocityY,
    bool ProjectileReady);

/// <summary>
/// Source-backed server-state portion of ordinary TerrariaServer 1.4.5.8 AI_005 projectile attacks. Probe and
/// Blood Squid use localAI[0] as a server-only timer; the returned state is folded into the same NpcStateUpdate
/// revision as movement. Projectile publication remains a separate post-commit intent.
/// </summary>
public static class VanillaFlyerProjectileAttack
{
    public const float ProbeAttackThreshold = 120f;
    public const float BloodSquidAttackThreshold = 120f;
    public const float BloodSquidRetryTimer = 50f;
    public const float BloodSquidMaximumShotDistance = 400f;
    public const float BloodSquidRecoilSpeed = 5f;
    public const float BloodSquidProjectileSpeed = 15f;

    public static bool IsSupportedShooter(NpcTypeId type) =>
        type == VanillaNpcIds.Probe || type == VanillaNpcIds.BloodSquid;

    public static bool TryStep(
        NpcTypeId type,
        in NpcSnapshot npc,
        in VanillaNpcHitboxSize hitbox,
        in VanillaNpcTargetCandidate target,
        float postMotionVelocityX,
        float postMotionVelocityY,
        IVanillaNpcProjectileEnvironment? environment,
        out VanillaFlyerProjectileAttackResult result)
    {
        if (!IsSupportedShooter(type) ||
            !float.IsFinite(postMotionVelocityX) ||
            !float.IsFinite(postMotionVelocityY) ||
            !npc.Simulation.LocalAi.IsFinite ||
            !float.IsFinite(target.CenterX) ||
            !float.IsFinite(target.CenterY))
        {
            result = default;
            return false;
        }

        float localAi0 = npc.Simulation.LocalAi.Ai0;
        float sourceCenterX = npc.PositionX + hitbox.Width * 0.5f;
        float sourceCenterY = npc.PositionY + hitbox.Height * 0.5f;
        float targetPositionX = target.CenterX - VanillaNpcBehaviorContext.BasePlayerWidth * 0.5f;
        float targetPositionY = target.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f;
        bool targetUsable = target.Active && !target.Dead && !target.Ghost;

        if (type == VanillaNpcIds.Probe)
        {
            // ai[3] != 0 belongs to the Mech Queen attachment path. TerraRuntime does not claim that composite
            // encounter yet, so ordinary Probe attack state remains fail-closed instead of applying its different
            // 360-tick cadence with incomplete parent state.
            if (npc.Ai.Ai3 != 0f)
            {
                result = new(
                    npc.Simulation.LocalAi,
                    postMotionVelocityX,
                    postMotionVelocityY,
                    ProjectileReady: false);
                return true;
            }

            if (npc.Simulation.JustHit)
            {
                localAi0 = 0f;
            }
            else
            {
                localAi0 += 1f;
            }

            bool ready = false;
            if (localAi0 >= ProbeAttackThreshold)
            {
                localAi0 = 0f;
                ready = targetUsable &&
                    VanillaNpcGlobalFiringDistance.Contains(
                        sourceCenterX,
                        sourceCenterY,
                        target.CenterX,
                        target.CenterY) &&
                    environment?.CanHit(
                        npc.PositionX,
                        npc.PositionY,
                        hitbox.Width,
                        hitbox.Height,
                        targetPositionX,
                        targetPositionY,
                        (int)VanillaNpcBehaviorContext.BasePlayerWidth,
                        (int)VanillaNpcBehaviorContext.BasePlayerHeight) == true;
            }

            result = new(
                new NpcAiState(
                    localAi0,
                    npc.Simulation.LocalAi.Ai1,
                    npc.Simulation.LocalAi.Ai2,
                    npc.Simulation.LocalAi.Ai3),
                postMotionVelocityX,
                postMotionVelocityY,
                ready);
            return true;
        }

        if (!targetUsable)
        {
            result = new(
                npc.Simulation.LocalAi,
                postMotionVelocityX,
                postMotionVelocityY,
                ProjectileReady: false);
            return true;
        }

        if (npc.Simulation.JustHit)
            localAi0 += 10f;
        localAi0 += 1f;

        bool bloodReady = false;
        float velocityX = postMotionVelocityX;
        float velocityY = postMotionVelocityY;
        if (localAi0 >= BloodSquidAttackThreshold)
        {
            float deltaCenterX = target.CenterX - sourceCenterX;
            float deltaCenterY = target.CenterY - sourceCenterY;
            bool withinShotDistance =
                deltaCenterX * deltaCenterX + deltaCenterY * deltaCenterY <
                BloodSquidMaximumShotDistance * BloodSquidMaximumShotDistance;
            bool canFire =
                VanillaNpcGlobalFiringDistance.Contains(
                    sourceCenterX,
                    sourceCenterY,
                    target.CenterX,
                    target.CenterY) &&
                environment?.CanHit(
                    npc.PositionX,
                    npc.PositionY,
                    hitbox.Width,
                    hitbox.Height,
                    targetPositionX,
                    targetPositionY,
                    (int)VanillaNpcBehaviorContext.BasePlayerWidth,
                    (int)VanillaNpcBehaviorContext.BasePlayerHeight) == true &&
                withinShotDistance;

            if (canFire)
            {
                float recoilX = target.CenterX - sourceCenterX;
                float recoilY = targetPositionY - sourceCenterY;
                Normalize(ref recoilX, ref recoilY, BloodSquidRecoilSpeed);
                velocityX = -recoilX;
                velocityY = -recoilY;
                localAi0 = 0f;
                bloodReady = true;
            }
            else
            {
                localAi0 = BloodSquidRetryTimer;
            }
        }

        result = new(
            new NpcAiState(
                localAi0,
                npc.Simulation.LocalAi.Ai1,
                npc.Simulation.LocalAi.Ai2,
                npc.Simulation.LocalAi.Ai3),
            velocityX,
            velocityY,
            bloodReady);
        return true;
    }

    public static void Normalize(ref float x, ref float y, float speed)
    {
        float lengthSquared = x * x + y * y;
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0f)
        {
            x = 0f;
            y = 0f;
            return;
        }

        float scale = speed / MathF.Sqrt(lengthSquared);
        x *= scale;
        y *= scale;
    }
}
''')

write("src/TerraRuntime.Application/VanillaNpcProjectileWorldEnvironment.cs", r'''using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>Production tile LOS adapter for source-backed ordinary NPC projectile attacks.</summary>
internal sealed class VanillaNpcProjectileWorldEnvironment : IVanillaNpcProjectileEnvironment
{
    private readonly WorldTileStore tiles;

    public VanillaNpcProjectileWorldEnvironment(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight) =>
        VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourcePositionX,
            sourcePositionY,
            sourceWidth,
            sourceHeight,
            targetPositionX,
            targetPositionY,
            targetWidth,
            targetHeight);
}
''')

replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs",
    "    public static readonly ProjectileTypeId PoisonedKnife = new(54);\n    public static readonly ProjectileTypeId ConfettiGun = new(178);",
    "    public static readonly ProjectileTypeId PoisonedKnife = new(54);\n    public static readonly ProjectileTypeId ProbePinkLaser = new(84);\n    public static readonly ProjectileTypeId ConfettiGun = new(178);\n    public static readonly ProjectileTypeId BloodShot = new(811);")

# Extend executor with bounded post-commit projectile intents.
replace_once(
    "src/TerraRuntime.Core/Npcs/RuntimeNpcAiStateExecutor.cs",
    "    private readonly RuntimeNpcStore _npcs;\n    private readonly NpcSnapshot[] _snapshotBuffer;\n    private readonly NpcAiSpawnIntent[] _spawnIntentBuffer;\n\n    public RuntimeNpcAiStateExecutor(RuntimeNpcStore npcs)\n    {\n        ArgumentNullException.ThrowIfNull(npcs);\n        _npcs = npcs;\n        _snapshotBuffer = new NpcSnapshot[npcs.Capacity];\n        _spawnIntentBuffer = new NpcAiSpawnIntent[npcs.Capacity];\n    }",
    "    private const int MaximumProjectileIntentsPerNpcStep = 8;\n\n    private readonly RuntimeNpcStore _npcs;\n    private readonly RuntimeProjectileStore? _projectiles;\n    private readonly NpcSnapshot[] _snapshotBuffer;\n    private readonly NpcAiSpawnIntent[] _spawnIntentBuffer;\n    private readonly NpcAiProjectileIntent[] _projectileIntentBuffer;\n\n    public RuntimeNpcAiStateExecutor(RuntimeNpcStore npcs, RuntimeProjectileStore? projectiles = null)\n    {\n        ArgumentNullException.ThrowIfNull(npcs);\n        _npcs = npcs;\n        _projectiles = projectiles;\n        _snapshotBuffer = new NpcSnapshot[npcs.Capacity];\n        _spawnIntentBuffer = new NpcAiSpawnIntent[npcs.Capacity];\n        _projectileIntentBuffer = new NpcAiProjectileIntent[MaximumProjectileIntentsPerNpcStep];\n    }")
replace_once(
    "src/TerraRuntime.Core/Npcs/RuntimeNpcAiStateExecutor.cs",
    "        INpcAiSpawnIntentPlanner? spawnPlanner =\n            NpcAiStateStepperComposition.FindCapability<INpcAiSpawnIntentPlanner>(stepper);\n        INpcAiStatePostCommitObserver? postCommitObserver =",
    "        INpcAiSpawnIntentPlanner? spawnPlanner =\n            NpcAiStateStepperComposition.FindCapability<INpcAiSpawnIntentPlanner>(stepper);\n        INpcAiProjectileIntentPlanner? projectilePlanner = _projectiles is null\n            ? null\n            : NpcAiStateStepperComposition.FindCapability<INpcAiProjectileIntentPlanner>(stepper);\n        INpcAiStatePostCommitObserver? postCommitObserver =")
replace_once(
    "src/TerraRuntime.Core/Npcs/RuntimeNpcAiStateExecutor.cs",
    "            if ((uint)spawnCount > (uint)_spawnIntentBuffer.Length)\n            {\n                rejected++;\n                continue;\n            }\n\n            if (_npcs.TryUpdate(npc.Handle, in next, out NpcSnapshot committed))",
    "            if ((uint)spawnCount > (uint)_spawnIntentBuffer.Length)\n            {\n                rejected++;\n                continue;\n            }\n\n            int projectileCount = projectilePlanner?.PlanProjectileSpawns(\n                in npc,\n                in next,\n                _projectileIntentBuffer) ?? 0;\n            if ((uint)projectileCount > (uint)_projectileIntentBuffer.Length)\n            {\n                rejected++;\n                continue;\n            }\n\n            if (_npcs.TryUpdate(npc.Handle, in next, out NpcSnapshot committed))")
replace_once(
    "src/TerraRuntime.Core/Npcs/RuntimeNpcAiStateExecutor.cs",
    "                postCommitObserver?.NpcAiStateCommitted(in npc, in committed);\n                commitSink?.NpcAiStateCommitted(in committed);\n\n                for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)",
    "                postCommitObserver?.NpcAiStateCommitted(in npc, in committed);\n                commitSink?.NpcAiStateCommitted(in committed);\n\n                if (_projectiles is not null)\n                {\n                    for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)\n                    {\n                        NpcAiProjectileIntent intent = _projectileIntentBuffer[projectileIndex];\n                        RuntimeNpcProjectileIntentApplier.TryApply(_projectiles, in intent, out _);\n                    }\n                }\n\n                for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)")
replace_once(
    "src/TerraRuntime.Core/Npcs/RuntimeNpcAiStateExecutor.cs",
    "/// changes cannot let stale AI work mutate a replacement NPC in the same slot. Optional NPC spawn intents\n/// are planned speculatively into executor-owned bounded scratch storage and are applied in order only after\n/// that source-state commit succeeds; newly spawned NPCs therefore cannot enter the same pre-pass or escape",
    "/// changes cannot let stale AI work mutate a replacement NPC in the same slot. Optional NPC and projectile\n/// spawn intents are planned speculatively into executor-owned bounded scratch storage and are applied in order\n/// only after that source-state commit succeeds; spawned entities therefore cannot enter the same pre-pass or escape")

# Replace the flyer strategy as one coherent state+intent implementation.
p = Path("src/TerraRuntime.Core/Npcs/VanillaNpcBehaviorStrategies.cs")
text = p.read_text(encoding="utf-8")
start = text.index("internal sealed class VanillaServantOfCthulhuNpcBehaviorStrategy")
end = text.index("internal sealed class VanillaWormNpcBehaviorStrategy", start)
flyer = r'''internal sealed class VanillaServantOfCthulhuNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private const int ProbeClassicDamage = 25;
    private const int ProbeExpertDamage = 22;
    private const int BloodSquidDamage = 35;
    private const float BloodSquidKnockBack = 1f;

    private readonly IVanillaNpcRandom random;
    private IVanillaNpcProjectileEnvironment? projectileEnvironment;

    public VanillaServantOfCthulhuNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        this.random = random ?? throw new ArgumentNullException(nameof(random));

    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment environment) =>
        projectileEnvironment = environment ?? throw new ArgumentNullException(nameof(environment));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Flyer ||
            !VanillaFlyerNpcCatalog.TryGetMotionProfile(
                definition.Type,
                out VanillaFlyerMotionProfile profile) ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        if (VanillaFlyerNpcCatalog.UsesScaleSpeedHandicap(definition.Type))
        {
            float speedFactor = 2f - npc.Simulation.Scale;
            if (!float.IsFinite(speedFactor) || speedFactor <= 0f)
            {
                next = default;
                return false;
            }

            profile = profile with
            {
                MaximumSpeed = profile.MaximumSpeed * speedFactor,
                Acceleration = profile.Acceleration * speedFactor
            };
        }

        if (!context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) ||
            !context.TryFindCandidate(checked((byte)closest.Target), out VanillaNpcTargetCandidate candidate))
        {
            NpcSimulationState idleSimulation = npc.Simulation with
            {
                NoGravity = true,
                NoTileCollide = definition.NoTileCollideAtSpawn
            };
            next = new NpcStateUpdate(
                definition.Type.Value,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                idleSimulation);
            return true;
        }

        var input = new VanillaFlyerAiMotionInput(
            PositionY: npc.PositionY,
            NpcCenterX: npc.PositionX + hitbox.Width * 0.5f,
            NpcCenterY: npc.PositionY + hitbox.Height * 0.5f,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            TargetCenterX: candidate.CenterX,
            TargetCenterY: candidate.CenterY,
            TargetTopY: candidate.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f,
            OldVelocityX: npc.Simulation.OldVelocityX,
            OldVelocityY: npc.Simulation.OldVelocityY,
            DirectionX: closest.DirectionX,
            Ai: npc.Ai,
            Scale: npc.Simulation.Scale,
            CollideX: npc.Simulation.CollideX,
            CollideY: npc.Simulation.CollideY,
            Wet: npc.Simulation.Wet,
            DayTime: context.DayTime,
            ExpertMode: context.ExpertMode,
            WorldSurfacePixels: context.WorldSurfacePixels,
            TimeLeft: npc.Simulation.TimeLeft);
        if (!VanillaFlyerAiMotion.TryStep(
                definition.Type,
                in input,
                in profile,
                out VanillaFlyerAiMotionResult result))
        {
            next = default;
            return false;
        }

        float finalVelocityX = result.VelocityX;
        float finalVelocityY = result.VelocityY;
        NpcAiState localAi = npc.Simulation.LocalAi;
        if (VanillaFlyerProjectileAttack.IsSupportedShooter(definition.Type) &&
            VanillaFlyerProjectileAttack.TryStep(
                definition.Type,
                in npc,
                in hitbox,
                in candidate,
                result.VelocityX,
                result.VelocityY,
                projectileEnvironment,
                out VanillaFlyerProjectileAttackResult attack))
        {
            finalVelocityX = attack.VelocityX;
            finalVelocityY = attack.VelocityY;
            localAi = attack.LocalAi;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            finalVelocityX,
            finalVelocityY,
            closest.Target,
            result.Ai,
            npc.Simulation with
            {
                DirectionX = closest.DirectionX,
                DirectionY = closest.DirectionY,
                NoGravity = true,
                NoTileCollide = definition.NoTileCollideAtSpawn,
                TimeLeft = result.TimeLeft,
                LocalAi = localAi
            });
        return true;
    }

    public int PlanProjectileSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        VanillaNpcBehaviorContext context,
        Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty ||
            proposed.Type != source.Type ||
            !NpcTypeId.TryCreate(source.Type, out NpcTypeId type) ||
            !VanillaFlyerProjectileAttack.IsSupportedShooter(type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, source.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            proposed.Target >= byte.MaxValue ||
            !context.TryFindCandidate(checked((byte)proposed.Target), out VanillaNpcTargetCandidate target))
        {
            return 0;
        }

        if (!VanillaFlyerProjectileAttack.TryStep(
                type,
                in source,
                in hitbox,
                in target,
                proposed.VelocityX,
                proposed.VelocityY,
                projectileEnvironment,
                out VanillaFlyerProjectileAttackResult attack) ||
            !attack.ProjectileReady ||
            !attack.LocalAi.Equals(proposed.Simulation.LocalAi))
        {
            return 0;
        }

        float sourceCenterX = source.PositionX + hitbox.Width * 0.5f;
        float sourceCenterY = source.PositionY + hitbox.Height * 0.5f;
        if (type == VanillaNpcIds.Probe)
        {
            if (!VanillaFlyerNpcCatalog.TryGetMotionProfile(type, out VanillaFlyerMotionProfile profile))
                return 0;

            float velocityX = target.CenterX - sourceCenterX;
            float velocityY = target.CenterY - sourceCenterY;
            float distanceSquared = velocityX * velocityX + velocityY * velocityY;
            if (distanceSquared < profile.MaximumSpeed * profile.MaximumSpeed)
            {
                velocityX = source.VelocityX;
                velocityY = source.VelocityY;
            }
            else
            {
                VanillaFlyerProjectileAttack.Normalize(ref velocityX, ref velocityY, profile.MaximumSpeed);
            }

            destination[0] = new NpcAiProjectileIntent(
                VanillaProjectileIds.ProbePinkLaser,
                sourceCenterX,
                sourceCenterY,
                velocityX,
                velocityY,
                context.ExpertMode ? ProbeExpertDamage : ProbeClassicDamage,
                KnockBack: 0f);
            return 1;
        }

        float targetPositionY = target.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f;
        int offsetX = random.NextInt32(-100, 101);
        int offsetY = random.NextInt32(-100, 101);
        float shotX = target.CenterX + offsetX - sourceCenterX;
        float shotY = targetPositionY + offsetY - sourceCenterY;
        VanillaFlyerProjectileAttack.Normalize(
            ref shotX,
            ref shotY,
            VanillaFlyerProjectileAttack.BloodSquidProjectileSpeed);
        destination[0] = new NpcAiProjectileIntent(
            VanillaProjectileIds.BloodShot,
            sourceCenterX,
            sourceCenterY,
            shotX,
            shotY,
            BloodSquidDamage,
            BloodSquidKnockBack);
        return 1;
    }
}

'''
p.write_text(text[:start] + flyer + text[end:], encoding="utf-8")

# Expose the typed flyer strategy and planner capability through the compatibility facade.
replace_once(
    "src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs",
    "    INpcAiStateStepper,\n    INpcAiSpawnIntentPlanner,\n    INpcAiPeerSnapshotConsumer",
    "    INpcAiStateStepper,\n    INpcAiSpawnIntentPlanner,\n    INpcAiProjectileIntentPlanner,\n    INpcAiPeerSnapshotConsumer")
replace_once(
    "src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs",
    "    private readonly IVanillaNpcBehaviorStrategy _flyer = new VanillaServantOfCthulhuNpcBehaviorStrategy();",
    "    private readonly VanillaServantOfCthulhuNpcBehaviorStrategy _flyer;")
replace_once(
    "src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs",
    "        _random = random ?? new SystemVanillaNpcRandom();\n        _eyeOfCthulhu = new VanillaEyeOfCthulhuExpertRapidDashNpcBehaviorStrategy(_random);",
    "        _random = random ?? new SystemVanillaNpcRandom();\n        _flyer = new VanillaServantOfCthulhuNpcBehaviorStrategy(_random);\n        _eyeOfCthulhu = new VanillaEyeOfCthulhuExpertRapidDashNpcBehaviorStrategy(_random);")
replace_once(
    "src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs",
    "    public void SetFlyingEyeEnvironment(IVanillaFlyingEyeEnvironment environment) =>\n        _flyingEye.SetEnvironment(environment);\n",
    "    public void SetFlyingEyeEnvironment(IVanillaFlyingEyeEnvironment environment) =>\n        _flyingEye.SetEnvironment(environment);\n\n    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment environment) =>\n        _flyer.SetProjectileEnvironment(environment);\n")
replace_once(
    "src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs",
    "        return 0;\n    }\n\n    private int PlanWormFollower(",
    "        return 0;\n    }\n\n    public int PlanProjectileSpawns(\n        in NpcSnapshot source,\n        in NpcStateUpdate proposed,\n        Span<NpcAiProjectileIntent> destination) =>\n        _flyer.PlanProjectileSpawns(in source, in proposed, _context, destination);\n\n    private int PlanWormFollower(")
replace_once(
    "src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs",
    "/// effects are exposed separately as speculative intents and are committed only by RuntimeNpcAiStateExecutor\n/// after the source state transition succeeds.",
    "/// effects are exposed separately as speculative NPC/projectile intents and are committed only by\n/// RuntimeNpcAiStateExecutor after the source state transition succeeds.")

# Production wiring: one projectile store feeds both NPC side effects and projectile simulation.
replace_once(
    "src/TerraRuntime.Application/ServerRuntimeState.cs",
    "        _npcs = npcs ?? new RuntimeNpcStore();\n        _npcAiExecutor = new RuntimeNpcAiStateExecutor(_npcs);",
    "        _npcs = npcs ?? new RuntimeNpcStore();\n        _projectiles = projectiles ?? new RuntimeProjectileStore();\n        _npcAiExecutor = new RuntimeNpcAiStateExecutor(_npcs, _projectiles);")
replace_once(
    "src/TerraRuntime.Application/ServerRuntimeState.cs",
    "        _npcShops = npcShops ?? new RuntimeNpcShopCatalogRegistry();\n        _projectiles = projectiles ?? new RuntimeProjectileStore();\n        _projectileExecutor = new RuntimeProjectileStateExecutor(_projectiles);",
    "        _npcShops = npcShops ?? new RuntimeNpcShopCatalogRegistry();\n        _projectileExecutor = new RuntimeProjectileStateExecutor(_projectiles);")
replace_once(
    "src/TerraRuntime.Application/ServerRuntimeState.cs",
    "                _vanillaNpcTargetingAiStepper.SetFlyingEyeEnvironment(new VanillaFlyingEyeWorldEnvironment(worldTiles));",
    "                _vanillaNpcTargetingAiStepper.SetFlyingEyeEnvironment(new VanillaFlyingEyeWorldEnvironment(worldTiles));\n                _vanillaNpcTargetingAiStepper.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(worldTiles));")

# Advertise this as a tested partial capability only for the two implemented shooters.
replace_once(
    "src/TerraRuntime.Core/Npcs/VanillaNpcAiCoverageCatalog.cs",
    "    BossExpertRapidDashSlice = 1u << 24,\n    FlyingEyeLifecycleStateSlice = 1u << 25",
    "    BossExpertRapidDashSlice = 1u << 24,\n    FlyingEyeLifecycleStateSlice = 1u << 25,\n    FlyerProjectileSideEffectSlice = 1u << 26")
replace_once(
    "src/TerraRuntime.Core/Npcs/VanillaNpcAiCoverageCatalog.cs",
    "            if (HasNegativeNetVariant(definition.Type))\n                capabilities |= VanillaNpcAiCapability.NegativeNetVariantDefaults;\n\n            entries[index++] = Partial(definition.Type, capabilities);\n        }\n\n        foreach (VanillaWormNpcEntry worm",
    "            if (HasNegativeNetVariant(definition.Type))\n                capabilities |= VanillaNpcAiCapability.NegativeNetVariantDefaults;\n            if (definition.Type == VanillaNpcIds.Probe || definition.Type == VanillaNpcIds.BloodSquid)\n                capabilities |= VanillaNpcAiCapability.FlyerProjectileSideEffectSlice;\n\n            entries[index++] = Partial(definition.Type, capabilities);\n        }\n\n        foreach (VanillaWormNpcEntry worm")

write("tests/TerraRuntime.Tests/VanillaFlyerProjectileAttackTests.cs", r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaFlyerProjectileAttackTests
{
    [Fact]
    public void Global_firing_rectangle_matches_source_edges()
    {
        Assert.True(VanillaNpcGlobalFiringDistance.Contains(-910f, -550f, 0f, 0f));
        Assert.True(VanillaNpcGlobalFiringDistance.Contains(909.99f, 549.99f, 0f, 0f));
        Assert.False(VanillaNpcGlobalFiringDistance.Contains(910f, 0f, 0f, 0f));
        Assert.False(VanillaNpcGlobalFiringDistance.Contains(0f, 550f, 0f, 0f));
    }

    [Fact]
    public void Probe_threshold_resets_local_timer_and_becomes_ready_with_los()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.Probe, localAi0: 119f);
        VanillaNpcHitboxSize hitbox = new(30, 30);
        VanillaNpcTargetCandidate target = Target(centerX: 200f, centerY: 100f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.Probe,
            in npc,
            in hitbox,
            in target,
            postMotionVelocityX: 1f,
            postMotionVelocityY: 2f,
            new FixedEnvironment(true),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(0f, result.LocalAi.Ai0);
        Assert.True(result.ProjectileReady);
        Assert.Equal(1f, result.VelocityX);
        Assert.Equal(2f, result.VelocityY);
    }

    [Fact]
    public void Probe_threshold_resets_even_when_los_blocks_fire()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.Probe, localAi0: 119f);
        VanillaNpcHitboxSize hitbox = new(30, 30);
        VanillaNpcTargetCandidate target = Target(centerX: 200f, centerY: 100f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.Probe,
            in npc,
            in hitbox,
            in target,
            0f,
            0f,
            new FixedEnvironment(false),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(0f, result.LocalAi.Ai0);
        Assert.False(result.ProjectileReady);
    }

    [Fact]
    public void Probe_just_hit_resets_timer_without_firing()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.Probe, localAi0: 119f) with
        {
            Simulation = CreateNpc(VanillaNpcIds.Probe, 119f).Simulation with { JustHit = true }
        };
        VanillaNpcHitboxSize hitbox = new(30, 30);
        VanillaNpcTargetCandidate target = Target(200f, 100f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.Probe,
            in npc,
            in hitbox,
            in target,
            0f,
            0f,
            new FixedEnvironment(true),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(0f, result.LocalAi.Ai0);
        Assert.False(result.ProjectileReady);
    }

    [Fact]
    public void Blood_squid_threshold_applies_recoil_and_resets_timer()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.BloodSquid, localAi0: 119f);
        VanillaNpcHitboxSize hitbox = new(44, 44);
        VanillaNpcTargetCandidate target = Target(centerX: 200f, centerY: 120f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.BloodSquid,
            in npc,
            in hitbox,
            in target,
            1f,
            1f,
            new FixedEnvironment(true),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(0f, result.LocalAi.Ai0);
        Assert.True(result.ProjectileReady);
        Assert.InRange(MathF.Sqrt(result.VelocityX * result.VelocityX + result.VelocityY * result.VelocityY), 4.999f, 5.001f);
        Assert.True(result.VelocityX < 0f);
    }

    [Fact]
    public void Blood_squid_blocked_threshold_uses_source_retry_timer()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.BloodSquid, localAi0: 119f);
        VanillaNpcHitboxSize hitbox = new(44, 44);
        VanillaNpcTargetCandidate target = Target(200f, 120f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.BloodSquid,
            in npc,
            in hitbox,
            in target,
            1f,
            2f,
            new FixedEnvironment(false),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(50f, result.LocalAi.Ai0);
        Assert.False(result.ProjectileReady);
        Assert.Equal(1f, result.VelocityX);
        Assert.Equal(2f, result.VelocityY);
    }

    [Fact]
    public void Blood_squid_dead_target_does_not_advance_local_timer()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.BloodSquid, localAi0: 77f);
        VanillaNpcHitboxSize hitbox = new(44, 44);
        VanillaNpcTargetCandidate target = Target(200f, 120f) with { Dead = true };

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.BloodSquid,
            in npc,
            in hitbox,
            in target,
            1f,
            2f,
            new FixedEnvironment(true),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(77f, result.LocalAi.Ai0);
        Assert.False(result.ProjectileReady);
    }

    [Fact]
    public void Targeting_stepper_plans_classic_probe_laser_from_committed_local_state()
    {
        var stepper = new VanillaNpcTargetingAiStepper(
            new PassthroughStepper(),
            random: new SequenceRandom());
        stepper.SetProjectileEnvironment(new FixedEnvironment(true));
        stepper.SetCandidates([Target(300f, 100f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.Probe, localAi0: 119f);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        int count = stepper.PlanProjectileSpawns(in npc, in next, intents);

        Assert.Equal(1, count);
        Assert.Equal(VanillaProjectileIds.ProbePinkLaser, intents[0].Type);
        Assert.Equal(25, intents[0].Damage);
        Assert.Equal((30f * 0.5f), intents[0].PositionX);
        Assert.InRange(MathF.Sqrt(intents[0].VelocityX * intents[0].VelocityX + intents[0].VelocityY * intents[0].VelocityY), 5.999f, 6.001f);
        Assert.Equal(0f, next.Simulation.LocalAi.Ai0);
    }

    [Fact]
    public void Targeting_stepper_plans_deterministic_blood_shot_and_recoil()
    {
        var random = new SequenceRandom(0, 0);
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: random);
        stepper.SetProjectileEnvironment(new FixedEnvironment(true));
        stepper.SetCandidates([Target(200f, 120f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.BloodSquid, localAi0: 119f);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        int count = stepper.PlanProjectileSpawns(in npc, in next, intents);

        Assert.Equal(1, count);
        Assert.Equal(VanillaProjectileIds.BloodShot, intents[0].Type);
        Assert.Equal(35, intents[0].Damage);
        Assert.Equal(1f, intents[0].KnockBack);
        Assert.Equal(0f, next.Simulation.LocalAi.Ai0);
        Assert.InRange(MathF.Sqrt(intents[0].VelocityX * intents[0].VelocityX + intents[0].VelocityY * intents[0].VelocityY), 14.999f, 15.001f);
        Assert.Equal(2, random.CallCount);
    }

    private static NpcSnapshot CreateNpc(NpcTypeId type, float localAi0)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, new NpcNetId(checked((short)type.Value)), out VanillaNpcDefinition definition));
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            Life = definition.LifeMax,
            LifeMax = definition.LifeMax,
            TimeLeft = 750,
            LocalAi = new NpcAiState(localAi0, 0f, 0f, 0f)
        };
        return new NpcSnapshot(
            new NpcHandle(0, new NpcGeneration(1)),
            new NpcRevision(1),
            type.Value,
            checked((short)type.Value),
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: default,
            simulation);
    }

    private static VanillaNpcTargetCandidate Target(float centerX, float centerY) =>
        new(0, centerX, centerY, Aggro: 0, Active: true, Dead: false, Ghost: false, NoAggro: false);

    private sealed class FixedEnvironment(bool canHit) : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(
            float sourcePositionX,
            float sourcePositionY,
            int sourceWidth,
            int sourceHeight,
            float targetPositionX,
            float targetPositionY,
            int targetWidth,
            int targetHeight) => canHit;
    }

    private sealed class SequenceRandom(params int[] values) : IVanillaNpcRandom
    {
        private int index;
        public int CallCount => index;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.Equal(-100, inclusiveMin);
            Assert.Equal(101, exclusiveMax);
            int value = index < values.Length ? values[index] : 0;
            index++;
            return value;
        }
    }

    private sealed class PassthroughStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            return true;
        }
    }
}
''')

write("tests/TerraRuntime.Tests/RuntimeNpcAiProjectileIntentTests.cs", r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcAiProjectileIntentTests
{
    [Fact]
    public void Projectile_intent_is_allocated_only_after_source_commit()
    {
        var npcs = new RuntimeNpcStore(capacity: 2);
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        NpcStateUpdate initial = CreateNpcUpdate(type: 1, positionX: 10f);
        Assert.True(npcs.TrySpawn(0, in initial, out NpcSnapshot source));
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);

        NpcAiStateTickSummary summary = executor.Tick(new ProjectilePlanningStepper());

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(11f, committed.PositionX);
        Assert.Equal(1, projectiles.ActiveCount);
        var snapshots = new ProjectileSnapshot[1];
        Assert.Equal(1, projectiles.CopyActive(snapshots));
        Assert.Equal(VanillaProjectileIds.ProbePinkLaser, snapshots[0].Type);
        Assert.Equal(byte.MaxValue, snapshots[0].Spawner);
        Assert.Equal((short)25, snapshots[0].Damage);
        Assert.Equal((short)25, snapshots[0].OriginalDamage);
    }

    [Fact]
    public void Stale_source_commit_cannot_publish_ghost_projectile()
    {
        var npcs = new RuntimeNpcStore(capacity: 2);
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        NpcStateUpdate initial = CreateNpcUpdate(type: 1, positionX: 10f);
        Assert.True(npcs.TrySpawn(0, in initial, out NpcSnapshot source));
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var stepper = new ReplacingProjectilePlanningStepper(npcs);

        NpcAiStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 0, 1), summary);
        Assert.Equal(0, projectiles.ActiveCount);
        Assert.False(npcs.TryGet(source.Handle, out _));
        Assert.True(npcs.TryGetActive(0, out NpcSnapshot replacement));
        Assert.Equal(99, replacement.Type);
    }

    [Fact]
    public void Intent_applier_rejects_invalid_damage_without_allocating()
    {
        var projectiles = new RuntimeProjectileStore(capacity: 2);
        var intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.ProbePinkLaser,
            0f,
            0f,
            1f,
            0f,
            short.MaxValue + 1,
            0f);

        Assert.False(RuntimeNpcProjectileIntentApplier.TryApply(projectiles, in intent, out _));
        Assert.Equal(0, projectiles.ActiveCount);
    }

    private static NpcStateUpdate CreateNpcUpdate(int type, float positionX) =>
        new(
            type,
            checked((short)type),
            positionX,
            20f,
            0f,
            0f,
            Target: 0,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

    private class ProjectilePlanningStepper : INpcAiStateStepper, INpcAiProjectileIntentPlanner
    {
        public virtual bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX + 1f,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            return true;
        }

        public virtual int PlanProjectileSpawns(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            Span<NpcAiProjectileIntent> destination)
        {
            destination[0] = new NpcAiProjectileIntent(
                VanillaProjectileIds.ProbePinkLaser,
                source.PositionX,
                source.PositionY,
                6f,
                0f,
                25,
                0f);
            return 1;
        }
    }

    private sealed class ReplacingProjectilePlanningStepper(RuntimeNpcStore npcs) : ProjectilePlanningStepper
    {
        public override int PlanProjectileSpawns(
            in NpcSnapshot source,
            in NpcStateUpdate proposed,
            Span<NpcAiProjectileIntent> destination)
        {
            Assert.True(npcs.TryDespawn(source.Handle));
            NpcStateUpdate replacement = CreateNpcUpdate(99, 500f);
            Assert.True(npcs.TrySpawn(source.Handle.Slot, in replacement, out _));
            return base.PlanProjectileSpawns(in source, in proposed, destination);
        }
    }
}
''')

# Roadmap remains explicit about what is still blocked by missing authoritative inputs/catalog entries.
replace_once(
    "docs/roadmap/npc-ai-parity.md",
    "- [ ] AI_005 projectile/NPC side effects: Hornet/Moss Hornet stingers, Probe laser, Blood Squid blood shot and Good World Eater spawn branch;",
    "- [x] ordinary AI_005 Probe laser and Blood Squid blood-shot/recoil side effects through generation-safe post-commit projectile intents;\n- [ ] remaining AI_005 side effects: Hornet/Moss Hornet stingers require authoritative player stealth/item-animation state; Good World Eater spawn requires admitted NPC 666 defaults/lifecycle;")

for path, paragraph in [
    ("docs/en/npc-behavior-families.md", """
### AI_005 projectile side-effect boundary

Ordinary Probe and Blood Squid attacks now keep their source-backed `localAI[0]` cadence in the NPC simulation revision and stage projectile creation through `INpcAiProjectileIntentPlanner`. The executor allocates projectile slots only after the exact source NPC generation commits, so a stale or rejected AI transition cannot emit a ghost laser/blood shot. Production LOS uses the same source-backed tile `Collision.CanHit` adapter as other NPC world queries, and the global firing gate pins TerrariaServer 1.4.5.8 `Main.MaxWorldViewSize` at 1920x1200 with the source 50-pixel inset. Hornet stingers and the Good World Eater child remain deliberately outside this claim until their missing authoritative player state / NPC 666 definition is admitted.
"""),
    ("docs/ru/npc-behavior-families.md", """
### Граница projectile-side-effects AI_005

Обычные атаки Probe и Blood Squid теперь хранят source-backed таймер `localAI[0]` в той же ревизии симуляции NPC, а создание снарядов планируется через `INpcAiProjectileIntentPlanner`. Executor выделяет projectile-слот только после успешного коммита точного поколения исходного NPC, поэтому stale/rejected AI-transition не может породить призрачный лазер или blood shot. Production LOS использует тот же source-backed адаптер tile `Collision.CanHit`, что и остальные world-запросы NPC, а глобальный firing gate фиксирует TerrariaServer 1.4.5.8 `Main.MaxWorldViewSize` 1920x1200 и исходный отступ 50 пикселей. Stinger-атаки Hornet и Good World Eater child намеренно не входят в этот claim до появления недостающего authoritative player-state / определения NPC 666.
""")
]:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if "AI_005 projectile side-effect boundary" in text or "Граница projectile-side-effects AI_005" in text:
        raise SystemExit(f"paragraph already exists in {path}")
    p.write_text(text.rstrip() + "\n\n" + paragraph.strip() + "\n", encoding="utf-8")
