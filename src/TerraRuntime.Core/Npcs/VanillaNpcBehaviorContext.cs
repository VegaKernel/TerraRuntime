using TerraRuntime.Contracts.Gameplay;
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
    private readonly NpcSnapshot[] _npcPeers = new NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
    private IRuntimePlayerSlotSnapshotLookup? _playerSnapshots;
    private int _candidateCount;
    private int _npcPeerCount;

    public bool SlimeGroundEnabled { get; private set; }

    public bool GroundFighterEnabled { get; private set; }

    public double WorldSurfacePixels { get; private set; } = double.PositiveInfinity;

    public bool DayTime { get; private set; } = true;

    public bool SlimeRainActive { get; private set; }

    public bool GoodWorld { get; private set; }

    public bool ExpertMode { get; private set; }

    public bool MasterMode { get; private set; }

    public void SetPlayerSnapshotLookup(IRuntimePlayerSlotSnapshotLookup playerSnapshots) =>
        _playerSnapshots = playerSnapshots ?? throw new ArgumentNullException(nameof(playerSnapshots));

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

    public void SetWorldConditions(
        bool dayTime,
        bool slimeRainActive,
        bool goodWorld = false,
        bool expertMode = false,
        bool masterMode = false)
    {
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));

        DayTime = dayTime;
        SlimeRainActive = slimeRainActive;
        GoodWorld = goodWorld;
        ExpertMode = expertMode;
        MasterMode = masterMode;
    }

    public void SetCandidates(ReadOnlySpan<VanillaNpcTargetCandidate> candidates)
    {
        if (candidates.Length > _candidates.Length)
            throw new ArgumentException("Too many vanilla player target candidates.", nameof(candidates));

        for (int index = 0; index < candidates.Length; index++)
        {
            VanillaNpcTargetCandidate candidate = candidates[index];
            if (_playerSnapshots is not null &&
                _playerSnapshots.TryGetPlayer(new PlayerSlotId(candidate.Slot), out PlayerStateSnapshot player) &&
                float.IsFinite(player.VelocityX) &&
                float.IsFinite(player.VelocityY))
            {
                candidate = candidate with
                {
                    VelocityX = player.VelocityX,
                    VelocityY = player.VelocityY
                };
            }

            _candidates[index] = candidate;
        }

        _candidateCount = candidates.Length;
    }

    public void SetNpcPeers(ReadOnlySpan<NpcSnapshot> peers)
    {
        if (peers.Length > _npcPeers.Length)
            throw new ArgumentException("Too many vanilla NPC peers.", nameof(peers));

        peers.CopyTo(_npcPeers);
        _npcPeerCount = peers.Length;
    }

    public int CountNpcPeers(NpcTypeId type)
    {
        int count = 0;
        for (int index = 0; index < _npcPeerCount; index++)
        {
            NpcSnapshot candidate = _npcPeers[index];
            if (candidate.IsActive && candidate.TypeIdentity == type)
                count++;
        }
        return count;
    }

    public int CountActivePlayersWithin(float centerX, float centerY, float radius)
    {
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY) || !float.IsFinite(radius) || radius < 0f)
            return 0;

        float radiusSquared = radius * radius;
        int count = 0;
        for (int index = 0; index < _candidateCount; index++)
        {
            VanillaNpcTargetCandidate candidate = _candidates[index];
            if (!candidate.Active || candidate.Dead || candidate.Ghost)
                continue;
            float dx = candidate.CenterX - centerX;
            float dy = candidate.CenterY - centerY;
            if (dx * dx + dy * dy < radiusSquared)
                count++;
        }
        return count;
    }

    public bool TryFindFirstNpcPeer(NpcTypeId type, out NpcSnapshot peer)
    {
        for (int index = 0; index < _npcPeerCount; index++)
        {
            NpcSnapshot candidate = _npcPeers[index];
            if (candidate.IsActive && candidate.TypeIdentity == type)
            {
                peer = candidate;
                return true;
            }
        }

        peer = default;
        return false;
    }

    public bool TryFindNpcPeer(byte slot, out NpcSnapshot peer)
    {
        for (int index = 0; index < _npcPeerCount; index++)
        {
            NpcSnapshot candidate = _npcPeers[index];
            if (candidate.Handle.Slot == slot && candidate.IsActive)
            {
                peer = candidate;
                return true;
            }
        }

        peer = default;
        return false;
    }

    public bool TrySelectClosestTarget(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        out VanillaBlueSlimeTargetRefresh target)
    {
        target = default;
        if (_candidateCount == 0 ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return false;
        }

        float npcCenterX = npc.PositionX + hitbox.Width * 0.5f;
        float npcCenterY = npc.PositionY + hitbox.Height * 0.5f;
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
            !candidate.Active || candidate.Dead || candidate.Ghost ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return false;
        }

        float playerLeft = candidate.CenterX - BasePlayerWidth * 0.5f;
        float playerTop = candidate.CenterY - BasePlayerHeight * 0.5f;
        return npc.PositionX < playerLeft + BasePlayerWidth &&
               npc.PositionX + hitbox.Width > playerLeft &&
               npc.PositionY < playerTop + BasePlayerHeight &&
               npc.PositionY + hitbox.Height > playerTop;
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
