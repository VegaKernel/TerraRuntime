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
}
