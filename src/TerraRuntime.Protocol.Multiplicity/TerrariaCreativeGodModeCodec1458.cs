using System.Buffers.Binary;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// TerrariaServer 1.4.5.8 / protocol 326 encoder for CreativePowers.GodmodePower.
/// Source backing:
/// - NetworkInitializer registers NetCreativePowersModule as net-module id 6.
/// - CreativePowerManager registers GodmodePower as creative-power id 5.
/// - APerPlayerTogglePower owns 255 player states and uses SyncEveryone(0) / SyncOnePlayer(1).
/// </summary>
public static class TerrariaCreativeGodModeCodec1458
{
    public const ushort NetCreativePowersModuleId = 6;
    public const ushort GodModePowerId = 5;
    public const byte SyncEveryoneSubMessage = 0;
    public const byte SyncOnePlayerSubMessage = 1;
    public const int PlayerStateCount = 255;
    public const int SyncEveryoneByteCount = 32;

    private const int NetModuleHeaderLength = sizeof(ushort);
    private const int PowerIdLength = sizeof(ushort);

    public static byte[] EncodeSyncOnePlayer(PlayerSlotId player, bool enabled)
    {
        if (player.Value >= PlayerStateCount)
            throw new ArgumentOutOfRangeException(nameof(player), player, "Vanilla creative powers expose player slots 0..254.");

        const int statePayloadLength = sizeof(byte) + sizeof(byte) + sizeof(byte);
        Span<byte> payload = stackalloc byte[NetModuleHeaderLength + PowerIdLength + statePayloadLength];
        WritePrefix(payload);
        int offset = NetModuleHeaderLength + PowerIdLength;
        payload[offset] = SyncOnePlayerSubMessage;
        payload[offset + 1] = player.Value;
        payload[offset + 2] = enabled ? (byte)1 : (byte)0;
        return EncodeFrame(payload);
    }

    public static byte[] EncodeSyncEveryone(ReadOnlySpan<bool> enabledByPlayer)
    {
        if (enabledByPlayer.Length != PlayerStateCount)
            throw new ArgumentException($"Exactly {PlayerStateCount} vanilla creative player states are required.", nameof(enabledByPlayer));

        Span<byte> payload = stackalloc byte[NetModuleHeaderLength + PowerIdLength + sizeof(byte) + SyncEveryoneByteCount];
        WritePrefix(payload);
        int offset = NetModuleHeaderLength + PowerIdLength;
        payload[offset++] = SyncEveryoneSubMessage;
        payload[offset..].Clear();

        for (int playerIndex = 0; playerIndex < PlayerStateCount; playerIndex++)
        {
            if (!enabledByPlayer[playerIndex])
                continue;
            int byteIndex = playerIndex >> 3;
            int bitIndex = playerIndex & 7;
            payload[offset + byteIndex] |= checked((byte)(1 << bitIndex));
        }

        return EncodeFrame(payload);
    }

    private static void WritePrefix(Span<byte> payload)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(payload, NetCreativePowersModuleId);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[NetModuleHeaderLength..], GodModePowerId);
    }

    private static byte[] EncodeFrame(ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length];
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            frame,
            (byte)TerrariaMessageId.LoadNetModule,
            payload);
        if (result != TerrariaFrameWriteResult.Written)
            throw new InvalidOperationException($"Failed to encode vanilla creative godmode frame: {result}.");
        return frame;
    }
}
