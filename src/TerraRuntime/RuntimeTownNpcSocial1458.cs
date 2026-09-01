using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

internal interface IRuntimeTownNpcEmoteSink1458
{
    bool TryPublishEmoteBubble(in TerrariaEmoteBubbleState state);
}

internal interface IRuntimeTownNpcSocialRandom1458
{
    int Next(int exclusiveMax);
}

internal sealed class SharedRuntimeTownNpcSocialRandom1458 : IRuntimeTownNpcSocialRandom1458
{
    public static SharedRuntimeTownNpcSocialRandom1458 Instance { get; } = new();
    private SharedRuntimeTownNpcSocialRandom1458() { }
    public int Next(int exclusiveMax) => Random.Shared.Next(exclusiveMax);
}

internal readonly record struct RuntimeTownNpcSocialTickSummary1458(
    int ResidentsVisited,
    int StatesAdvanced,
    int ConversationsStarted,
    int RpsGamesStarted,
    int PlayerReactionsStarted,
    int PetIdlesStarted,
    int BubblesPublished,
    int RejectedCommits);

/// <summary>
/// Authoritative TerrariaServer 1.4.5.8 AI_007 social slice. It owns the server-only initiation and timer/facing
/// transitions for ordinary conversations (3/4), RPS conversations (16/17), player-facing states (6/7/18/19),
/// passive idle states (2/11), and Town Pet idle states (20..23). Explicit RPS emotes are emitted through packet 91
/// at the pinned FindFrame ticks 40/100/160. Chair/home state 5 remains owned by RuntimeTownNpcSchedule1458 and
/// combat states remain owned by RuntimeTownNpcCombat1458.
/// </summary>
internal sealed class RuntimeTownNpcSocial1458
{
    private const float ConversationMinDistance = 20f;
    private const float ConversationMaxDistance = 100f;
    private const float PlayerStartDistance = 150f;
    private const float PlayerKeepDistance = 200f;
    private const float PlayerWidth = 20f;
    private const float PlayerHeight = 42f;

    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeNpcStore npcs;
    private readonly WorldTileStore tiles;
    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly IRuntimeTownNpcEmoteSink1458? emotes;
    private readonly RuntimeTownNpcSchedule1458? schedule;
    private readonly IRuntimeTownNpcSocialRandom1458 random;
    private readonly NpcSnapshot[] peers;
    private readonly Dictionary<NpcHandle, int> rpsElapsed = [];
    private readonly Dictionary<NpcHandle, NpcHandle> rpsPartners = [];
    private int nextBubbleId;

    public RuntimeTownNpcSocial1458(
        RuntimeTownNpcStateStore townNpcs,
        RuntimeNpcStore npcs,
        WorldTileStore tiles,
        IRuntimePlayerSlotSnapshotLookup players,
        IRuntimeTownNpcEmoteSink1458? emotes = null,
        RuntimeTownNpcSchedule1458? schedule = null,
        IRuntimeTownNpcSocialRandom1458? random = null)
    {
        this.townNpcs = townNpcs ?? throw new ArgumentNullException(nameof(townNpcs));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.emotes = emotes;
        this.schedule = schedule;
        this.random = random ?? SharedRuntimeTownNpcSocialRandom1458.Instance;
        peers = new NpcSnapshot[npcs.Capacity];
    }

