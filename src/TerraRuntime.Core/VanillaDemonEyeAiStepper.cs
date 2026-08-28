using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// State-only type-2 AI adapter for the verified ordinary Demon Eye path. Targeting, collision detection
/// and wet-state production happen before this step and are represented in the immutable NPC snapshot.
/// Cosmetic dust remains outside the authoritative state transition.
/// </summary>
public sealed class VanillaDemonEyeAiStepper : INpcAiStateStepper
{
    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (npc.Type != 2 ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.Type, out VanillaNpcDefinition definition) ||
            definition.AiStyle != 2)
        {
            next = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaDemonEyeMotionInput(
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            OldVelocityX: simulation.OldVelocityX,
            OldVelocityY: simulation.OldVelocityY,
            DirectionX: simulation.DirectionX,
            DirectionY: simulation.DirectionY,
            Scale: simulation.Scale,
            NoTileCollide: simulation.NoTileCollide,
            CollideX: simulation.CollideX,
            CollideY: simulation.CollideY,
            Wet: simulation.Wet);

        if (!VanillaDemonEyeMotion.TryStep(in input, out VanillaDemonEyeMotionResult result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            result.VelocityX,
            result.VelocityY,
            npc.Target,
            npc.Ai,
            simulation with { NoGravity = result.NoGravity });
        return true;
    }
}
