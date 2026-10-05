using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    internal readonly record struct SocialEmotePlan(NpcHandle Npc, ushort Lifetime, byte Emote);

    // Source FindFrame runs after contact and collision, still before this actor's network update.
    private bool TryPlanPresentation(in NpcStateUpdate state, ReadOnlySpan<NpcSnapshot> peers,
        ref SocialPeerPlan? partner, Span<SocialEmotePlan> emotes, bool ownsRandomPreview,
        out int emoteCount, out NpcStateUpdate next)
    {
        next = FinishResidentPresentation(in state);
        emoteCount = 0;
        if (!VanillaTownNpcFrameCatalog1458.TryGet(new(state.Type), out int frames, out int extra, out int attack))
        { next = next with { Simulation = next.Simulation with { FrameIndex = null } }; return true; }
        NpcSimulationState simulation = next.Simulation;
        int socialBase = frames - attack;
        double clock = simulation.FrameCounter;
        int? index = simulation.FrameIndex;
        if (next.VelocityY != 0f) { index = 1; clock = 0d; }
        else if (next.Ai.Ai0 is 0f or 1f or 8f or 9f)
        {
            if (next.VelocityX == 0f) { index = 0; clock = 0d; }
            else if (index.HasValue)
            {
                index = Math.Max(2, index.Value);
                clock += Math.Abs(next.VelocityX) * 2f;
                clock++;
                if (clock > 6d) { index++; clock = 0d; }
                if (index >= frames - extra) index = 2;
            }
        }
        else if (next.Ai.Ai0 == 5f) { index = socialBase - 3; clock = 0d; }
        else if (next.Ai.Ai0 == 2f)
        {
            if (!index.HasValue) return false;
            clock++;
            if (index == socialBase - 1 && clock >= 5d || index == 0 && clock >= 40d)
            { index = index == 0 ? socialBase - 1 : 0; clock = 0d; }
            else if (index != 0 && index != socialBase - 1) { index = 0; clock = 0d; }
        }
        else if (next.Ai.Ai0 is 10f or 13f)
        {
            if (!index.HasValue) return false;
            clock++;
            if (index != 0 && (index < socialBase || index > socialBase + 3)) { index = 0; clock = 0d; }
            int windup = state.Type == VanillaNpcIds.Zoologist.Value ? 0 : 10;
            int phase = state.Type == VanillaNpcIds.Zoologist.Value ? 2 : 6;
            index = clock < windup || clock >= windup + phase * 4 ? 0 : socialBase + (int)((clock - windup) / phase);
        }
        else if (next.Ai.Ai0 is 3f or 4f or 16f or 17f)
        {
            if (!index.HasValue) return false;
            clock++;
            int offset = socialBase - index.Value;
            if (index != 0 && offset is not (1 or 2 or 4 or 5)) { index = 0; clock = 0d; }
            if (next.Ai.Ai0 is 3f or 4f)
            {
                if (next.Ai.Ai0 == 3f && clock is 70d or 216d or 320d)
                {
                    if (!ownsRandomPreview) return false;
                    NpcSnapshot? selected = null;
                    foreach (NpcSnapshot candidate in peers)
                        if (candidate.Handle.Slot == (int)next.Ai.Ai2) { selected = candidate; break; }
                    if (selected is not { } peer) return false;
                    NpcStateUpdate peerState = partner is { } plan && plan.Expected.Handle == peer.Handle
                        ? plan.Update : ToUpdate(in peer);
                    bool peerSpeaks = clock == 70d;
                    if (!VanillaTownNpcFrameCatalog1458.TryGet(peer.TypeIdentity, out _, out _, out _)) return false;
                    NpcStateUpdate emitter = peerSpeaks ? peerState : next;
                    NpcStateUpdate listener = peerSpeaks ? next : peerState;
                    if (peerSpeaks && socialNpcBuffOwner is not null &&
                        !socialNpcBuffOwner.TryGetSourceOrderedDebuffs(socialViewingNpc, peer.Handle, out socialNpcBuffFlags)) return false;
                    if (!TryContextualEmote(in emitter, in listener, peers, out byte emote)) return false;
                    emotes[0] = new(peerSpeaks ? peer.Handle : default, (ushort)(peerSpeaks ? 90 : clock == 216d ? 70 : 100), emote);
                    emoteCount = 1;
                }
                index = ConversationFrame(clock, socialBase, next.Ai.Ai0 == 3f);
            }
            else
            {
                index = RockPaperScissorsFrame(clock, socialBase);
                if (next.Ai.Ai0 == 16f && clock is 40d or 100d or 160d)
                {
                    NpcSnapshot? peer = null;
                    foreach (NpcSnapshot candidate in peers)
                        if (candidate.Handle.Slot == (int)next.Ai.Ai2) { peer = candidate; break; }
                    if (!peer.HasValue) return false;
                    NpcStateUpdate peerState = partner.HasValue && partner.Value.Expected.Handle == peer.Value.Handle
                        ? partner.Value.Update : ToUpdate(peer.Value);
                    NpcAiState own = simulation.LocalAi, other = peerState.Simulation.LocalAi;
                    int wins = (int)own.Ai2, losses = (int)own.Ai3;
                    int otherWins = (int)other.Ai2, otherLosses = (int)other.Ai3;
                    if (wins is < 0 or > 3 || losses is < 0 or > 3 || wins + losses > 3 ||
                        otherWins is < 0 or > 3 || otherLosses is < 0 or > 3) return false;
                    int round = clock == 40d ? 1 : clock == 100d ? 2 : 3;
                    int remaining = 3 - round, draws = 3 - wins - losses;
                    int outcome = -1;
                    for (int retry = 1; retry < 100 && outcome < 0; retry++)
                    {
                        outcome = random.Next(2);
                        if (outcome == 0 && otherWins >= losses || outcome == 1 && otherLosses >= wins) outcome = -1;
                        if (outcome == -1 && remaining <= draws) outcome = 2;
                    }
                    if (outcome == 0) { other = other with { Ai3 = other.Ai3 + 1f }; otherLosses++; }
                    if (outcome == 1) { other = other with { Ai2 = other.Ai2 + 1f }; otherWins++; }
                    int ownEmote = 38 - random.Next(3), peerEmote = ownEmote;
                    if (outcome == 0) peerEmote = ownEmote == 36 ? 38 : ownEmote - 1;
                    if (outcome == 1) peerEmote = ownEmote == 38 ? 36 : ownEmote + 1;
                    if (remaining == 0)
                    {
                        if (otherLosses >= 2) ownEmote -= 3;
                        if (otherWins >= 2) peerEmote -= 3;
                    }
                    partner = new(peer.Value, peerState with { Simulation = peerState.Simulation with { LocalAi = other } },
                        partner?.Force ?? false);
                    ushort lifetime = (ushort)(clock == 160d ? 75 : 45);
                    // Actor handle is supplied by the caller after its accepted commit.
                    emotes[0] = new(default, lifetime, (byte)ownEmote);
                    emotes[1] = new(peer.Value.Handle, lifetime, (byte)peerEmote);
                    emoteCount = 2;
                }
            }
            if (clock >= 420d) clock = 0d;
        }
        else index = null;
        next = next with { Simulation = simulation with { FrameCounter = clock, FrameIndex = index } };
        return true;
    }

    private static int RockPaperScissorsFrame(double clock, int basis)
    {
        if (clock < 10d || clock is 40d or 100d or 160d || clock >= 226d) return 0;
        if (clock < 40d) return basis - (((int)((clock - 10d) / 6d) % 2 == 0) ? 5 : 4);
        if (clock < 70d || clock >= 100d && clock < 130d || clock >= 160d && clock < 220d) return basis - 4;
        if (clock >= 220d) return basis - 5;
        double start = clock < 100d ? 70d : 130d;
        return basis - (((int)((clock - start) / 6d) % 2 == 0) ? 5 : 4);
    }

    private static int ConversationFrame(double clock, int basis, bool initiator)
    {
        if (initiator)
        {
            if (clock < 10d || clock >= 60d && clock <= 216d || clock >= 286d && clock <= 320d || clock >= 420d) return 0;
            if (clock < 16d || clock >= 46d && clock < 60d) return basis - 5;
            if (clock < 46d) return basis - 4;
            return clock < 286d ? (clock % 12d < 6d ? basis - 2 : 0) : (clock % 16d < 8d ? basis - 2 : 0);
        }
        if (clock <= 70d || clock >= 200d && clock < 320d || clock >= 326d) return 0;
        if (clock < 160d) return clock % 16d < 8d ? basis - 2 : 0;
        if (clock < 166d || clock >= 186d && clock < 200d) return basis - 5;
        if (clock < 186d) return basis - 4;
        return basis - 1;
    }
}
