#!/usr/bin/env python3
from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8-sig')

def write(path, content):
    p=Path(path); p.parent.mkdir(parents=True, exist_ok=True); p.write_text(content, encoding='utf-8')

def replace_once(path, old, new, label):
    text=read(path)
    if text.count(old)!=1:
        raise SystemExit(f'{label}: expected one anchor, found {text.count(old)}')
    write(path, text.replace(old,new,1))

codec = r'''using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaEmoteBubbleState(
    int BubbleId,
    byte AnchorType,
    ushort AnchorIndex,
    ushort Lifetime,
    byte Emote)
{
    public const byte NpcAnchor = 0;
    public const byte PlayerAnchor = 1;
    public const byte ProjectileAnchor = 2;
    public const byte RemoveAnchor = 255;

    public bool IsCreate => AnchorType is NpcAnchor or PlayerAnchor or ProjectileAnchor;
    public bool IsRemove => AnchorType == RemoveAnchor;
    public bool IsValid => IsRemove || (IsCreate && Lifetime > 0);
}

public enum TerrariaEmoteBubbleDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    InvalidState = 3
}

public enum TerrariaEmoteBubbleEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    FrameTooLarge = 2,
    Failed = 3
}

/// <summary>
/// Protocol-326 packet 91 adapter for the source-backed positive-emote subset used by Town NPC social AI.
/// NPC/player/projectile anchors use the exact vanilla 0/1/2 tags. Removal uses anchor 255 and the five-byte
/// payload. Negative emotes with metadata remain outside this slice because AI_007 RPS only emits 33..38.
/// </summary>
public static class TerrariaEmoteBubbleCodec
{
    public const int RemovePayloadLength = 5;
    public const int CreatePayloadLength = 10;

    public static TerrariaEmoteBubbleDecodeResult TryDecode(in TerrariaFrame frame, out TerrariaEmoteBubbleState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.EmoteBubble)
            return TerrariaEmoteBubbleDecodeResult.WrongMessageId;
        if (frame.Payload.Length is not RemovePayloadLength and not CreatePayloadLength)
            return TerrariaEmoteBubbleDecodeResult.InvalidPayloadLength;

        Span<byte> scratch = stackalloc byte[CreatePayloadLength];
        ReadOnlySpan<byte> payload;
        if (frame.Payload.IsSingleSegment)
        {
            payload = frame.Payload.FirstSpan;
        }
        else
        {
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(scratch[offset..]);
                offset += segment.Length;
            }
            payload = scratch[..checked((int)frame.Payload.Length)];
        }

        int id = BinaryPrimitives.ReadInt32LittleEndian(payload[..4]);
        byte anchorType = payload[4];
        if (anchorType == TerrariaEmoteBubbleState.RemoveAnchor)
        {
            if (payload.Length != RemovePayloadLength)
                return TerrariaEmoteBubbleDecodeResult.InvalidPayloadLength;
            state = new TerrariaEmoteBubbleState(id, anchorType, 0, 0, 0);
            return TerrariaEmoteBubbleDecodeResult.Decoded;
        }
        if (payload.Length != CreatePayloadLength)
            return TerrariaEmoteBubbleDecodeResult.InvalidPayloadLength;

        state = new TerrariaEmoteBubbleState(
            id,
            anchorType,
            BinaryPrimitives.ReadUInt16LittleEndian(payload[5..7]),
            BinaryPrimitives.ReadUInt16LittleEndian(payload[7..9]),
            payload[9]);
        return state.IsValid
            ? TerrariaEmoteBubbleDecodeResult.Decoded
            : TerrariaEmoteBubbleDecodeResult.InvalidState;
    }

    public static TerrariaEmoteBubbleEncodeResult TryEncode(in TerrariaEmoteBubbleState state, out byte[] frame)
    {
        if (!state.IsValid)
        {
            frame = [];
            return TerrariaEmoteBubbleEncodeResult.InvalidState;
        }

        int payloadLength = state.IsRemove ? RemovePayloadLength : CreatePayloadLength;
        Span<byte> payload = stackalloc byte[payloadLength];
        BinaryPrimitives.WriteInt32LittleEndian(payload[..4], state.BubbleId);
        payload[4] = state.AnchorType;
        if (!state.IsRemove)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload[5..7], state.AnchorIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload[7..9], state.Lifetime);
            payload[9] = state.Emote;
        }

        var writer = new ArrayBufferWriter<byte>(payloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.EmoteBubble,
            payload);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
        {
            frame = [];
            return TerrariaEmoteBubbleEncodeResult.FrameTooLarge;
        }
        if (result != TerrariaFrameWriteResult.Written)
        {
            frame = [];
            return TerrariaEmoteBubbleEncodeResult.Failed;
        }

        frame = writer.WrittenSpan.ToArray();
        return TerrariaEmoteBubbleEncodeResult.Encoded;
    }
}
'''
write('src/TerraRuntime.Protocol.Multiplicity/TerrariaEmoteBubbleCodec.cs', codec)

