using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Mutable per-world inputs shared by verified vanilla NPC behavior-family strategies. This object owns the
/// bounded player-candidate scratch buffer and world-condition facts so individual strategies remain focused on
/// behavior rather than orchestration or server-state discovery.
/// </summary>
internal sealed class VanillaNpcBehaviorContext
{
    public IVanillaNpcProjectileAnchorLookup? ProjectileAnchors { get; set; }

    public const int MaximumPlayerCandidates = byte.MaxValue;

    private readonly VanillaNpcTargetCandidate[] _candidates = new VanillaNpcTargetCandidate[MaximumPlayerCandidates];
    private readonly NpcSnapshot[] _npcPeers = new NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
    private readonly VanillaNpcRetainedSlot[] _retainedNpcSlots = new VanillaNpcRetainedSlot[RuntimeNpcStore.MaximumAddressableCapacity];
    private IRuntimePlayerSlotSnapshotLookup? _playerSnapshots;
    private int _candidateCount;
    private int _npcPeerCount;
    private int _retainedNpcSlotCount;

    public bool SlimeGroundEnabled { get; private set; }

    public bool GroundFighterEnabled { get; private set; }

    public double WorldSurfacePixels { get; private set; } = double.PositiveInfinity;

    public double RockLayerPixels { get; private set; } = double.PositiveInfinity;

    public double WorldWidthPixels { get; private set; }

