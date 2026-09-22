using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>AI_018 routing for the one source-verified hostile Jellyfish identity.</summary>
internal sealed class VanillaJellyfishNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private IVanillaFishEnvironment1458? environment;

    public void SetEnvironment(IVanillaFishEnvironment1458 value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.Jellyfish ||
            !VanillaJellyfishNpcCatalog1458.TryGetDefinition(definition.Type, out _) ||
            !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox) ||
            environment is null)
        {
            next = default;
            return false;
        }

        VanillaBlueSlimeTargetRefresh closest =
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh selected)
                ? selected
                : default;
        bool closestWetAndVisible = false;
        float closestCenterX = 0f;
        float closestCenterY = 0f;
        if (closest.HasTarget &&
            context.TryFindCandidate(checked((byte)closest.Target), out VanillaNpcTargetCandidate target) &&
            target.Wet && !target.Dead &&
            environment.CanHit(
                npc.PositionX,
                npc.PositionY,
                hitbox.Width,
                hitbox.Height,
                target.CenterX - target.Width * .5f,
                target.CenterY - target.Height * .5f,
                (int)target.Width,
                (int)target.Height))
        {
            closestWetAndVisible = true;
            closestCenterX = target.CenterX;
            closestCenterY = target.CenterY;
        }

        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaJellyfishMotionInput1458(
            npc.VelocityX, npc.VelocityY, npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height,
            simulation.DirectionX, simulation.DirectionY, npc.Target, npc.Ai, simulation.Wet,
            simulation.CollideX, simulation.CollideY, simulation.Friendly == true,
            environment.GetBottomSlope(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height),
            environment.HasDeepLiquidAboveAndActiveTileBelow(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height),
            closest, closestWetAndVisible, closestCenterX, closestCenterY);
        if (!VanillaJellyfishMotion1458.TryStep(in input, out VanillaJellyfishMotionResult1458 result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, result.VelocityX, result.VelocityY,
            result.Target, result.Ai,
            simulation with
            {
                DirectionX = result.DirectionX,
                DirectionY = result.DirectionY,
                NoGravity = true,
                NoTileCollide = false
            });
        return true;
    }
}
