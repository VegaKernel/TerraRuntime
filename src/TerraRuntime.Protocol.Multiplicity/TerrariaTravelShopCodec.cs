using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaTravelShopDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2
}

public enum TerrariaTravelShopEncodeResult : byte
{
    Encoded = 0,
    InvalidInventoryLength = 1,
    InvalidItemId = 2,
    FrameTooLarge = 3,
    Failed = 4
}

/// <summary>
/// Wire adapter for TerrariaServer 1.4.5.8 packet 72 (SyncTravelingMerchant).
/// The payload is exactly forty little-endian Int16 item identities, matching Main.travelShop.
/// </summary>
public static class TerrariaTravelShopCodec
{
    public const int SlotCount = 40;
    public const int PayloadLength = SlotCount * sizeof(short);

    public static TerrariaTravelShopDecodeResult TryDecode(in TerrariaFrame frame, Span<short> itemIds)
    {
        if (frame.MessageId != (byte)TerrariaMessageId.TravelShop)
            return TerrariaTravelShopDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength || itemIds.Length < SlotCount)
            return TerrariaTravelShopDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
        {
            DecodePayload(frame.Payload.FirstSpan, itemIds);
            return TerrariaTravelShopDecodeResult.Decoded;
        }

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }
        DecodePayload(scratch, itemIds);
        return TerrariaTravelShopDecodeResult.Decoded;
    }

    public static TerrariaTravelShopEncodeResult TryEncode(ReadOnlySpan<short> itemIds, out byte[] frame)
    {
        if (itemIds.Length != SlotCount)
        {
            frame = [];
            return TerrariaTravelShopEncodeResult.InvalidInventoryLength;
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        for (int i = 0; i < SlotCount; i++)
        {
            if (itemIds[i] < 0)
            {
                frame = [];
                return TerrariaTravelShopEncodeResult.InvalidItemId;
            }
            BinaryPrimitives.WriteInt16LittleEndian(payload.Slice(i * sizeof(short), sizeof(short)), itemIds[i]);
        }

        var writer = new ArrayBufferWriter<byte>(PayloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.TravelShop,
            payload);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
        {
            frame = [];
            return TerrariaTravelShopEncodeResult.FrameTooLarge;
        }
        if (result != TerrariaFrameWriteResult.Written)
        {
            frame = [];
            return TerrariaTravelShopEncodeResult.Failed;
        }

        frame = writer.WrittenSpan.ToArray();
        return TerrariaTravelShopEncodeResult.Encoded;
    }

    private static void DecodePayload(ReadOnlySpan<byte> payload, Span<short> itemIds)
    {
        for (int i = 0; i < SlotCount; i++)
            itemIds[i] = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(i * sizeof(short), sizeof(short)));
    }
}