    public RuntimeTownNpcSocialTickSummary1458 Tick()
    {
        int peerCount = npcs.CopyActive(peers);
        Span<RuntimeTownNpcHomeCommit> roster = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
        int townCount = townNpcs.CopyHomeBaselines(roster);
        int visited = 0;
        int advanced = 0;
        int conversations = 0;
        int rps = 0;
        int playerReactions = 0;
        int petIdles = 0;
        int bubbles = 0;
        int rejected = 0;

        for (int i = 0; i < townCount; i++)
        {
            short slot = roster[i].NpcSlot;
            if ((uint)slot > byte.MaxValue || !npcs.TryGetActive(checked((byte)slot), out NpcSnapshot source))
                continue;
            visited++;

            if (IsOwnedState(source.Ai.Ai0))
            {
                if (TryAdvance(in source, out int emitted))
                {
                    advanced++;
                    bubbles += emitted;
                }
                else
                {
                    rejected++;
                }
                continue;
            }

            if (source.Ai.Ai0 != 0f || source.VelocityY != 0f || source.Simulation.Wet)
                continue;

            NpcTypeId type = source.TypeIdentity;
            if (IsTownPet(type))
            {
                int baseChance = type == VanillaNpcIds.TownDog ? 60 : 120;
                bool homeRest = schedule?.GetState(slot) == RuntimeTownNpcScheduleState1458.RestingAtHome;
                int chance = homeRest ? checked(baseChance * 4) : baseChance;
                if (random.Next(chance) == 0 && TryStartPetIdle(in source, type))
                    petIdles++;
                continue;
            }

            if (random.Next(300) == 0 && TryStartConversation(in source, peers.AsSpan(0, peerCount), rpsGame: false))
            {
                conversations++;
                continue;
            }
            if (random.Next(1800) == 0 && TryStartConversation(in source, peers.AsSpan(0, peerCount), rpsGame: true))
            {
                rps++;
                continue;
            }
            if (type == VanillaNpcIds.PartyGirl && random.Next(1200) == 0 && TryStartPlayerState(in source, 6f, 300))
            {
                playerReactions++;
                continue;
            }
            if (type == VanillaNpcIds.Tavernkeep && random.Next(600) == 0 && TryStartPlayerState(in source, 18f, 300))
            {
                playerReactions++;
                continue;
            }
            if (random.Next(1800) == 0)
            {
                NpcAiState ai = source.Ai with { Ai0 = 2f, Ai1 = 45f };
                if (TryCommit(in source, ai, source.Simulation, source.VelocityX, out _))
                    continue;
                rejected++;
            }
            if (type == VanillaNpcIds.Pirate && random.Next(600) == 0)
            {
                NpcAiState ai = source.Ai with { Ai0 = 11f, Ai1 = 30f * random.Next(1, 4) };
                if (TryCommit(in source, ai, source.Simulation, source.VelocityX, out _))
                    continue;
                rejected++;
            }
            if (random.Next(1200) == 0 && TryStartPlayerState(in source, 7f, 220))
                playerReactions++;
        }

        PruneRpsState();
        return new RuntimeTownNpcSocialTickSummary1458(
            visited, advanced, conversations, rps, playerReactions, petIdles, bubbles, rejected);
    }

    internal bool TryStartConversationForTesting(NpcHandle source, bool rpsGame)
    {
        if (!npcs.TryGet(source, out NpcSnapshot current))
            return false;
        int count = npcs.CopyActive(peers);
        return TryStartConversation(in current, peers.AsSpan(0, count), rpsGame);
    }

    internal bool TryStartPlayerStateForTesting(NpcHandle source, float state, int duration)
    {
        if (!npcs.TryGet(source, out NpcSnapshot current))
            return false;
        return TryStartPlayerState(in current, state, duration);
    }

    internal bool TryStartPetIdleForTesting(NpcHandle source)
    {
        if (!npcs.TryGet(source, out NpcSnapshot current))
            return false;
        return TryStartPetIdle(in current, current.TypeIdentity);
    }

