using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Adds the verified vanilla player-target selection/facing pre-pass to a state-only NPC AI step.
/// Candidates must be supplied in player-slot order. Target selection and the wrapped AI transition
/// are committed as one NpcStateUpdate, so one simulation tick advances the NPC revision only once.
/// </summary>
public sealed class VanillaNpcTargetingAiStepper : INpcAiStateStepper
{
    public const int MaximumPlayerCandidates = byte.MaxValue;

    private readonly INpcAiStateStepper _inner;
    private readonly VanillaNpcTargetCandidate[] _candidates = new VanillaNpcTargetCandidate[MaximumPlayerCandidates];
    private int _candidateCount;

    public VanillaNpcTargetingAiStepper(INpcAiStateStepper inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public void SetCandidates(ReadOnlySpan<VanillaNpcTargetCandidate> candidates)
    {
        if (candidates.Length > _candidates.Length)
            throw new ArgumentException("Too many vanilla player target candidates.", nameof(candidates));

        candidates.CopyTo(_candidates);
        _candidateCount = candidates.Length;
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        NpcSnapshot targeted = npc;
        if (_candidateCount != 0 &&
            VanillaNpcDefinitionCatalog.TryGet(npc.Type, out VanillaNpcDefinition definition))
        {
            float npcCenterX = npc.PositionX + definition.Width * 0.5f;
            float npcCenterY = npc.PositionY + definition.Height * 0.5f;
            ReadOnlySpan<VanillaNpcTargetCandidate> candidates = _candidates.AsSpan(0, _candidateCount);

            if (VanillaNpcTargeting.TrySelectClosestPlayerTarget(
                    npcCenterX,
                    npcCenterY,
                    npc.Simulation.DirectionX,
                    candidates,
                    out VanillaNpcTargetSelection selection) &&
                TryFindCandidate(selection.PlayerSlot, candidates, out VanillaNpcTargetCandidate candidate))
            {
                int directionX = candidate.CenterX < npcCenterX ? -1 : 1;
                int directionY = candidate.CenterY < npcCenterY ? -1 : 1;
                targeted = npc with
                {
                    Target = selection.PlayerSlot,
                    Simulation = npc.Simulation with
                    {
                        DirectionX = directionX,
                        DirectionY = directionY
                    }
                };
            }
        }

        return _inner.TryStepState(in targeted, out next);
    }

    private static bool TryFindCandidate(
        byte slot,
        ReadOnlySpan<VanillaNpcTargetCandidate> candidates,
        out VanillaNpcTargetCandidate candidate)
    {
        foreach (VanillaNpcTargetCandidate current in candidates)
        {
            if (current.Slot != slot)
                continue;

            candidate = current;
            return true;
        }

        candidate = default;
        return false;
    }
}
