using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

internal sealed class VanillaVultureNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Vulture ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        bool currentTargetDead = true;
        if (npc.Target < byte.MaxValue &&
            context.TryFindCandidate((byte)npc.Target, out VanillaNpcTargetCandidate current))
        {
            currentTargetDead = !current.Active || current.Dead || current.Ghost;
        }

        VanillaBlueSlimeTargetRefresh closest =
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh selected)
                ? selected
                : default;
        float targetCenterX = 0f;
        float targetCenterY = 0f;
        if (closest.HasTarget &&
            context.TryFindCandidate((byte)closest.Target, out VanillaNpcTargetCandidate candidate))
        {
            targetCenterX = candidate.CenterX;
            targetCenterY = candidate.CenterY;
        }

        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaVultureMotionInput1458(
            npc.PositionX,
            npc.PositionY,
            npc.VelocityX,
            npc.VelocityY,
            simulation.OldVelocityX,
            simulation.OldVelocityY,
            hitbox.Width,
            hitbox.Height,
            simulation.DirectionX,
            simulation.DirectionY,
            npc.Target,
            npc.Ai,
            simulation.Wet,
            simulation.CollideX,
            simulation.CollideY,
            simulation.Life,
            simulation.LifeMax,
            currentTargetDead,
            closest,
            targetCenterX,
            targetCenterY);
        if (!VanillaVultureMotion1458.TryStep(in input, out VanillaVultureMotionResult1458 result))
        {
            next = default;
            return false;
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
                NoGravity = result.NoGravity,
                NoTileCollide = false
            });
        return true;
    }
}

internal sealed class VanillaSpikeBallNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom random;

    public VanillaSpikeBallNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        this.random = random ?? throw new ArgumentNullException(nameof(random));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.SpikeBall ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        VanillaBlueSlimeTargetRefresh closest =
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh selected)
                ? selected
                : default;
        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaSpikeBallMotionInput1458(
            npc.PositionX,
            npc.PositionY,
            npc.VelocityX,
            npc.VelocityY,
            hitbox.Width,
            hitbox.Height,
            simulation.DirectionX,
            simulation.DirectionY,
            npc.Target,
            npc.Ai,
            closest);
        if (!VanillaSpikeBallMotion1458.TryStep(in input, random, out VanillaSpikeBallMotionResult1458 result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            npc.PositionX,
            result.PositionY,
            result.VelocityX,
            result.VelocityY,
            result.Target,
            result.Ai,
            simulation with
            {
                DirectionX = result.DirectionX,
                DirectionY = result.DirectionY,
                NoGravity = true,
                NoTileCollide = true,
                DontTakeDamage = true
            });
        return true;
    }
}

internal sealed class VanillaBlazingWheelNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.BlazingWheel)
        {
            next = default;
            return false;
        }

        VanillaBlueSlimeTargetRefresh closest =
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh selected)
                ? selected
                : default;
        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaBlazingWheelMotionInput1458(
            npc.VelocityX,
            npc.VelocityY,
            simulation.DirectionX,
            simulation.DirectionY,
            npc.Target,
            npc.Ai,
            simulation.CollideX,
            simulation.CollideY,
            closest);
        if (!VanillaBlazingWheelMotion1458.TryStep(in input, out VanillaBlazingWheelMotionResult1458 result))
        {
            next = default;
            return false;
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
                NoTileCollide = false,
                DontTakeDamage = true
            });
        return true;
    }
}
