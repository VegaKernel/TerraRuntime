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
        if (definition.Type == VanillaNpcIds.Harpy)
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
                    simulation with { Life = transformedLife, LifeMax = 750, HitboxOverride = null, BaseDamage = null, BaseDefense = null,
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

    public NpcSnapshot CompleteHarpyAttackTimer(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity != VanillaNpcIds.Harpy || committed.TypeIdentity != VanillaNpcIds.Harpy ||
            IsHarpyShotTick(committed.Ai.Ai0) || committed.Target >= byte.MaxValue ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost ||
            !VanillaBatNpcCatalog1458.TryGetDefinition(VanillaNpcIds.Harpy, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(committed.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return committed;
        }

        float centerX = committed.PositionX + hitbox.Width * .5f;
        float centerY = committed.PositionY + hitbox.Height * .5f;
        if (!VanillaNpcGlobalFiringDistance.Contains(centerX, centerY, target.CenterX, target.CenterY))
            return committed;

        // Source evaluates Main.rand.Next(400) on every non-shot tick within the global firing rectangle.
        if (committed.Ai.Ai0 < 400f + random.NextInt32(0, 400))
            return committed;

        return mutations.TryUpdateAi(in committed, committed.Ai with { Ai0 = 0f }, out NpcSnapshot completed)
            ? completed
            : committed;
    }

    public void SpawnHarpyFeather(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity != VanillaNpcIds.Harpy || committed.TypeIdentity != VanillaNpcIds.Harpy ||
            !IsHarpyShotTick(committed.Ai.Ai0) || committed.Target >= byte.MaxValue ||
            environment is null ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost ||
            !VanillaBatNpcCatalog1458.TryGetDefinition(VanillaNpcIds.Harpy, out VanillaNpcDefinition definition) ||
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
        float velocityX = target.CenterX - centerX + random.NextInt32(-100, 101);
        float velocityY = target.CenterY - centerY + random.NextInt32(-100, 101);
        float length = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
        if (!(length > 0f) || !float.IsFinite(length))
            return;

        const float projectileSpeed = 6f;
        velocityX = velocityX / length * projectileSpeed;
        velocityY = velocityY / length * projectileSpeed;
        var intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.HarpyFeather,
            centerX - 7f,
            centerY - 7f,
            velocityX,
            velocityY,
            Damage: 15,
            KnockBack: 0f)
        {
            TimeLeftOverride = 300
        };
        mutations.TrySpawnProjectile(in committed, in intent, out _);
    }

    private static bool IsHarpyShotTick(float timer) => timer is 30f or 60f or 90f;

    private static int ScaleTransformLife(int life, int lifeMax, int transformedLifeMax) =>
        lifeMax > 0 ? Math.Max(1, (int)((long)life * transformedLifeMax / lifeMax)) : transformedLifeMax;
}