    public void SetWorldBounds(int widthTiles, double worldSurfaceTiles, double rockLayerTiles = double.PositiveInfinity)
    {
        if (widthTiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthTiles));
        if (!double.IsFinite(worldSurfaceTiles) || worldSurfaceTiles <= 0d)
            throw new ArgumentOutOfRangeException(nameof(worldSurfaceTiles));
        if (!double.IsPositiveInfinity(rockLayerTiles) &&
            (!double.IsFinite(rockLayerTiles) || rockLayerTiles <= worldSurfaceTiles))
        {
            throw new ArgumentOutOfRangeException(nameof(rockLayerTiles));
        }
        WorldWidthPixels = widthTiles * 16d;
        WorldSurfacePixels = worldSurfaceTiles * 16d;
        RockLayerPixels = rockLayerTiles * 16d;
    }

    public bool DayTime { get; private set; } = true;

    public bool SlimeRainActive { get; private set; }

    public bool GoodWorld { get; private set; }

    public bool ExpertMode { get; private set; }

    public bool MasterMode { get; private set; }

    public bool RemixWorld { get; private set; }

    public float WindSpeedCurrent { get; private set; }

    private bool empressRemixRageMode;

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
        bool masterMode = false,
        float windSpeedCurrent = 0f,
        bool remixWorld = false)
    {
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));
        if (!float.IsFinite(windSpeedCurrent))
            throw new ArgumentOutOfRangeException(nameof(windSpeedCurrent));

        DayTime = dayTime;
        SlimeRainActive = slimeRainActive;
        GoodWorld = goodWorld;
        ExpertMode = expertMode;
        MasterMode = masterMode;
        WindSpeedCurrent = windSpeedCurrent;
        RemixWorld = remixWorld;
        if (!remixWorld)
            empressRemixRageMode = false;
    }

    /// <summary>
    /// Mirrors <c>NPC.ShouldEmpressBeEnraged</c>. In a Remix world, TerrariaServer latches rage when the
    /// first physical Empress slot rises above the surface; otherwise rage is the normal daytime condition.
    /// </summary>
    public bool ShouldEmpressBeEnraged(in NpcSnapshot current)
    {
        if (!RemixWorld)
            return DayTime;
        if (empressRemixRageMode)
            return true;

        // The source scans Main.npc by physical slot and checks type without an active guard. Retained slots
        // preserve that observable state after despawn, so prefer them whenever the executor supplies them.
        if (_retainedNpcSlotCount > 0)
        {
            for (int index = 0; index < _retainedNpcSlotCount; index++)
            {
                VanillaNpcRetainedSlot slot = _retainedNpcSlots[index];
                if (slot.TypeIdentity != VanillaNpcIds.EmpressOfLight)
                    continue;
                if (slot.PositionY + 50f < WorldSurfacePixels)
                {
                    empressRemixRageMode = true;
                    return true;
                }
                return false;
            }
            return false;
        }

        NpcSnapshot first = current;
        for (int index = 0; index < _npcPeerCount; index++)
        {
            NpcSnapshot peer = _npcPeers[index];
            if (peer.TypeIdentity == VanillaNpcIds.EmpressOfLight && peer.Handle.Slot < first.Handle.Slot)
                first = peer;
        }

        // Type 636 has the source 100-pixel body. AI_120 compares its physical center with the strict surface edge.
        if (first.PositionY + 50f < WorldSurfacePixels)
        {
            empressRemixRageMode = true;
            return true;
        }
        return false;
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

    public void SetRetainedNpcSlots(ReadOnlySpan<VanillaNpcRetainedSlot> slots)
    {
        if (slots.Length > _retainedNpcSlots.Length)
            throw new ArgumentException("Too many retained vanilla NPC slots.", nameof(slots));

        slots.CopyTo(_retainedNpcSlots);
        _retainedNpcSlotCount = slots.Length;
    }

    public int CopyNpcPeers(NpcTypeId type, Span<NpcSnapshot> destination)
    {
        int count = 0;
        for (int index = 0; index < _npcPeerCount && count < destination.Length; index++)
        {
            NpcSnapshot candidate = _npcPeers[index];
            if (candidate.IsActive && candidate.TypeIdentity == type)
                destination[count++] = candidate;
        }
        return count;
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

    public ReadOnlySpan<VanillaNpcTargetCandidate> Candidates => _candidates.AsSpan(0, _candidateCount);

    public int CandidateCount => _candidateCount;

    public VanillaNpcTargetCandidate GetCandidateAt(int index)
    {
        if ((uint)index >= (uint)_candidateCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _candidates[index];
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

    public bool TryGetAverageNpcPeerCenter(NpcTypeId type, out float centerX, out float centerY)
    {
        float sumX = 0f;
        float sumY = 0f;
        int count = 0;
        for (int index = 0; index < _npcPeerCount; index++)
        {
            NpcSnapshot candidate = _npcPeers[index];
            if (!candidate.IsActive || candidate.TypeIdentity != type ||
                !VanillaNpcDefinitionCatalog.TryGet(type, candidate.NetIdentity, out VanillaNpcDefinition definition) ||
                !definition.TryResolveHitbox(candidate.Simulation, out VanillaNpcHitboxSize hitbox))
                continue;
            sumX += candidate.PositionX + hitbox.Width * 0.5f;
            sumY += candidate.PositionY + hitbox.Height * 0.5f;
            count++;
        }
        if (count == 0)
        {
            centerX = 0f;
            centerY = 0f;
            return false;
        }
        centerX = sumX / count;
        centerY = sumY / count;
        return true;
    }

    public bool HasOwnedNpcPeer(NpcTypeId type, byte ownerSlot)
    {
        float encodedOwner = ownerSlot + 1;
        for (int index = 0; index < _npcPeerCount; index++)
        {
            NpcSnapshot candidate = _npcPeers[index];
            if (candidate.IsActive && candidate.TypeIdentity == type && candidate.Simulation.LocalAi.Ai3 == encodedOwner)
                return true;
        }
        return false;
    }

    public int CopyOwnedNpcPeers(NpcTypeId type, byte ownerSlot, Span<NpcSnapshot> destination)
    {
        float encodedOwner = ownerSlot + 1;
        int count = 0;
        for (int index = 0; index < _npcPeerCount && count < destination.Length; index++)
        {
            NpcSnapshot candidate = _npcPeers[index];
            if (candidate.IsActive && candidate.TypeIdentity == type && candidate.Simulation.LocalAi.Ai3 == encodedOwner)
                destination[count++] = candidate;
        }
        return count;
    }

    public bool TryGetAverageOwnedNpcPeerCenter(NpcTypeId type, byte ownerSlot, out float centerX, out float centerY)
    {
        float encodedOwner = ownerSlot + 1;
        float sumX = 0f;
        float sumY = 0f;
        int count = 0;
        for (int index = 0; index < _npcPeerCount; index++)
        {
            NpcSnapshot candidate = _npcPeers[index];
            if (!candidate.IsActive || candidate.TypeIdentity != type || candidate.Simulation.LocalAi.Ai3 != encodedOwner ||
                !VanillaNpcDefinitionCatalog.TryGet(type, candidate.NetIdentity, out VanillaNpcDefinition definition) ||
                !definition.TryResolveHitbox(candidate.Simulation, out VanillaNpcHitboxSize hitbox))
                continue;
            sumX += candidate.PositionX + hitbox.Width * 0.5f;
            sumY += candidate.PositionY + hitbox.Height * 0.5f;
            count++;
        }
        if (count == 0)
        {
            centerX = 0f;
            centerY = 0f;
            return false;
        }
        centerX = sumX / count;
        centerY = sumY / count;
        return true;
    }

    public bool TryFindOwnedNpcPeer(NpcTypeId type, byte ownerSlot, out NpcSnapshot peer)
    {
        float encodedOwner = ownerSlot + 1;
        for (int index = 0; index < _npcPeerCount; index++)
        {
            NpcSnapshot candidate = _npcPeers[index];
            if (candidate.IsActive && candidate.TypeIdentity == type && candidate.Simulation.LocalAi.Ai3 == encodedOwner)
            {
                peer = candidate;
                return true;
            }
        }
        peer = default;
        return false;
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

    /// <summary>Returns a retained physical slot, including an inactive NPC whose source fields remain readable.</summary>
    public bool TryFindNpcSlot(byte slot, out VanillaNpcRetainedSlot peer)
    {
        if (slot < _retainedNpcSlotCount)
        {
            peer = _retainedNpcSlots[slot];
            return true;
        }

        if (TryFindNpcPeer(slot, out NpcSnapshot active))
        {
            peer = new VanillaNpcRetainedSlot(slot, true, new NpcStateUpdate(
                active.Type, active.NetId, active.PositionX, active.PositionY, active.VelocityX, active.VelocityY,
                active.Target, active.Ai, active.Simulation));
            return true;
        }

        peer = default;
        return false;
    }

    public bool HasLivingPlayer
    {
        get
        {
            for (int i = 0; i < _candidateCount; i++)
                if (_candidates[i].Active && !_candidates[i].Dead) return true;
            return false;
        }
    }

    public bool TrySelectClosestTarget(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        out VanillaBlueSlimeTargetRefresh target)
    {
        target = default;
        if (_candidateCount == 0 ||
            !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
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
            !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return false;
        }

        float playerLeft = candidate.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f;
        float playerTop = candidate.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f;
        return npc.PositionX < playerLeft + VanillaPlayerHitboxFacts.BaseWidth &&
               npc.PositionX + hitbox.Width > playerLeft &&
               npc.PositionY < playerTop + VanillaPlayerHitboxFacts.BaseHeight &&
               npc.PositionY + hitbox.Height > playerTop;
    }

    public bool AnyLivingPlayerIntersects(int left, int top, int width, int height)
    {
        for (int index = 0; index < _candidateCount; index++)
        {
            VanillaNpcTargetCandidate player = _candidates[index];
            if (!player.Active || player.Dead)
                continue;
            int playerLeft = (int)(player.CenterX - VanillaPlayerHitboxFacts.BaseWidth * .5f);
            int playerTop = (int)(player.CenterY - VanillaPlayerHitboxFacts.BaseHeight * .5f);
            if (left < playerLeft + VanillaPlayerHitboxFacts.BaseWidth && left + width > playerLeft &&
                top < playerTop + VanillaPlayerHitboxFacts.BaseHeight && top + height > playerTop)
                return true;
        }
        return false;
    }

    /// <summary>
    /// AI_037's head uses this exact active-player rectangle scan before deciding that it must dig
    /// toward a target above it. Unlike combat targeting, the source scan intentionally includes
    /// dead and ghost player slots as long as the slot is active.
    /// </summary>
    public bool AnyActivePlayerIntersectsExpanded(int left, int top, int width, int height, int padding)
    {
        if (width < 0 || height < 0 || padding < 0)
            return false;

        for (int index = 0; index < _candidateCount; index++)
        {
            VanillaNpcTargetCandidate player = _candidates[index];
            if (!player.Active)
                continue;
            int playerLeft = (int)(player.CenterX - VanillaPlayerHitboxFacts.BaseWidth * .5f) - padding;
            int playerTop = (int)(player.CenterY - VanillaPlayerHitboxFacts.BaseHeight * .5f) - padding;
            int playerSize = padding * 2;
            if (left < playerLeft + playerSize && left + width > playerLeft &&
                top < playerTop + playerSize && top + height > playerTop)
                return true;
        }

        return false;
    }

    public bool ShadowSpawnIntersectsOtherPlayer(
        byte targetSlot,
        float spawnCenterX,
        float spawnCenterY,
        float padding)
    {
        if (!float.IsFinite(spawnCenterX) || !float.IsFinite(spawnCenterY) || !float.IsFinite(padding) || padding < 0f)
            return true;

        float left = spawnCenterX - padding;
        float top = spawnCenterY - padding;
        float width = 40f + padding * 2f;
        float height = 40f + padding * 2f;
        for (int index = 0; index < _candidateCount; index++)
        {
            VanillaNpcTargetCandidate candidate = _candidates[index];
            if (candidate.Slot == targetSlot || !candidate.Active || candidate.Dead || candidate.Ghost)
                continue;
            float playerLeft = candidate.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f;
            float playerTop = candidate.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f;
            if (left < playerLeft + VanillaPlayerHitboxFacts.BaseWidth &&
                left + width > playerLeft &&
                top < playerTop + VanillaPlayerHitboxFacts.BaseHeight &&
                top + height > playerTop)
            {
                return true;
            }
        }
        return false;
    }

    public bool TrySelectClosestWallOfFleshTarget(
        float centerX,
        float centerY,
        float minimumCenterY,
        out VanillaNpcTargetCandidate target)
    {
        target = default;
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY) || !float.IsFinite(minimumCenterY))
            return false;

        float bestDistanceSquared = float.PositiveInfinity;
        bool found = false;
        for (int index = 0; index < _candidateCount; index++)
        {
            VanillaNpcTargetCandidate candidate = _candidates[index];
            if (!candidate.Active || candidate.Dead || candidate.Ghost || candidate.CenterY < minimumCenterY)
                continue;

            float dx = candidate.CenterX - centerX;
            float dy = candidate.CenterY - centerY;
            float distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            target = candidate;
            found = true;
        }

        return found;
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

    /// <summary>
    /// TerrariaServer 1.4.5.8 Player.FindClosest geometry: active, non-dead players are compared by Manhattan
    /// distance between centers, with physical player-slot order breaking ties.
    /// </summary>
    public bool TrySelectClosestActivePlayer(
        float positionX,
        float positionY,
        float width,
        float height,
        out VanillaNpcTargetCandidate target)
    {
        target = default;
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY) ||
            !float.IsFinite(width) || !float.IsFinite(height) || width < 0f || height < 0f)
        {
            return false;
        }

        float centerX = positionX + width * 0.5f;
        float centerY = positionY + height * 0.5f;
        float bestDistance = float.PositiveInfinity;
        byte bestSlot = byte.MaxValue;
        bool found = false;
        for (int index = 0; index < _candidateCount; index++)
        {
            VanillaNpcTargetCandidate candidate = _candidates[index];
            if (!candidate.Active || candidate.Dead)
                continue;

            float distance = MathF.Abs(candidate.CenterX - centerX) + MathF.Abs(candidate.CenterY - centerY);
            if (found && (distance > bestDistance || (distance == bestDistance && candidate.Slot >= bestSlot)))
                continue;

            bestDistance = distance;
            bestSlot = candidate.Slot;
            target = candidate;
            found = true;
        }

        return found;
    }

    /// <summary>Player.FindClosest fallback: first active slot, even dead; otherwise raw slot zero.</summary>
    public VanillaNpcTargetCandidate FindClosestPlayer(float x, float y, float width, float height)
    {
        if (TrySelectClosestActivePlayer(x, y, width, height, out var closest))
            return closest;

        byte slot = byte.MaxValue;
        for (int i = 0; i < _candidateCount; i++)
            if (_candidates[i].Active && _candidates[i].Slot < slot)
                slot = _candidates[i].Slot;
        if (slot == byte.MaxValue) slot = 0;
        if (TryFindCandidate(slot, out var fallback)) return fallback;
        return new VanillaNpcTargetCandidate(slot,
            VanillaPlayerHitboxFacts.BaseWidth * .5f, VanillaPlayerHitboxFacts.BaseHeight * .5f,
            0, false, false, false, false);
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