    private bool TryAdvance(in NpcSnapshot source, out int bubblesPublished)
    {
        bubblesPublished = 0;
        float state = source.Ai.Ai0;
        NpcAiState ai = source.Ai;
        NpcSimulationState simulation = source.Simulation;
        float velocityX = source.VelocityX * 0.8f;

        if (state is 2f or 11f)
        {
            NpcAiState local = simulation.LocalAi with { Ai3 = simulation.LocalAi.Ai3 - 1f };
            if (random.Next(60) == 0 && local.Ai3 == 0f)
            {
                local = local with { Ai3 = 60f };
                int direction = simulation.DirectionX is -1 or 1 ? -simulation.DirectionX : -1;
                simulation = simulation with { DirectionX = direction, SpriteDirection = direction, LocalAi = local };
            }
            else
            {
                simulation = simulation with { LocalAi = local };
            }
            ai = ai with { Ai1 = ai.Ai1 - 1f };
            if (ai.Ai1 <= 0f)
                ResetToWander(ref ai, ref simulation);
            return TryCommit(in source, ai, simulation, velocityX, out _);
        }

        if (state is 3f or 4f or 16f or 17f or 20f or 21f or 22f or 23f)
        {
            ai = ai with { Ai1 = ai.Ai1 - 1f };
            if (state == 16f)
                bubblesPublished = AdvanceRps(in source);
            if (ai.Ai1 <= 0f)
            {
                ResetToWander(ref ai, ref simulation);
                rpsElapsed.Remove(source.Handle);
                rpsPartners.Remove(source.Handle);
            }
            return TryCommit(in source, ai, simulation, velocityX, out _);
        }

        if (state is 6f or 7f or 18f or 19f)
        {
            if (state == 18f && (simulation.LocalAi.Ai3 < 1f || simulation.LocalAi.Ai3 > 2f))
                simulation = simulation with { LocalAi = simulation.LocalAi with { Ai3 = 2f } };
            ai = ai with { Ai1 = ai.Ai1 - 1f };
            int playerSlot = (int)ai.Ai2;
            if (!TryGetTalkablePlayer(playerSlot, in source, PlayerKeepDistance, out PlayerStateSnapshot player))
            {
                ai = ai with { Ai1 = 0f };
            }
            if (ai.Ai1 > 0f)
            {
                int direction = CenterX(in source) < player.PositionX + PlayerWidth * 0.5f ? 1 : -1;
                simulation = simulation with { DirectionX = direction, SpriteDirection = direction };
            }
            else
            {
                ResetToWander(ref ai, ref simulation);
            }
            return TryCommit(in source, ai, simulation, velocityX, out _);
        }

        return false;
    }

    private bool TryStartConversation(in NpcSnapshot source, ReadOnlySpan<NpcSnapshot> candidates, bool rpsGame)
    {
        if (source.Ai.Ai0 != 0f || source.VelocityY != 0f || source.Simulation.Wet || IsTownPet(source.TypeIdentity))
            return false;

        int duration = 420;
        duration *= random.Next(2) != 0 ? random.Next(1, 3) : random.Next(1, 4);
        if (duration <= 0)
            duration = 420;

        foreach (NpcSnapshot candidateSnapshot in candidates)
        {
            if (candidateSnapshot.Handle == source.Handle || !candidateSnapshot.IsActive ||
                !townNpcs.TryGet(checked((short)candidateSnapshot.Handle.Slot), out _) ||
                candidateSnapshot.Ai.Ai0 != 0f || candidateSnapshot.Simulation.Wet ||
                (rpsGame && IsTownPet(candidateSnapshot.TypeIdentity)) ||
                !TryGetCenters(in source, in candidateSnapshot, out float sx, out float sy, out float tx, out float ty))
            {
                continue;
            }
            float dx = tx - sx;
            float dy = ty - sy;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (!float.IsFinite(distance) || distance <= ConversationMinDistance || distance >= ConversationMaxDistance ||
                !VanillaWorldLineOfSight.CanHitLine(tiles, sx, sy, tx, ty) ||
                !npcs.TryGet(candidateSnapshot.Handle, out NpcSnapshot candidate))
            {
                continue;
            }

            int direction = source.PositionX < candidate.PositionX ? 1 : -1;
            NpcAiState sourceAi = source.Ai with { Ai0 = rpsGame ? 16f : 3f, Ai1 = duration, Ai2 = candidate.Handle.Slot };
            NpcSimulationState sourceSimulation = source.Simulation with { DirectionX = direction, SpriteDirection = direction };
            if (rpsGame)
            {
                int first = random.Next(4);
                int second = random.Next(3 - first);
                sourceSimulation = sourceSimulation with { LocalAi = sourceSimulation.LocalAi with { Ai2 = first, Ai3 = second } };
            }
            NpcAiState candidateAi = candidate.Ai with { Ai0 = rpsGame ? 17f : 4f, Ai1 = duration, Ai2 = source.Handle.Slot };
            NpcSimulationState candidateSimulation = candidate.Simulation with
            {
                DirectionX = -direction,
                SpriteDirection = -direction,
                LocalAi = rpsGame ? candidate.Simulation.LocalAi with { Ai2 = 0f, Ai3 = 0f } : candidate.Simulation.LocalAi
            };

            if (!TryCommitPair(
                    in source, sourceAi, sourceSimulation,
                    in candidate, candidateAi, candidateSimulation,
                    out NpcSnapshot committedSource,
                    out NpcSnapshot committedCandidate))
            {
                return false;
            }

            if (rpsGame)
            {
                rpsElapsed[committedSource.Handle] = 0;
                rpsPartners[committedSource.Handle] = committedCandidate.Handle;
            }
            return true;
        }
        return false;
    }

