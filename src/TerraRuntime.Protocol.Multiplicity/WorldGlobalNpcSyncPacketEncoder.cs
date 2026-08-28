using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public enum WorldGlobalNpcSyncPacketEncodeResult : byte
{
    Encoded = 0,
    InvalidNpc = 1,
    FrameTooLarge = 2
}

/// <summary>
/// Encodes the persistence-backed global NPC baseline emitted after all packet-10 sections.
/// Vanilla sends each active NPC update (23) followed by its buff state (54). Persisted town NPCs
/// are the only NPCs currently available before the authoritative simulation owns live NPC state.
/// </summary>
public static class WorldGlobalNpcSyncPacketEncoder
{
    public static WorldGlobalNpcSyncPacketEncodeResult TryEncode(
        IReadOnlyList<WorldTownNpc> townNpcs,
        out ReadOnlyMemory<byte>[] frames)
    {
        ArgumentNullException.ThrowIfNull(townNpcs);
        var encodedFrames = new List<ReadOnlyMemory<byte>>(checked(townNpcs.Count * 2));

        for (int npcSlot = 0; npcSlot < townNpcs.Count; npcSlot++)
        {
            WorldTownNpcSyncPacketEncodeResult npcResult = WorldTownNpcSyncPacketEncoder.TryEncode(
                npcSlot,
                townNpcs[npcSlot],
                out ReadOnlyMemory<byte> npcFrame);
            if (npcResult != WorldTownNpcSyncPacketEncodeResult.Encoded)
            {
                frames = [];
                return WorldGlobalNpcSyncPacketEncodeResult.InvalidNpc;
            }

            encodedFrames.Add(npcFrame);

            var buffs = new NpcUpdateBuff
            {
                NpcId = checked((short)npcSlot)
            };

            using var stream = new MemoryStream();
            buffs.ToStream(stream);
            if (stream.Length < TerrariaFrameDecoderOptions.MinimumFrameLength || stream.Length > ushort.MaxValue)
            {
                frames = [];
                return WorldGlobalNpcSyncPacketEncodeResult.FrameTooLarge;
            }

            encodedFrames.Add(stream.ToArray());
        }

        frames = encodedFrames.ToArray();
        return WorldGlobalNpcSyncPacketEncodeResult.Encoded;
    }
}
