using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public enum WorldGlobalTownNpcBootstrapPacketEncodeResult : byte
{
    Encoded = 0,
    InvalidNpcState = 1,
    FrameTooLarge = 2,
    FrameBudgetExceeded = 3
}

/// <summary>
/// Encodes the global persisted town-NPC baseline sent after all section synchronization.
/// Each active persisted town NPC contributes packet 23 followed by packet 54. The .wld file does
/// not persist live NPC buffs, so the startup packet-54 baseline intentionally contains no buffs.
/// </summary>
public static class WorldGlobalTownNpcBootstrapPacketEncoder
{
    // Packet 23 carries NpcSlot as one byte, so the wire can address slots 0..255.
    public const int MaximumTownNpcs = byte.MaxValue + 1;
    public const int FramesPerTownNpc = 2;
    public const int MaximumFrames = MaximumTownNpcs * FramesPerTownNpc;

    public static WorldGlobalTownNpcBootstrapPacketEncodeResult TryEncode(
        IReadOnlyList<WorldTownNpc> townNpcs,
        out ReadOnlyMemory<byte>[] frames)
    {
        ArgumentNullException.ThrowIfNull(townNpcs);
        if (townNpcs.Count > MaximumTownNpcs)
        {
            frames = [];
            return WorldGlobalTownNpcBootstrapPacketEncodeResult.FrameBudgetExceeded;
        }

        var encoded = new ReadOnlyMemory<byte>[checked(townNpcs.Count * FramesPerTownNpc)];
        int frameIndex = 0;
        for (int npcSlot = 0; npcSlot < townNpcs.Count; npcSlot++)
        {
            WorldTownNpcSyncPacketEncodeResult updateResult = WorldTownNpcSyncPacketEncoder.TryEncode(
                npcSlot,
                townNpcs[npcSlot],
                out ReadOnlyMemory<byte> updateFrame);
            if (updateResult != WorldTownNpcSyncPacketEncodeResult.Encoded)
            {
                frames = [];
                return updateResult == WorldTownNpcSyncPacketEncodeResult.FrameTooLarge
                    ? WorldGlobalTownNpcBootstrapPacketEncodeResult.FrameTooLarge
                    : WorldGlobalTownNpcBootstrapPacketEncodeResult.InvalidNpcState;
            }

            var buffs = new NpcUpdateBuff
            {
                NpcId = checked((short)npcSlot)
            };

            using var stream = new MemoryStream();
            buffs.ToStream(stream);
            if (stream.Length < TerrariaFrameDecoderOptions.MinimumFrameLength ||
                stream.Length > ushort.MaxValue)
            {
                frames = [];
                return WorldGlobalTownNpcBootstrapPacketEncodeResult.FrameTooLarge;
            }

            encoded[frameIndex++] = updateFrame;
            encoded[frameIndex++] = stream.ToArray();
        }

        frames = encoded;
        return WorldGlobalTownNpcBootstrapPacketEncodeResult.Encoded;
    }
}
