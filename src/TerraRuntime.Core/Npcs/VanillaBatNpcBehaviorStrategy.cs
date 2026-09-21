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

    private static int ScaleTransformLife(int life, int lifeMax, int transformedLifeMax) =>
        lifeMax > 0 ? Math.Max(1, (int)((long)life * transformedLifeMax / lifeMax)) : transformedLifeMax;
}