    private bool TryStartPlayerState(in NpcSnapshot source, float state, int duration)
    {
        for (int slot = 0; slot < 255; slot++)
        {
            if (!TryGetTalkablePlayer(slot, in source, PlayerStartDistance, out PlayerStateSnapshot player))
                continue;
            int direction = source.PositionX < player.PositionX ? 1 : -1;
            NpcAiState ai = source.Ai with { Ai0 = state, Ai1 = duration, Ai2 = slot };
            NpcSimulationState simulation = source.Simulation with { DirectionX = direction, SpriteDirection = direction };
            return TryCommit(in source, ai, simulation, source.VelocityX, out _);
        }
        return false;
    }

    private bool TryStartPetIdle(in NpcSnapshot source, NpcTypeId type)
    {
        if (!IsTownPet(type) || source.VelocityX != 0f || source.Ai.Ai0 != 0f)
            return false;
        int stateCount = type == VanillaNpcIds.TownDog ? 2 : IsTownSlime(type) ? 0 : 3;
        int state = stateCount == 0 ? 20 : random.Next(20, 20 + stateCount);
        int duration = 200 + random.Next(300);
        if (state == 20 && type == VanillaNpcIds.TownCat)
            duration = 500 + random.Next(200);
        if (state == 21 && type == VanillaNpcIds.TownDog)
            duration = 100 + random.Next(100);
        if (state == 22 && type == VanillaNpcIds.TownBunny)
            duration = 200 + random.Next(200);
        if (state == 20 && IsTownSlime(type))
            duration = 180 + random.Next(240);

        NpcAiState ai = source.Ai with { Ai0 = state, Ai1 = duration, Ai2 = 0f };
        NpcSimulationState simulation = source.Simulation with { LocalAi = source.Simulation.LocalAi with { Ai3 = 0f } };
        return TryCommit(in source, ai, simulation, source.VelocityX, out _);
    }

