using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Npcs;

internal sealed class VanillaBatNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private IVanillaNpcProjectileEnvironment? environment;

    public void SetEnvironment(IVanillaNpcProjectileEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Bat ||
            !VanillaBatNpcCatalog1458.IsSupportedMotionType(definition.Type) ||
            !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        VanillaBlueSlimeTargetRefresh closest =
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh selected)
                ? selected
                : default;
        bool targetDryAndVisible = false;
        if (closest.HasTarget &&
            context.TryFindCandidate(checked((byte)closest.Target), out VanillaNpcTargetCandidate target) &&
            !target.Wet &&
            environment is not null)
        {
            targetDryAndVisible = environment.CanHit(
                npc.PositionX,
                npc.PositionY,
                hitbox.Width,
                hitbox.Height,
                target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f,
                target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f,
                (int)VanillaPlayerHitboxFacts.BaseWidth,
                (int)VanillaPlayerHitboxFacts.BaseHeight);
        }

        if (definition.Type == VanillaNpcIds.FlyingSnake && closest.HasTarget && environment is not null &&
            context.TryFindCandidate(checked((byte)closest.Target), out VanillaNpcTargetCandidate snakeTarget) &&
            !environment.CanHit(
                npc.PositionX,
                npc.PositionY,
                hitbox.Width,
                hitbox.Height,
                snakeTarget.CenterX - snakeTarget.Width * .5f,
                snakeTarget.CenterY - snakeTarget.Height * .5f,
                (int)snakeTarget.Width,
                (int)snakeTarget.Height))
        {
            // Type 226 first calls TargetClosest, then restores pursuit from its velocity signs when sight is blocked.
            closest = closest with
            {
                DirectionX = npc.VelocityX < 0f ? -1 : 1,
                DirectionY = npc.VelocityY < 0f ? -1 : 1
            };
        }

        if (definition.Type == VanillaNpcIds.Vampire && closest.HasTarget &&
            npc.PositionY < context.WorldSurfacePixels && context.DayTime && !context.EclipseActive)
        {
            // AI_014 reverses the horizontal target direction and climbs while a Flying Vampire is exposed at day.
            closest = closest with { DirectionX = -closest.DirectionX, DirectionY = -1 };
        }

        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaBatMotionInput1458(
            npc.VelocityX,
            npc.VelocityY,
            simulation.OldVelocityX,
            simulation.OldVelocityY,
            simulation.DirectionX,
            simulation.DirectionY,
            npc.Target,
            npc.Ai,
            simulation.Wet,
            simulation.CollideX,
            simulation.CollideY,
            closest,
            targetDryAndVisible);
        if (!VanillaBatMotion1458.TryStep(definition.Type, in input, out VanillaBatMotionResult1458 result))
        {
            next = default;
            return false;
        }

        // AI_014 advances Harpy's server-only firing timer after its flight and wander work. The timer reset
        // needs RNG, so it is completed only after this proposed state is accepted by the NPC store.
        if (IsBatShooter(definition.Type))
            result = result with { Ai = result.Ai with { Ai0 = result.Ai.Ai0 + 1f } };

        if (definition.Type == VanillaNpcIds.Vampire && closest.HasTarget &&
            context.TryFindCandidate((byte)closest.Target, out target) && environment is not null)
        {
            float dx = target.CenterX - (npc.PositionX + hitbox.Width * .5f);
            float dy = target.CenterY - (npc.PositionY + hitbox.Height * .5f);
            if (dx * dx + dy * dy < 40_000f &&
                npc.PositionY + hitbox.Height < target.CenterY + target.Height * .5f &&
                environment.CanHit(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height,
                    target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f, (int)target.Width, (int)target.Height))
            {
                int transformedLife = ScaleTransformLife(simulation.Life, simulation.LifeMax, 750);
                next = new NpcStateUpdate(VanillaNpcIds.VampireHumanoid.Value, (short)VanillaNpcIds.VampireHumanoid.Value,
                    npc.PositionX, npc.PositionY - 18f, result.VelocityX, result.VelocityY, closest.Target, default,
                    simulation with { Life = transformedLife, LifeMax = 750, HitboxOverride = null, BaseDamage = null, BaseDefense = null, BaseLifeMax = null,
                        DefenseOverride = null, DamageOverride = null, KnockBackResist = null, NoGravity = false, NoTileCollide = false,
                        DirectionX = target.CenterX < npc.PositionX + 9f ? -1 : 1,
                        DirectionY = target.CenterY < npc.PositionY + 2f ? -1 : 1,
                        LocalAi = default, FrameCounter = 0d, TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                        Alpha = 0, Hidden = false, DontTakeDamage = false, ReflectsProjectiles = false, JustHit = false,
                        CanBeReplacedByOtherNpcs = false, Wet = false, LiquidContact = NpcLiquidContactKind.None,
                        CollideX = false, CollideY = false, SpriteDirection = VanillaNpcDefinitionCatalog.DefaultSpriteDirection,
                        Rotation = null, Friendly = null, Chaseable = null, Immortal = null });
                return true;
            }
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            result.VelocityX,
            result.VelocityY,
            result.Target,
            result.Ai,
            simulation with
            {
                DirectionX = result.DirectionX,
                DirectionY = result.DirectionY,
                NoGravity = true,
                NoTileCollide = false
            });
        return true;
    }

    public NpcSnapshot CompleteBatShooterAttackTimer(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (!IsBatShooter(before.TypeIdentity) || committed.TypeIdentity != before.TypeIdentity ||
            IsBatShooterShotTick(before.TypeIdentity, committed.Ai.Ai0) || committed.Target >= byte.MaxValue ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost ||
            !VanillaBatNpcCatalog1458.TryGetDefinition(before.TypeIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(committed.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return committed;
        }

        float centerX = committed.PositionX + hitbox.Width * .5f;
        float centerY = committed.PositionY + hitbox.Height * .5f;
        if (!VanillaNpcGlobalFiringDistance.Contains(centerX, centerY, target.CenterX, target.CenterY))
            return committed;

        int resetBase = before.TypeIdentity == VanillaNpcIds.Harpy ? 400 :
            before.TypeIdentity == VanillaNpcIds.RedDevil ? 250 : 300;
        int resetRange = before.TypeIdentity == VanillaNpcIds.Harpy ? 400 :
            before.TypeIdentity == VanillaNpcIds.RedDevil ? 250 : 300;
        // Source evaluates the random reset threshold on every non-shot tick within the global firing rectangle.
        if (committed.Ai.Ai0 < resetBase + random.NextInt32(0, resetRange))
            return committed;

        return mutations.TryUpdateAi(in committed, committed.Ai with { Ai0 = 0f }, out NpcSnapshot completed)
            ? completed
            : committed;
    }

    public void SpawnBatShooterProjectile(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (!IsBatShooter(before.TypeIdentity) || committed.TypeIdentity != before.TypeIdentity ||
            !IsBatShooterShotTick(before.TypeIdentity, committed.Ai.Ai0) || committed.Target >= byte.MaxValue ||
            environment is null ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost ||
            !VanillaBatNpcCatalog1458.TryGetDefinition(before.TypeIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(committed.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return;
        }

        float targetPositionX = target.CenterX - target.Width * .5f;
        float targetPositionY = target.CenterY - target.Height * .5f;
        if (!environment.CanHit(
                committed.PositionX,
                committed.PositionY,
                hitbox.Width,
                hitbox.Height,
                targetPositionX,
                targetPositionY,
                (int)target.Width,
                (int)target.Height))
        {
            return;
        }

        float centerX = committed.PositionX + hitbox.Width * .5f;
        float centerY = committed.PositionY + hitbox.Height * .5f;
        int jitter = before.TypeIdentity == VanillaNpcIds.RedDevil ? 50 : 100;
        float velocityX = target.CenterX - centerX + random.NextInt32(-jitter, jitter + 1);
        float velocityY = target.CenterY - centerY + random.NextInt32(-jitter, jitter + 1);
        float length = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
        if (!(length > 0f) || !float.IsFinite(length))
            return;

        float projectileSpeed = before.TypeIdentity == VanillaNpcIds.Harpy ? 6f : .2f;
        velocityX = velocityX / length * projectileSpeed;
        velocityY = velocityY / length * projectileSpeed;
        ProjectileTypeId projectileType = before.TypeIdentity == VanillaNpcIds.Harpy
            ? VanillaProjectileIds.HarpyFeather
            : before.TypeIdentity == VanillaNpcIds.RedDevil
                ? VanillaProjectileIds.RedDevilSickle
                : VanillaProjectileIds.DemonScythe;
        float projectileHalfSize = before.TypeIdentity == VanillaNpcIds.Harpy ? 7f : 24f;
        int damage = before.TypeIdentity == VanillaNpcIds.Harpy ? 15 :
            before.TypeIdentity == VanillaNpcIds.RedDevil ? 80 : 21;
        float projectilePositionX = centerX - projectileHalfSize;
        float projectilePositionY = centerY - projectileHalfSize;
        if (before.TypeIdentity == VanillaNpcIds.RedDevil)
        {
            // AI_014 emits type 115 from the leading NPC center plus a 100px shot offset. NewProjectile receives
            // these top-left coordinates directly; it does not center the 16px projectile at this point.
            projectilePositionX = centerX + committed.VelocityX * 5f + velocityX * 100f;
            projectilePositionY = centerY + committed.VelocityY * 5f + velocityY * 100f;
        }
        var intent = new NpcAiProjectileIntent(
            projectileType,
            projectilePositionX,
            projectilePositionY,
            velocityX,
            velocityY,
            Damage: damage,
            KnockBack: 0f)
        {
            TimeLeftOverride = 300
        };
        mutations.TrySpawnProjectile(in committed, in intent, out _);
    }

    private static bool IsBatShooter(NpcTypeId type) =>
        type == VanillaNpcIds.Harpy || type == VanillaNpcIds.Demon || type == VanillaNpcIds.VoodooDemon ||
        type == VanillaNpcIds.RedDevil;

    private static bool IsBatShooterShotTick(NpcTypeId type, float timer) =>
        type == VanillaNpcIds.Harpy
            ? timer is 30f or 60f or 90f
            : type == VanillaNpcIds.RedDevil
                ? timer is 20f or 40f or 60f or 80f or 100f
                : timer is 20f or 40f or 60f or 80f;

    private static int ScaleTransformLife(int life, int lifeMax, int transformedLifeMax) =>
        lifeMax > 0 ? Math.Max(1, (int)((long)life * transformedLifeMax / lifeMax)) : transformedLifeMax;
}