social = r'''using TerraRuntime.Contracts.Gameplay;
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

    private static bool TryGetHitbox(in NpcSnapshot npc, out VanillaNpcHitboxSize hitbox) =>
        VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) &&
        definition.TryResolveHitbox(npc.Simulation.Scale, out hitbox);
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
'''
write('src/TerraRuntime/RuntimeTownNpcSocial1458.cs', social)

# enum
replace_once(
    'src/TerraRuntime.Protocol/TerrariaMessageId.cs',
    '    SyncPlayerChestIndex = 80,\n    LoadNetModule = 82,',
    '    SyncPlayerChestIndex = 80,\n    LoadNetModule = 82,\n    EmoteBubble = 91,',
    'packet 91 enum')

# replication interface + method
replace_once(
    'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs',
    'internal sealed class RuntimeNpcReplicationRegistry : INpcStateCommitSink, IRuntimePlayerEventSink',
    'internal sealed class RuntimeNpcReplicationRegistry : INpcStateCommitSink, IRuntimePlayerEventSink, IRuntimeTownNpcEmoteSink1458',
    'emote sink interface')
replace_once(
    'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs',
    '''    public bool TryPublishNpcTalk(ConnectionHandle connection, short npcSlot)\n    {\n        if (!connection.IsAssigned || !TerrariaNpcTalkCodec.IsValidNpcSlot(npcSlot))\n            return false;\n        var state = new TerrariaNpcTalkState(connection.Player.Slot.Value, npcSlot);\n        if (TerrariaNpcTalkCodec.TryEncode(in state, out byte[] encoded) != TerrariaNpcTalkEncodeResult.Encoded)\n            return false;\n        BroadcastExcept(connection.Source, encoded);\n        return true;\n    }\n''',
    '''    public bool TryPublishNpcTalk(ConnectionHandle connection, short npcSlot)\n    {\n        if (!connection.IsAssigned || !TerrariaNpcTalkCodec.IsValidNpcSlot(npcSlot))\n            return false;\n        var state = new TerrariaNpcTalkState(connection.Player.Slot.Value, npcSlot);\n        if (TerrariaNpcTalkCodec.TryEncode(in state, out byte[] encoded) != TerrariaNpcTalkEncodeResult.Encoded)\n            return false;\n        BroadcastExcept(connection.Source, encoded);\n        return true;\n    }\n\n    public bool TryPublishEmoteBubble(in TerrariaEmoteBubbleState state)\n    {\n        if (TerrariaEmoteBubbleCodec.TryEncode(in state, out byte[] encoded) != TerrariaEmoteBubbleEncodeResult.Encoded)\n        {\n            Interlocked.Increment(ref unsupportedCommits);\n            return false;\n        }\n        Broadcast(encoded);\n        return true;\n    }\n''',
    'replication bubble publish')

# server wiring
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n    private readonly RuntimeTownNpcCombat1458? _townCombat;',
    '    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n    private readonly RuntimeTownNpcSocial1458? _townSocial;\n    private readonly RuntimeTownNpcCombat1458? _townCombat;',
    'server social field')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '            _townSchedule = new RuntimeTownNpcSchedule1458(townNpcs, _npcs, worldTiles);\n            _townShimmer = new RuntimeTownNpcShimmerService1458(_npcs, townNpcs, worldTiles, npcReplication);',
    '            _townSchedule = new RuntimeTownNpcSchedule1458(townNpcs, _npcs, worldTiles);\n            _townSocial = new RuntimeTownNpcSocial1458(townNpcs, _npcs, worldTiles, this, npcReplication, _townSchedule);\n            _townShimmer = new RuntimeTownNpcShimmerService1458(_npcs, townNpcs, worldTiles, npcReplication);',
    'server social construction')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '        if (_townMoveIn is null && _townSchedule is null && _townCombat is null)\n            return;',
    '        if (_townMoveIn is null && _townSchedule is null && _townSocial is null && _townCombat is null)\n            return;',
    'server social lifecycle gate')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '        _townCombat?.Tick();\n    }',
    '        _townSocial?.Tick();\n        _townCombat?.Tick();\n    }',
    'server social tick ordering')

