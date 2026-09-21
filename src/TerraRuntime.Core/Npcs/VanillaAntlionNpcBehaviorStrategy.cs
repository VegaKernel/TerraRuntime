using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>AI_019 routing for the source-verified Antlion identity.</summary>
internal sealed class VanillaAntlionNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private IVanillaAntlionEnvironment? environment;

    public void SetEnvironment(IVanillaAntlionEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.Antlion ||
            !VanillaAntlionNpcCatalog1458.TryGetDefinition(definition.Type, out _) ||
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
        VanillaNpcTargetCandidate target = default;
        bool hasTarget = closest.HasTarget &&
            context.TryFindCandidate(checked((byte)closest.Target), out target) &&
            target.Active && !target.Dead && !target.Ghost;

        NpcSimulationState simulation = npc.Simulation;
        int directionY = hasTarget ? closest.DirectionY : simulation.DirectionY;
        float targetCenterX = hasTarget ? target.CenterX : 0f;
        float targetTopY = hasTarget ? target.CenterY - target.Height * .5f : 0f;
        bool playerShotReady = hasTarget &&
            VanillaNpcGlobalFiringDistance.Contains(
                npc.PositionX + hitbox.Width * .5f,
                npc.PositionY + hitbox.Height * .5f,
                target.CenterX,
                target.CenterY) &&
            environment.CanHit(
                npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height,
                target.CenterX - target.Width * .5f,
                targetTopY, (int)target.Width, (int)target.Height);

        var input = new VanillaAntlionMotionInput1458(
            npc.VelocityX, npc.VelocityY, simulation.Rotation ?? 0f, npc.Ai.Ai0,
            npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height, directionY,
            hasTarget, targetCenterX, targetTopY, playerShotReady,
            environment.HasConveyorBelow(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height),
            environment.HasSolidFloor(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height));
        if (!VanillaAntlionMotion1458.TryStep(in input, out VanillaAntlionMotionResult1458 result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, result.VelocityX, result.VelocityY,
            hasTarget ? closest.Target : npc.Target,
            npc.Ai with { Ai0 = result.Ai0 },
            simulation with
            {
                DirectionX = hasTarget ? closest.DirectionX : simulation.DirectionX,
                DirectionY = directionY,
                Rotation = result.Rotation,
                NoGravity = result.NoGravity,
                NoTileCollide = result.NoTileCollide
            });
        return true;
    }
}
