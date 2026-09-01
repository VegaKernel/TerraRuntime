using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Final ordinary-NPC lifecycle layer after AI and world movement. For explicitly admitted AI_003 fighters,
/// vanilla CheckActive observes the final position, may reset/decrement timeLeft, and can request despawn.
/// A requested despawn is represented by TimeLeft=0 so ServerRuntimeState can remove the exact generation
/// after the state executor successfully commits this tick.
/// </summary>
internal sealed class VanillaNpcCheckActiveAiStepper : INpcAiStateStepper, INpcAiStateStepperWrapper
{
    private readonly INpcAiStateStepper inner;
    private readonly VanillaNpcTargetCandidate[] candidates =
        new VanillaNpcTargetCandidate[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private int candidateCount;

    public VanillaNpcCheckActiveAiStepper(INpcAiStateStepper inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public INpcAiStateStepper InnerStepper => inner;

    public void SetCandidates(ReadOnlySpan<VanillaNpcTargetCandidate> players)
    {
        if (players.Length > candidates.Length)
            throw new ArgumentException("Too many vanilla player candidates.", nameof(players));

        players.CopyTo(candidates);
        candidateCount = players.Length;
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!inner.TryStepState(in npc, out next))
            return false;

        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcDefinitionCatalog.TryGet(npcType, npc.NetIdentity, out VanillaNpcDefinition definition) ||
            definition.AiStyle != VanillaNpcAiStyles.Fighter ||
            definition.BehaviorFamily != VanillaNpcBehaviorFamily.GroundFighter ||
            next.Simulation.TimeLeft < 0)
        {
            return true;
        }

        if (!VanillaZombieCheckActive.TryStep(
                next.PositionX,
                next.PositionY,
                definition.Width,
                definition.Height,
                next.Simulation.TimeLeft,
                candidates.AsSpan(0, candidateCount),
                out VanillaZombieCheckActiveResult lifetime))
        {
            return true;
        }

        next = next with
        {
            Simulation = next.Simulation with
            {
                TimeLeft = lifetime.ShouldDespawn ? 0 : lifetime.TimeLeft
            }
        };
        return true;
    }
}
