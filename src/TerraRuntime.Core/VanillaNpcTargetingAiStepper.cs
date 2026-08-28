using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Coordinates verified player-target selection cadence with state-only NPC AI. Demon Eye refreshes every
/// ordinary style-2 tick; Blue Slime refreshes at its AI_001 state-machine points; ordinary Zombie follows
/// verified AI_003 pursuit, visibility and discouraged/despawn branches.
/// </summary>
public sealed class VanillaNpcTargetingAiStepper : INpcAiStateStepper
{
    public const int MaximumPlayerCandidates = byte.MaxValue;

    private const float VanillaBasePlayerWidth = 20f;
    private const float VanillaBasePlayerHeight = 42f;

    private readonly INpcAiStateStepper _inner;
    private IVanillaNpcCanHitQuery? _canHitQuery;
    private readonly VanillaNpcTargetCandidate[] _candidates = new VanillaNpcTargetCandidate[MaximumPlayerCandidates];
    private int _candidateCount;
    private bool _blueSlimeMotionEnabled;
    private bool _zombieMotionEnabled;
    private double _worldSurfacePixels = double.PositiveInfinity;
    private bool _dayTime = true;
    private bool _slimeRainActive;

    public VanillaNpcTargetingAiStepper(
        INpcAiStateStepper inner,
        IVanillaNpcCanHitQuery? canHitQuery = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _canHitQuery = canHitQuery;
    }

    public void SetCanHitQuery(IVanillaNpcCanHitQuery canHitQuery)
    {
        ArgumentNullException.ThrowIfNull(canHitQuery);
        _canHitQuery = canHitQuery;
    }

    public void EnableBlueSlimeMotion(double worldSurfaceTiles = double.PositiveInfinity)
    {
        ValidateWorldSurface(worldSurfaceTiles);
        _blueSlimeMotionEnabled = true;
        _worldSurfacePixels = worldSurfaceTiles * 16d;
    }

    public void EnableZombieMotion(double worldSurfaceTiles)
    {
        ValidateWorldSurface(worldSurfaceTiles);
        _zombieMotionEnabled = true;
        _worldSurfacePixels = worldSurfaceTiles * 16d;
    }

    public void SetWorldConditions(bool dayTime, bool slimeRainActive)
    {
        _dayTime = dayTime;
        _slimeRainActive = slimeRainActive;
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
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType))
        {
            next = default;
            return false;
        }

        if (npcType == VanillaNpcIds.BlueSlime && _blueSlimeMotionEnabled)
            return TryStepBlueSlime(in npc, npcType, out next);

        if (npcType == VanillaNpcIds.Zombie && _zombieMotionEnabled)
            return TryStepZombie(in npc, npcType, out next);

        NpcSnapshot targeted = npc;
        if (npcType == VanillaNpcIds.DemonEye && TrySelectClosestTarget(in npc, out VanillaBlueSlimeTargetRefresh closest))
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

    private bool TryStepBlueSlime(in NpcSnapshot npc, NpcTypeId npcType, out NpcStateUpdate next)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition definition) ||
            definition.AiStyle != VanillaNpcAiStyles.Slime)
        {
            next = default;
            return false;
        }

        VanillaBlueSlimeTargetRefresh closest = TrySelectClosestTarget(in npc, out VanillaBlueSlimeTargetRefresh selected)
            ? selected
            : default;
        NpcSimulationState simulation = npc.Simulation;
        bool damaged = simulation.LifeMax > 0 && simulation.Life != simulation.LifeMax;
        bool engaged = !_dayTime || damaged || _slimeRainActive || npc.PositionY > _worldSurfacePixels;
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
            SolidCollision: simulation.SolidCollision,
            ClosestTarget: closest);

        if (!VanillaBlueSlimeMotion.TryStep(in input, out VanillaBlueSlimeMotionResult result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            npcType.Value,
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

    private bool TryStepZombie(in NpcSnapshot npc, NpcTypeId npcType, out NpcStateUpdate next)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition definition) ||
            definition.AiStyle != VanillaNpcAiStyles.Fighter)
        {
            next = default;
            return false;
        }

        bool daytimeSurface = _dayTime && npc.PositionY < _worldSurfacePixels;
        ReadOnlySpan<VanillaNpcTargetCandidate> candidates = _candidates.AsSpan(0, _candidateCount);
        VanillaBlueSlimeTargetRefresh closest = TrySelectClosestTarget(in npc, out VanillaBlueSlimeTargetRefresh selected)
            ? selected
            : default;
        int zombieDirectionY = closest.DirectionY;
        if (closest.HasTarget &&
            zombieDirectionY > 0 &&
            TryFindCandidate(checked((byte)closest.Target), candidates, out VanillaNpcTargetCandidate selectedCandidate) &&
            selectedCandidate.CenterY <= npc.PositionY + definition.Height)
        {
            zombieDirectionY = -1;
        }

        var zombieTarget = new VanillaZombieTargetRefresh(
            closest.HasTarget,
            closest.Target,
            closest.DirectionX,
            zombieDirectionY);

        bool applyCanHitRule = false;
        bool canHitCurrentTarget = true;
        float currentTargetCenterY = 0f;
        if (_canHitQuery is not null &&
            npc.Target < byte.MaxValue &&
            TryFindCandidate(checked((byte)npc.Target), candidates, out VanillaNpcTargetCandidate currentTarget) &&
            currentTarget.Active &&
            !currentTarget.Dead &&
            !currentTarget.Ghost)
        {
            applyCanHitRule = true;
            canHitCurrentTarget = _canHitQuery.CanHit(in npc, in currentTarget);
            currentTargetCenterY = currentTarget.CenterY;
        }

        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaZombieMotionInput(
            PositionX: npc.PositionX,
            OldPositionX: simulation.OldPositionX,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            DirectionX: simulation.DirectionX,
            DirectionY: simulation.DirectionY,
            Target: npc.Target,
            Ai: npc.Ai,
            Scale: simulation.Scale,
            TargetOverlaps: TargetOverlapsNpc(in npc, definition),
            ClosestTarget: zombieTarget)
        {
            PursuitAllowed = !daytimeSurface,
            EncourageDespawn = daytimeSurface,
            JustHit = simulation.JustHit,
            ApplyCanHitRule = applyCanHitRule,
            CanHitCurrentTarget = canHitCurrentTarget,
            NpcCenterY = npc.PositionY + definition.Height * 0.5f,
            CurrentTargetCenterY = currentTargetCenterY,
            TimeLeft = simulation.TimeLeft
        };

        if (!VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result))
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
            result.Target,
            result.Ai,
            simulation with
            {
                DirectionX = result.DirectionX,
                DirectionY = result.DirectionY,
                NoGravity = false,
                JustHit = false,
                TimeLeft = result.TimeLeft
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

    private bool TargetOverlapsNpc(in NpcSnapshot npc, VanillaNpcDefinition definition)
    {
        if (npc.Target >= byte.MaxValue ||
            !TryFindCandidate((byte)npc.Target, _candidates.AsSpan(0, _candidateCount), out VanillaNpcTargetCandidate candidate) ||
            !candidate.Active || candidate.Dead || candidate.Ghost)
        {
            return false;
        }

        float playerLeft = candidate.CenterX - VanillaBasePlayerWidth * 0.5f;
        float playerTop = candidate.CenterY - VanillaBasePlayerHeight * 0.5f;
        return npc.PositionX < playerLeft + VanillaBasePlayerWidth &&
               npc.PositionX + definition.Width > playerLeft &&
               npc.PositionY < playerTop + VanillaBasePlayerHeight &&
               npc.PositionY + definition.Height > playerTop;
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
