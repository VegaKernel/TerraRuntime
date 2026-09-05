using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

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
            !VanillaNpcDefinitionCatalog.TryGet(npcType, npc.NetIdentity, out VanillaNpcDefinition definition) ||
            definition.AiStyle != VanillaNpcAiStyles.DemonEye)
        {
            next = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        int lifeMax = simulation.LifeMax > 0 ? simulation.LifeMax : definition.LifeMax;
        int life = simulation.LifeMax > 0 ? simulation.Life : definition.LifeMax;
        if (!VanillaFlyingEyeNpcCatalog.TryGetMotionProfile(
                npcType,
                life,
                lifeMax,
                out VanillaFlyingEyeMotionProfile profile))
        {
            next = default;
            return false;
        }

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

        if (!VanillaDemonEyeMotion.TryStep(in input, in profile, out VanillaDemonEyeMotionResult result))
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