checker = r'''#!/usr/bin/env python3
import argparse, re
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--npc', required=True)
p.add_argument('--netmessage', required=True)
p.add_argument('--messagebuffer', required=True)
p.add_argument('--emotebubble', required=True)
a=p.parse_args()

def text(path): return Path(path).read_text(encoding='utf-8-sig')
def require(haystack, pattern, label):
    if re.search(pattern, haystack, re.S) is None:
        raise SystemExit(f'missing source contract: {label}')

npc=text(a.npc); net=text(a.netmessage); mb=text(a.messagebuffer); eb=text(a.emotebubble)
require(npc, r'Main\.rand\.Next\(300\) == 0.*?ai\[0\] = 3f;.*?nPC4\.ai\[0\] = 4f;', 'AI_007 conversation pair 3/4')
require(npc, r'Main\.rand\.Next\(1800\) == 0.*?ai\[0\] = 16f;.*?localAI\[2\] = Main\.rand\.Next\(4\);.*?nPC5\.ai\[0\] = 17f;', 'AI_007 RPS pair 16/17')
require(npc, r'type == 208.*?ai\[0\] = 6f;.*?ai\[1\] = num106;', 'Party Girl player state 6')
require(npc, r'type == 550.*?ai\[0\] = 18f;.*?ai\[1\] = num110;', 'Tavernkeep player state 18')
require(npc, r'Main\.rand\.Next\(1800\) == 0\).*?ai\[0\] = 2f;.*?ai\[1\] = 45 \* Main\.rand\.Next\(1, 2\);', 'ordinary idle state 2')
require(npc, r'type == 229.*?ai\[0\] = 11f;.*?ai\[1\] = 30 \* Main\.rand\.Next\(1, 4\);', 'Pirate idle state 11')
require(npc, r'Main\.rand\.Next\(1200\) == 0\).*?ai\[0\] = 7f;.*?ai\[1\] = num114;', 'generic player reaction state 7')
require(npc, r'else if \(ai\[0\] == 2f \|\| ai\[0\] == 11f\).*?localAI\[3\]--;.*?ai\[1\]--;.*?velocity\.X \*= 0\.8f;', 'idle timer behavior')
require(npc, r'ai\[0\] == 3f \|\| ai\[0\] == 4f.*?ai\[0\] == 16f \|\| ai\[0\] == 17f.*?ai\[0\] == 20f.*?ai\[0\] == 23f.*?velocity\.X \*= 0\.8f;.*?ai\[1\]--;', 'conversation and pet timer group')
require(npc, r'ai\[0\] == 6f \|\| ai\[0\] == 7f \|\| ai\[0\] == 18f \|\| ai\[0\] == 19f.*?Distance\(base\.Center\) > 200f.*?Collision\.CanHitLine', 'player state keep-distance/LOS')
require(npc, r'AI_007_AttemptToPlayIdleAnimationsForPets\(int petIdleChance\).*?type == 638.*?num = 2;.*?IsTownSlime\[type\].*?num = 0;.*?ai\[0\] = \(\(num == 0\) \? 20 : Main\.rand\.Next\(20, 20 \+ num\)\);', 'pet idle state selection')
require(npc, r'ai\[0\] == 20f && type == 637.*?500 \+ Main\.rand\.Next\(200\).*?ai\[0\] == 21f && type == 638.*?100 \+ Main\.rand\.Next\(100\).*?ai\[0\] == 22f && type == 656.*?200 \+ Main\.rand\.Next\(200\).*?IsTownSlime\[type\].*?180 \+ Main\.rand\.Next\(240\)', 'pet idle durations')
require(npc, r'ai\[0\] == 16f \|\| ai\[0\] == 17f.*?frameCounter == 40\.0.*?num98 = 45;.*?frameCounter == 100\.0.*?num98 = 45;.*?frameCounter != 160\.0.*?num98 = 75;', 'RPS bubble frame cadence')
require(npc, r'num108 = Utils\.SelectRandom<int>\(Main\.rand, 38, 37, 36\).*?EmoteBubble\.NewBubble\(num108.*?EmoteBubble\.NewBubble\(num109', 'RPS explicit emotes')
require(net, r'case 91:.*?writer\.Write\(number\);.*?writer\.Write\(\(byte\)number2\);.*?writer\.Write\(\(ushort\)number3\);.*?writer\.Write\(\(ushort\)number4\);.*?writer\.Write\(\(byte\)number5\);', 'packet 91 wire writer')
require(mb, r'case 91:.*?ReadInt32\(\).*?ReadByte\(\).*?ReadUInt16\(\).*?ReadUInt16\(\).*?ReadByte\(\).*?DeserializeNetAnchor', 'packet 91 client reader')
require(eb, r'anch\.entity is NPC.*?item = 0;.*?anch\.entity is Player.*?item = 1;.*?anch\.entity is Projectile.*?item = 2;', 'emote anchor tags')
print('Town NPC social/emote TerrariaServer 1.4.5.8 source contract OK')
'''
write('tools/ci/check_town_social_emotes_source.py', checker)