    private int AdvanceRps(in NpcSnapshot source)
    {
        int elapsed = rpsElapsed.TryGetValue(source.Handle, out int current) ? current + 1 : 1;
        rpsElapsed[source.Handle] = elapsed;
        if (elapsed is not (40 or 100 or 160) ||
            !rpsPartners.TryGetValue(source.Handle, out NpcHandle partnerHandle) ||
            !npcs.TryGet(partnerHandle, out NpcSnapshot partner) || partner.Ai.Ai0 != 17f ||
            (int)partner.Ai.Ai2 != source.Handle.Slot)
        {
            return 0;
        }

        int round = elapsed == 40 ? 1 : elapsed == 100 ? 2 : 3;
        int remainingRounds = 3 - round;
        int sourceA = (int)source.Simulation.LocalAi.Ai2;
        int sourceB = (int)source.Simulation.LocalAi.Ai3;
        int partnerA = (int)partner.Simulation.LocalAi.Ai2;
        int partnerB = (int)partner.Simulation.LocalAi.Ai3;
        int sourceUnused = 3 - sourceA - sourceB;
        int outcome = -1;
        for (int attempt = 0; outcome < 0 && attempt < 99; attempt++)
        {
            outcome = random.Next(2);
            if (outcome == 0 && partnerA >= sourceB)
                outcome = -1;
            if (outcome == 1 && partnerB >= sourceA)
                outcome = -1;
            if (outcome == -1 && remainingRounds <= sourceUnused)
                outcome = 2;
        }
        if (outcome < 0)
            outcome = 2;

        NpcSimulationState partnerSimulation = partner.Simulation;
        if (outcome == 0)
        {
            partnerB++;
            partnerSimulation = partnerSimulation with { LocalAi = partnerSimulation.LocalAi with { Ai3 = partnerB } };
        }
        else if (outcome == 1)
        {
            partnerA++;
            partnerSimulation = partnerSimulation with { LocalAi = partnerSimulation.LocalAi with { Ai2 = partnerA } };
        }
        if (partnerSimulation != partner.Simulation &&
            !TryCommit(in partner, partner.Ai, partnerSimulation, partner.VelocityX, out partner))
        {
            return 0;
        }

        int sourceEmote = random.Next(3) switch { 0 => 38, 1 => 37, _ => 36 };
        int partnerEmote = outcome switch
        {
            0 => sourceEmote switch { 38 => 37, 37 => 36, _ => 38 },
            1 => sourceEmote switch { 38 => 36, 37 => 38, _ => 37 },
            _ => sourceEmote
        };
        if (remainingRounds == 0)
        {
            if (partnerB >= 2) sourceEmote -= 3;
            if (partnerA >= 2) partnerEmote -= 3;
        }

        ushort lifetime = checked((ushort)(elapsed == 160 ? 75 : 45));
        int published = 0;
        if (PublishNpcBubble(source.Handle.Slot, checked((byte)sourceEmote), lifetime)) published++;
        if (PublishNpcBubble(partner.Handle.Slot, checked((byte)partnerEmote), lifetime)) published++;
        return published;
    }

    private bool PublishNpcBubble(byte slot, byte emote, ushort lifetime)
    {
        if (emotes is null)
            return false;
        int id = nextBubbleId++;
        var state = new TerrariaEmoteBubbleState(
            id,
            TerrariaEmoteBubbleState.NpcAnchor,
            slot,
            lifetime,
            emote);
        return emotes.TryPublishEmoteBubble(in state);
    }

    private bool TryGetTalkablePlayer(int slot, in NpcSnapshot source, float maximumDistance, out PlayerStateSnapshot player)
    {
        player = default;
        if ((uint)slot >= 255u || !players.TryGetPlayer(new PlayerSlotId(checked((byte)slot)), out player) || player.IsDead)
            return false;
        float sx = CenterX(in source);
        float sy = CenterY(in source);
        float px = player.PositionX + PlayerWidth * 0.5f;
        float py = player.PositionY + PlayerHeight * 0.5f;
        float dx = px - sx;
        float dy = py - sy;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        return float.IsFinite(distance) && distance < maximumDistance &&
               VanillaWorldLineOfSight.CanHitLine(tiles, sx, sy, px, py);
    }

    private bool TryCommitPair(
        in NpcSnapshot first,
        NpcAiState firstAi,
        NpcSimulationState firstSimulation,
        in NpcSnapshot second,
        NpcAiState secondAi,
        NpcSimulationState secondSimulation,
        out NpcSnapshot committedFirst,
        out NpcSnapshot committedSecond)
    {
        if (!TryCommit(in first, firstAi, firstSimulation, first.VelocityX, out committedFirst))
        {
            committedSecond = default;
            return false;
        }
        if (TryCommit(in second, secondAi, secondSimulation, second.VelocityX, out committedSecond))
            return true;

        var rollback = SnapshotUpdate(in committedFirst, first.Ai, first.Simulation, first.VelocityX);
        npcs.TryUpdate(committedFirst.Handle, in rollback, out _);
        committedFirst = default;
        return false;
    }

    private bool TryCommit(
        in NpcSnapshot source,
        NpcAiState ai,
        NpcSimulationState simulation,
        float velocityX,
        out NpcSnapshot committed)
    {
        var update = SnapshotUpdate(in source, ai, simulation, velocityX);
        return npcs.TryUpdate(source.Handle, in update, out committed);
    }

