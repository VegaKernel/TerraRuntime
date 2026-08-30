using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Mutable per-world inputs shared by verified vanilla NPC behavior-family strategies. This object owns the
/// bounded player-candidate scratch buffer and world-condition facts so individual strategies remain focused on
/// behavior rather than orchestration or server-state discovery.
/// </summary>
internal sealed class VanillaNpcBehaviorContext
{
    public const int MaximumPlayerCandidates = byte.MaxValue;
    public const float BasePlayerWidth = 20f;
    public const float BasePlayerHeight = 42f;

    private readonly VanillaNpcTargetCandidate[] _candidates = new VanillaNpcTargetCandidate[MaximumPlayerCandidates];
    private int _candidateCount;

    public bool SlimeGroundEnabled { get; private set; }

    public bool GroundFighterEnabled { get; private set; }

    public double WorldSurfacePixels { get; private set; } = double.PositiveInfinity;

    public bool DayTime { get; private set; } = true;

    public bool SlimeRainActive { get; private set; }

    public void EnableSlimeGround(double worldSurfaceTiles)
    {
        ValidateWorldSurface(worldSurfaceTiles);
        SlimeGroundEnabled = true;
        WorldSurfacePixels = worldSurfaceTiles * 16d;
    }

    public void EnableGroundFighter(double worldSurfaceTiles)
    {
        ValidateWorldSurface(worldSurfaceTiles);
        GroundFighterEnabled = true;
        WorldSurfacePixels = worldSurfaceTiles * 16d;
    }

    public void SetWorldConditions(bool dayTime, bool slimeRainActive)
    {
        DayTime = dayTime;
        SlimeRainActive = slimeRainActive;
    }

    public void SetCandidates(ReadOnlySpan<VanillaNpcTargetCandidate> candidates)
    {
        if (candidates.Length > _candidates.Length)
            throw new ArgumentException("Too many vanilla player target candidates.", nameof(candidates));

        candidates.CopyTo(_candidates);
        _candidateCount = candidates.Length;
    }

    public bool TrySelectClosestTarget(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        out VanillaBlueSlimeTargetRefresh target)
    {
        target = default;
        if (_candidateCount == 0)
            return false;

        float npcCenterX = npc.PositionX + definition.Width * 0.5f;
        float npcCenterY = npc.PositionY + definition.Height * 0.5f;
        ReadOnlySpan<VanillaNpcTargetCandidate> candidates = _candidates.AsSpan(0, _candidateCount);
        if (!VanillaNpcTargeting.TrySelectClosestPlayerTarget(
                npcCenterX,
                npcCenterY,
                npc.Simulation.DirectionX,
                candidates,
                out VanillaNpcTargetSelection selection) ||
            !TryFindCandidate(selection.PlayerSlot, out VanillaNpcTargetCandidate candidate))
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

    public bool TargetOverlapsNpc(in NpcSnapshot npc, in VanillaNpcDefinition definition)
    {
        if (npc.Target >= byte.MaxValue ||
            !TryFindCandidate((byte)npc.Target, out VanillaNpcTargetCandidate candidate) ||
            !candidate.Active || candidate.Dead || candidate.Ghost)
        {
            return false;
        }

        float playerLeft = candidate.CenterX - BasePlayerWidth * 0.5f;
        float playerTop = candidate.CenterY - BasePlayerHeight * 0.5f;
        return npc.PositionX < playerLeft + BasePlayerWidth &&
               npc.PositionX + definition.Width > playerLeft &&
               npc.PositionY < playerTop + BasePlayerHeight &&
               npc.PositionY + definition.Height > playerTop;
    }

    public bool TryFindCandidate(byte slot, out VanillaNpcTargetCandidate candidate)
    {
        ReadOnlySpan<VanillaNpcTargetCandidate> candidates = _candidates.AsSpan(0, _candidateCount);
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

    private static void ValidateWorldSurface(double worldSurfaceTiles)
    {
        if (double.IsNaN(worldSurfaceTiles) ||
            worldSurfaceTiles <= 0d ||
            (double.IsInfinity(worldSurfaceTiles) && !double.IsPositiveInfinity(worldSurfaceTiles)))
        {
            throw new ArgumentOutOfRangeException(nameof(worldSurfaceTiles));
        }
    }
}