workflow = r'''name: Town NPC Social Emotes Source Contract

on:
  push:
    branches: [main]
    paths:
      - 'src/TerraRuntime/RuntimeTownNpcSocial1458.cs'
      - 'src/TerraRuntime.Protocol.Multiplicity/TerrariaEmoteBubbleCodec.cs'
      - 'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs'
      - 'tools/ci/check_town_social_emotes_source.py'
  pull_request:
    paths:
      - 'src/TerraRuntime/RuntimeTownNpcSocial1458.cs'
      - 'src/TerraRuntime.Protocol.Multiplicity/TerrariaEmoteBubbleCodec.cs'
      - 'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs'
      - 'tools/ci/check_town_social_emotes_source.py'
  workflow_dispatch:

jobs:
  source-contract:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 11.0.100-preview.7.26381.103
      - name: Decompile pinned TerrariaServer 1.4.5.8
        run: bash tools/decompile-reference.sh 1458
      - name: Verify social/emote source contract
        run: |
          python3 tools/ci/check_town_social_emotes_source.py \
            --npc decompiled/1458/Terraria/NPC.cs \
            --netmessage decompiled/1458/Terraria/NetMessage.cs \
            --messagebuffer decompiled/1458/Terraria/MessageBuffer.cs \
            --emotebubble decompiled/1458/Terraria.GameContent.UI/EmoteBubble.cs
      - name: Build Release
        run: dotnet build TerraRuntime.slnx -c Release
'''
write('.github/workflows/town-social-emotes-source-contract.yml', workflow)

# docs
for path, addition in [
('docs/en/town-npc-combat.md', '''\n\n## AI_007 social/emote vertical\n\nTown social state is now server-owned alongside combat. The runtime covers ordinary conversation pairs (3/4), RPS pairs (16/17), passive idle states (2/11), player-facing states (6/7/18/19), and source-shaped Town Pet idle states (20..23). RPS bubbles are emitted as protocol-326 packet 91 with vanilla NPC anchor tag 0 and the source frame cadence 40/100/160. Chair state 5 remains owned by the schedule service. NPC-picked free-form conversation bubbles still depend on Terraria's broader `PickNPCEmote` content graph and are not claimed by this slice.\n'''),
('docs/ru/town-npc-combat.md', '''\n\n## Social/emote-вертикаль AI_007\n\nСоциальное состояние Town NPC теперь также принадлежит серверу. Runtime поддерживает обычные разговорные пары (3/4), RPS-пары (16/17), пассивные idle-состояния (2/11), реакции на игрока (6/7/18/19) и source-shaped idle-состояния Town Pet (20..23). RPS-пузыри отправляются настоящим packet 91 protocol-326 с vanilla NPC anchor 0 и исходным cadence на кадрах 40/100/160. Chair state 5 остаётся во владении schedule-сервиса. Свободные NPC-picked реплики через полный граф `PickNPCEmote` этим блоком пока не заявляются.\n''')]:
    content=read(path)
    if addition.strip() not in content:
        content += addition
    write(path, content)

road='docs/roadmap/npc-ai-parity.md'
t=read(road)
old='  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, projectile combat for Merchant/Nurse/Arms Dealer/Guide, and melee state 15 for Dye Trader/TaxCollector/Stylist are authoritative; social/emote and remaining special town branches remain open;'
# tolerate the exact current spelling as merged by PR #90
old2='  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, projectile combat for Merchant/Nurse/Arms Dealer/Guide, and melee state 15 for Dye Trader/Tax Collector/Stylist are authoritative; social/emote and remaining special town branches remain open;'
new='  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, projectile combat for Merchant/Nurse/Arms Dealer/Guide, melee state 15 for Dye Trader/Tax Collector/Stylist, and social/emote states 2/3/4/6/7/11/16/17/18/19/20..23 are authoritative; support/magic projectile and remaining special town branches remain open;'
if old in t:
    t=t.replace(old,new,1)
