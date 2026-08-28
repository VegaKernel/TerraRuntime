using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// State-only Demon Eye AI adapter for the verified ordinary vanilla path. Targeting, collision detection
/// and wet-state production happen before this step and are represented in the immutable NPC snapshot.
/// Cosmetic dust remains outside the authoritative state transition.
/// </summary>
public sealed class VanillaDemonEyeAiStepper : INpcAiStateStepper
{
    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            npcType != VanillaNpcIds.DemonEye ||
            !VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition definition) ||
            definition.AiStyle != VanillaNpcAiStyles.DemonEye)
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
            npcType.Value,
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
