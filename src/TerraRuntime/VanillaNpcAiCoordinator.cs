using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Server-side orchestration for the verified vanilla NPC AI slice. It owns per-tick player target
/// candidates and dispatches style-specific targeting cadence instead of forcing one global retarget policy.
/// </summary>
internal sealed class VanillaNpcAiCoordinator : INpcAiStateStepper
{
    public const int MaximumPlayerCandidates = byte.MaxValue;

    private readonly VanillaDemonEyeAiStepper demonEye = new();
    private readonly VanillaNpcTargetCandidate[] candidates = new VanillaNpcTargetCandidate[MaximumPlayerCandidates];
    private readonly WorldTileStore? tiles;
    private readonly bool dayTime;
    private readonly bool slimeRain;
    private readonly double worldSurfaceTiles;
    private int candidateCount;

    public VanillaNpcAiCoordinator(
        WorldTileStore? tiles = null,
        bool dayTime = true,
        bool slimeRain = false,
        double worldSurfaceTiles = 250d)
    {
        if (!double.IsFinite(worldSurfaceTiles) || worldSurfaceTiles <= 0d)
            throw new ArgumentOutOfRangeException(nameof(worldSurfaceTiles));

        this.tiles = tiles;
        this.dayTime = dayTime;
        this.slimeRain = slimeRain;
        this.worldSurfaceTiles = worldSurfaceTiles;
    }

    public void SetCandidates(ReadOnlySpan<VanillaNpcTargetCandidate> source)
    {
        if (source.Length > candidates.Length)
            throw new ArgumentException("Too many vanilla player target candidates.", nameof(source));

        source.CopyTo(candidates);
        candidateCount = source.Length;
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) =>
        npc.Type switch
        {
            1 => TryStepBlueSlime(in npc, out next),
            2 => TryStepDemonEye(in npc, out next),
            _ => NoProposal(out next)
        };

    private bool TryStepDemonEye(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        NpcSnapshot targeted = npc;
        if (TrySelectClosestTarget(in npc, out VanillaBlueSlimeTargetRefresh closest))
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

        return demonEye.TryStepState(in targeted, out next);
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
        bool solidCollision = tiles is not null &&
            VanillaWorldSolidCollision.Intersects(
                tiles,
                npc.PositionX,
                npc.PositionY,
                definition.Width,
                definition.Height);

        // Vanilla also engages a damaged surface slime during daytime. NPC combat/life state is not yet
        // part of the runtime snapshot, and the current supported lifecycle cannot damage NPCs, so the
        // undamaged branch is exact for the live state TerraRuntime can presently produce.
        bool engaged = !dayTime ||
            npc.PositionY > worldSurfaceTiles * 16d ||
            slimeRain;

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
            Engaged: engaged,
            SolidCollision: solidCollision,
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
        if (candidateCount == 0 ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.Type, out VanillaNpcDefinition definition))
        {
            return false;
        }

        float npcCenterX = npc.PositionX + definition.Width * 0.5f;
        float npcCenterY = npc.PositionY + definition.Height * 0.5f;
        ReadOnlySpan<VanillaNpcTargetCandidate> current = candidates.AsSpan(0, candidateCount);
        if (!VanillaNpcTargeting.TrySelectClosestPlayerTarget(
                npcCenterX,
                npcCenterY,
                npc.Simulation.DirectionX,
                current,
                out VanillaNpcTargetSelection selection))
        {
            return false;
        }

        foreach (VanillaNpcTargetCandidate candidate in current)
        {
            if (candidate.Slot != selection.PlayerSlot)
                continue;

            target = new VanillaBlueSlimeTargetRefresh(
                HasTarget: true,
                Target: selection.PlayerSlot,
                DirectionX: candidate.CenterX < npcCenterX ? -1 : 1,
                DirectionY: candidate.CenterY < npcCenterY ? -1 : 1);
            return true;
        }

        return false;
    }

    private static bool NoProposal(out NpcStateUpdate next)
    {
        next = default;
        return false;
    }
}