elif old2 in t:
    t=t.replace(old2,new,1)
elif new not in t:
    raise SystemExit('roadmap social anchor missing')
write(road,t)

tests = r'''using System.Buffers;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcSocial1458Tests
{
    [Fact]
    public void Packet91_roundtrips_source_positive_npc_bubble_shape()
    {
        var state = new TerrariaEmoteBubbleState(42, TerrariaEmoteBubbleState.NpcAnchor, 17, 45, 38);
        Assert.Equal(TerrariaEmoteBubbleEncodeResult.Encoded, TerrariaEmoteBubbleCodec.TryEncode(in state, out byte[] encoded));
        Assert.Equal(13, encoded.Length);
        Assert.Equal((byte)TerrariaMessageId.EmoteBubble, encoded[2]);

        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(TerrariaEmoteBubbleDecodeResult.Decoded, TerrariaEmoteBubbleCodec.TryDecode(in frame, out TerrariaEmoteBubbleState decoded));
        Assert.Equal(state, decoded);
    }

    [Fact]
    public void Ordinary_conversation_commits_source_state_three_and_peer_state_four()
    {
        SocialFixture f = SocialFixture.Create([VanillaNpcIds.Merchant, VanillaNpcIds.Nurse]);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));

        Assert.True(f.Social.TryStartConversationForTesting(source.Handle, rpsGame: false));
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.True(f.Npcs.TryGetActive(1, out NpcSnapshot peer));
        Assert.Equal(3f, source.Ai.Ai0);
        Assert.Equal(4f, peer.Ai.Ai0);
        Assert.Equal(1f, source.Ai.Ai2);
        Assert.Equal(0f, peer.Ai.Ai2);
        Assert.Equal(420f, source.Ai.Ai1);
        Assert.Equal(source.Ai.Ai1, peer.Ai.Ai1);
        Assert.Equal(1, source.Simulation.DirectionX);
        Assert.Equal(-1, peer.Simulation.DirectionX);
    }

    [Fact]
    public void Rps_pair_emits_two_packet91_bubbles_on_source_tick_forty()
    {
        var emotes = new RecordingEmotes();
        SocialFixture f = SocialFixture.Create(
            [VanillaNpcIds.Merchant, VanillaNpcIds.Nurse],
            emotes: emotes,
            random: new ZeroRandom());
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));
        Assert.True(f.Social.TryStartConversationForTesting(source.Handle, rpsGame: true));

        for (int i = 0; i < 39; i++)
            f.Social.Tick();
        Assert.Empty(emotes.States);

        RuntimeTownNpcSocialTickSummary1458 tick = f.Social.Tick();
        Assert.Equal(2, tick.BubblesPublished);
        Assert.Equal(2, emotes.States.Count);
        Assert.All(emotes.States, x => Assert.Equal(TerrariaEmoteBubbleState.NpcAnchor, x.AnchorType));
        Assert.All(emotes.States, x => Assert.Equal((ushort)45, x.Lifetime));
        Assert.Contains(emotes.States, x => x.AnchorIndex == 0);
        Assert.Contains(emotes.States, x => x.AnchorIndex == 1);
        Assert.All(emotes.States, x => Assert.InRange(x.Emote, (byte)36, (byte)38));
    }

    [Fact]
    public void Player_facing_state_resets_when_generation_safe_player_disappears()
    {
        var players = new MutablePlayers(CreatePlayer(0, 220f, 160f));
        SocialFixture f = SocialFixture.Create([VanillaNpcIds.Merchant], players: players);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));
        Assert.True(f.Social.TryStartPlayerStateForTesting(source.Handle, 7f, 220));
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.Equal(7f, source.Ai.Ai0);
        Assert.Equal(0f, source.Ai.Ai2);
        Assert.Equal(1, source.Simulation.DirectionX);

        players.Clear();
        f.Social.Tick();
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.Equal(0f, source.Ai.Ai0);
        Assert.Equal(0f, source.Ai.Ai2);
        Assert.InRange(source.Ai.Ai1, 60f, 119f);
    }

    [Theory]
    [InlineData(637, 20, 500)]
    [InlineData(638, 20, 200)]
    [InlineData(656, 20, 200)]
    [InlineData(670, 20, 180)]
    public void Pet_idle_entry_matches_source_state_selection_and_type_specific_duration(
        int typeValue,
        int expectedState,
        int expectedDuration)
    {
        SocialFixture f = SocialFixture.Create([new NpcTypeId(typeValue)]);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));
        Assert.True(f.Social.TryStartPetIdleForTesting(source.Handle));
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.Equal(expectedState, source.Ai.Ai0);
        Assert.Equal(expectedDuration, source.Ai.Ai1);
        Assert.Equal(0f, source.Ai.Ai2);
        Assert.Equal(0f, source.Simulation.LocalAi.Ai3);
    }

    [Fact]
    public void Rps_state_times_out_back_to_wander_and_clears_peer_reference()
    {
        SocialFixture f = SocialFixture.Create([VanillaNpcIds.Merchant, VanillaNpcIds.Nurse]);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));
        Assert.True(f.Social.TryStartConversationForTesting(source.Handle, rpsGame: true));
        for (int i = 0; i < 420; i++)
            f.Social.Tick();
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.Equal(0f, source.Ai.Ai0);
        Assert.Equal(0f, source.Ai.Ai2);
        Assert.InRange(source.Ai.Ai1, 60f, 119f);
    }

    private static PlayerStateSnapshot CreatePlayer(byte slot, float x, float y) => new(
        new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1)),
        new PlayerStateRevision(1),
        Team: 0,
        ControlFlags: 0,
        MovementFlags: 0,
        MiscFlags1: 0,
        MiscFlags2: 0,
        SelectedItem: 0,
        PositionX: x,
        PositionY: y,
        VelocityX: 0f,
        VelocityY: 0f,
        MountType: 0,
        PotionOfReturnOriginalPositionX: 0f,
        PotionOfReturnOriginalPositionY: 0f,
        PotionOfReturnHomePositionX: 0f,
        PotionOfReturnHomePositionY: 0f,
        CameraTargetX: 0f,
        CameraTargetY: 0f)
    {
        HasHealth = true,
        Life = 100,
        MaxLife = 100,
        IsDead = false
    };

    private sealed class SocialFixture
    {
        private SocialFixture(RuntimeNpcStore npcs, RuntimeTownNpcSocial1458 social)
        {
            Npcs = npcs;
            Social = social;
        }
        public RuntimeNpcStore Npcs { get; }
        public RuntimeTownNpcSocial1458 Social { get; }

        public static SocialFixture Create(
            NpcTypeId[] types,
            MutablePlayers? players = null,
            RecordingEmotes? emotes = null,
            IRuntimeTownNpcSocialRandom1458? random = null)
        {
            var tiles = new WorldTileStore(new WorldDimensions(120, 80));
            var residents = new WorldTownNpc[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                residents[i] = new WorldTownNpc(
                    types[i].Value,
                    $"Town{i}",
                    160f + i * 60f,
                    160f,
                    true,
                    10 + i * 4,
                    14,
                    null,
                    false);
            }
            var persistence = new WorldNpcPersistence([], residents, []);
            var town = new RuntimeTownNpcStateStore(persistence, [], tiles.Dimensions);
            var npcs = new RuntimeNpcStore();
            Assert.True(town.TryReserveRuntimeSlots(npcs));
            var social = new RuntimeTownNpcSocial1458(
                town,
                npcs,
                tiles,
                players ?? new MutablePlayers(),
                emotes,
                schedule: null,
                random ?? new ZeroRandom());
            return new SocialFixture(npcs, social);
        }
    }

    private sealed class MutablePlayers(params PlayerStateSnapshot[] initial) : IRuntimePlayerSlotSnapshotLookup
    {
        private readonly Dictionary<byte, PlayerStateSnapshot> players = initial.ToDictionary(x => x.Player.Slot.Value);
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot) => players.TryGetValue(slot.Value, out snapshot);
        public void Clear() => players.Clear();
    }

    private sealed class RecordingEmotes : IRuntimeTownNpcEmoteSink1458
    {
        public List<TerrariaEmoteBubbleState> States { get; } = [];
        public bool TryPublishEmoteBubble(in TerrariaEmoteBubbleState state)
        {
            States.Add(state);
            return true;
        }
    }

    private sealed class ZeroRandom : IRuntimeTownNpcSocialRandom1458
    {
        public int Next(int exclusiveMax) => 0;
    }
}
'''
write('tests/TerraRuntime.Tests/RuntimeTownNpcSocial1458Tests.cs', tests)

print('N4 Town NPC social/emote block applied')
