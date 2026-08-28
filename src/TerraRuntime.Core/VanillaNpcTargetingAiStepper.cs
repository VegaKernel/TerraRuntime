using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Coordinates the verified player-target selection cadence with state-only NPC AI. Demon Eye refreshes
/// its target every ordinary style-2 tick; Blue Slime refreshes only at the exact AI_001 state-machine
/// points represented by VanillaBlueSlimeMotion. All state remains one NpcStateUpdate per simulation tick.
/// </summary>
public sealed class VanillaNpcTargetingAiStepper : INpcAiStateStepper
{
    public const int MaximumPlayerCandidates = byte.MaxValue;

    private readonly INpcAiStateStepper _inner;
    private readonly VanillaNpcTargetCandidate[] _candidates = new VanillaNpcTargetCandidate[MaximumPlayerCandidates];
    private int _candidateCount;
    private bool _blueSlimeMotionEnabled;

    public VanillaNpcTargetingAiStepper(INpcAiStateStepper inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>
    /// Enables the verified undamaged daytime/surface Blue Slime movement baseline. The owning world-motion
    /// layer calls this only when tile collision and gravity are available. Night/underground/damaged engagement
    /// acceleration is enabled separately once authoritative world-time and NPC life state are wired.
    /// </summary>
    public void EnableBlueSlimeMotion() => _blueSlimeMotionEnabled = true;

    public void SetCandidates(ReadOnlySpan<VanillaNpcTargetCandidate> candidates)
    {
        if (candidates.Length > _candidates.Length)
            throw new ArgumentException("Too many vanilla player target candidates.", nameof(candidates));

        candidates.CopyTo(_candidates);
        _candidateCount = candidates.Length;
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (npc.Type == 1 && _blueSlimeMotionEnabled)
            return TryStepBlueSlime(in npc, out next);

        // The verified ordinary style-2 path calls TargetClosest every tick. Do not apply this policy
        // globally: other vanilla AI styles, including slimes, have their own retarget cadence.
        NpcSnapshot targeted = npc;
        if (npc.Type == 2 && TrySelectClosestTarget(in npc, out VanillaBlueSlimeTargetRefresh closest))
        {
            targeted = npc with
            {
                Target = closest.Target,
                Simulation = npc.Simulation with
                {
                    DirectionX = closest.DirectionX,
                    DirectionY = closest.DirectionY
                }
            };
        }

        return _inner.TryStepState(in targeted, out next);
    }

    private bool TryStepBlueSlime(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.Type, out VanillaNpcDefinition definition) ||
            definition.AiStyle != 1)
        {
            next = default;
            return false;
        }

        VanillaBlueSlimeTargetRefresh closest = TrySelectClosestTarget(in npc, out VanillaBlueSlimeTargetRefresh selected)
            ? selected
            : default;
        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaBlueSlimeMotionInput(
            PositionX: npc.PositionX,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            OldVelocityY: simulation.OldVelocityY,
            DirectionX: simulation.DirectionX,
            DirectionY: simulation.DirectionY,
            Target: npc.Target,
            Ai: npc.Ai,
            Wet: simulation.Wet,
            CollideX: simulation.CollideX,
            CollideY: simulation.CollideY,
            Engaged: false,
            SolidCollision: simulation.SolidCollision,
            ClosestTarget: closest);

        if (!VanillaBlueSlimeMotion.TryStep(in input, out VanillaBlueSlimeMotionResult result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            npc.Type,
            npc.NetId,
            result.PositionX,
            npc.PositionY,
            result.VelocityX,
            result.VelocityY,
            result.Target,
            result.Ai,
            simulation with
            {
                DirectionX = result.DirectionX,
                DirectionY = result.DirectionY,
                NoGravity = false
            });
        return true;
    }

    private bool TrySelectClosestTarget(
        in NpcSnapshot npc,
        out VanillaBlueSlimeTargetRefresh target)
    {
        target = default;
        if (_candidateCount == 0 ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.Type, out VanillaNpcDefinition definition))
        {
            return false;
        }

        float npcCenterX = npc.PositionX + definition.Width * 0.5f;
        float npcCenterY = npc.PositionY + definition.Height * 0.5f;
        ReadOnlySpan<VanillaNpcTargetCandidate> candidates = _candidates.AsSpan(0, _candidateCount);
        if (!VanillaNpcTargeting.TrySelectClosestPlayerTarget(
                npcCenterX,
                npcCenterY,
                npc.Simulation.DirectionX,
                candidates,
                out VanillaNpcTargetSelection selection) ||
            !TryFindCandidate(selection.PlayerSlot, candidates, out VanillaNpcTargetCandidate candidate))
        {
            return false;
        }

        target = new VanillaBlueSlimeTargetRefresh(
            HasTarget: true,
            Target: selection.PlayerSlot,
            DirectionX: candidate.CenterX < npcCenterX ? -1 : 1,
            DirectionY: candidate.CenterY < npcCenterY ? -1 : 1);
        return true;
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