    private static NpcStateUpdate SnapshotUpdate(
        in NpcSnapshot source,
        NpcAiState ai,
        NpcSimulationState simulation,
        float velocityX) => new(
            source.Type,
            source.NetId,
            source.PositionX,
            source.PositionY,
            velocityX,
            source.VelocityY,
            source.Target,
            ai,
            simulation);

    private void ResetToWander(ref NpcAiState ai, ref NpcSimulationState simulation)
    {
        ai = ai with { Ai0 = 0f, Ai1 = 60 + random.Next(60), Ai2 = 0f };
        simulation = simulation with { LocalAi = simulation.LocalAi with { Ai3 = 30 + random.Next(60) } };
    }

    private void PruneRpsState()
    {
        if (rpsElapsed.Count == 0)
            return;
        Span<NpcHandle> stale = stackalloc NpcHandle[Math.Min(rpsElapsed.Count, RuntimeTownNpcStateStore.MaximumTownNpcs)];
        int count = 0;
        foreach (NpcHandle handle in rpsElapsed.Keys)
        {
            if (count >= stale.Length)
                break;
            if (!npcs.TryGet(handle, out NpcSnapshot npc) || npc.Ai.Ai0 != 16f)
                stale[count++] = handle;
        }
        for (int i = 0; i < count; i++)
        {
            rpsElapsed.Remove(stale[i]);
            rpsPartners.Remove(stale[i]);
        }
    }

    private static bool IsOwnedState(float state) =>
        state is 2f or 3f or 4f or 6f or 7f or 11f or 16f or 17f or 18f or 19f or 20f or 21f or 22f or 23f;

    private static bool IsTownPet(NpcTypeId type) =>
        type == VanillaNpcIds.TownCat || type == VanillaNpcIds.TownDog || type == VanillaNpcIds.TownBunny || IsTownSlime(type);

    private static bool IsTownSlime(NpcTypeId type) =>
        type == VanillaNpcIds.TownSlimeBlue || type == VanillaNpcIds.TownSlimeGreen ||
        type == VanillaNpcIds.TownSlimeOld || type == VanillaNpcIds.TownSlimePurple ||
        type == VanillaNpcIds.TownSlimeRainbow || type == VanillaNpcIds.TownSlimeRed ||
        type == VanillaNpcIds.TownSlimeYellow || type == VanillaNpcIds.TownSlimeCopper;

    private static float CenterX(in NpcSnapshot npc)
    {
        return TryGetHitbox(in npc, out VanillaNpcHitboxSize hitbox)
            ? npc.PositionX + hitbox.Width * 0.5f
            : npc.PositionX + 8f;
    }

    private static float CenterY(in NpcSnapshot npc)
    {
        return TryGetHitbox(in npc, out VanillaNpcHitboxSize hitbox)
            ? npc.PositionY + hitbox.Height * 0.5f
            : npc.PositionY + 8f;
    }

    private static bool TryGetCenters(
        in NpcSnapshot first,
        in NpcSnapshot second,
        out float firstX,
        out float firstY,
        out float secondX,
        out float secondY)
    {
        firstX = CenterX(in first);
        firstY = CenterY(in first);
        secondX = CenterX(in second);
        secondY = CenterY(in second);
        return float.IsFinite(firstX) && float.IsFinite(firstY) && float.IsFinite(secondX) && float.IsFinite(secondY);
    }

    private static bool TryGetHitbox(in NpcSnapshot npc, out VanillaNpcHitboxSize hitbox)
    {
        hitbox = default;
        return VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) &&
               definition.TryResolveHitbox(npc.Simulation.Scale, out hitbox);
    }
}

internal static class RuntimeTownNpcSocialRandomExtensions1458
{
    public static int Next(this IRuntimeTownNpcSocialRandom1458 random, int inclusiveMin, int exclusiveMax)
    {
        if (exclusiveMax <= inclusiveMin)
            return inclusiveMin;
        return inclusiveMin + random.Next(exclusiveMax - inclusiveMin);
    }
}
