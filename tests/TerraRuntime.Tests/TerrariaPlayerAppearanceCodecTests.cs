using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaPlayerAppearanceCodecTests
{
    [Fact]
    public void Round_trips_packet4_through_multiplicity()
    {
        var appearance = new TerrariaPlayerAppearanceState(
            PlayerId: 7,
            SkinVariant: 3,
            VoiceVariant: 2,
            VoicePitchOffset: 0.25f,
            Hair: 12,
            Name: "Vega",
            HairDye: 4,
            HideVisibleAccessory: 0x1234,
            HideMisc: 0x05,
            HairColor: new TerrariaRgbColor(1, 2, 3),
            SkinColor: new TerrariaRgbColor(4, 5, 6),
            EyeColor: new TerrariaRgbColor(7, 8, 9),
            ShirtColor: new TerrariaRgbColor(10, 11, 12),
            UnderShirtColor: new TerrariaRgbColor(13, 14, 15),
            PantsColor: new TerrariaRgbColor(16, 17, 18),
            ShoeColor: new TerrariaRgbColor(19, 20, 21),
            DifficultyFlags: 2,
            TorchAndCartFlags: 3,
            ConsumableUnlockFlags: 4);

        byte[] encoded = TerrariaPlayerAppearanceCodec.Encode(in appearance);
        var input = new ReadOnlySequence<byte>(encoded);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.True(input.IsEmpty);
        Assert.Equal((byte)TerrariaMessageId.SyncPlayer, frame.MessageId);

        TerrariaPlayerAppearanceDecodeResult result = TerrariaPlayerAppearanceCodec.TryDecode(
            frame,
            out TerrariaPlayerAppearanceState decoded);

        Assert.Equal(TerrariaPlayerAppearanceDecodeResult.Decoded, result);
        Assert.Equal(appearance, decoded);
    }

    [Fact]
    public void Rejects_packet4_payload_above_runtime_bound()
    {
        byte[] oversized = new byte[TerrariaPlayerAppearanceCodec.MaximumPayloadLength + 4];
        int frameLength = oversized.Length;
        oversized[0] = (byte)frameLength;
        oversized[1] = (byte)(frameLength >> 8);
        oversized[2] = (byte)TerrariaMessageId.SyncPlayer;
        var input = new ReadOnlySequence<byte>(oversized);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.Equal(
            TerrariaPlayerAppearanceDecodeResult.InvalidPayloadLength,
            TerrariaPlayerAppearanceCodec.TryDecode(frame, out _));
    }
}
