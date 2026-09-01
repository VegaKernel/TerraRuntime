using System.Buffers;
using System.Text;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaTownNpcIdentityState(short NpcSlot, string GivenName, int VariationIndex);

public enum TerrariaTownNpcIdentityEncodeResult : byte
{
    Encoded = 0,
    InvalidNpcSlot = 1,
    InvalidName = 2,
    FrameTooLarge = 3,
    Failed = 4
}

/// <summary>
/// Server-side encoder for TerrariaServer 1.4.5.8 packet 56 (UniqueTownNPCInfoSyncRequest response):
/// Int16 NPC slot, BinaryWriter string and Int32 townNpcVariationIndex.
/// </summary>
public static class TerrariaTownNpcIdentityCodec
{
    public const int MaximumNpcSlots = 200;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static TerrariaTownNpcIdentityEncodeResult TryEncode(
        in TerrariaTownNpcIdentityState state,
        out byte[] frame)
    {
        frame = [];
        if ((uint)state.NpcSlot >= MaximumNpcSlots)
            return TerrariaTownNpcIdentityEncodeResult.InvalidNpcSlot;
        if (state.GivenName is null)
            return TerrariaTownNpcIdentityEncodeResult.InvalidName;

        byte[] payload;
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true))
            {
                writer.Write(state.NpcSlot);
                writer.Write(state.GivenName);
                writer.Write(state.VariationIndex);
            }
            payload = stream.ToArray();
        }
        catch (EncoderFallbackException)
        {
            return TerrariaTownNpcIdentityEncodeResult.InvalidName;
        }

        var output = new ArrayBufferWriter<byte>(payload.Length + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            output,
            (byte)TerrariaMessageId.UniqueTownNpcInfoSyncRequest,
            payload);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
            return TerrariaTownNpcIdentityEncodeResult.FrameTooLarge;
        if (result != TerrariaFrameWriteResult.Written)
            return TerrariaTownNpcIdentityEncodeResult.Failed;

        frame = output.WrittenSpan.ToArray();
        return TerrariaTownNpcIdentityEncodeResult.Encoded;
    }
}
