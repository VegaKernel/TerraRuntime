using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class PlayerJoinFrameEncoderTests
{
    [Fact]
    public void Continue_connecting_is_encoded_as_a_complete_packet_3_frame()
    {
        byte[] frame = PlayerJoinFrameEncoder.EncodeContinueConnecting(new PlayerSlotId(7));

        AssertValidFrame(frame, TerrariaMessageId.PlayerInfo);
        Assert.True(frame.Length > TerrariaFrameDecoderOptions.MinimumFrameLength);
    }

    [Fact]
    public void Continue_connecting_preserves_slot_and_server_flag_payload()
    {
        byte[] frame = PlayerJoinFrameEncoder.EncodeContinueConnecting(
            new PlayerSlotId(17),
            serverSpecialFlag2: true);

        Assert.Equal(new byte[] { 5, 0, (byte)TerrariaMessageId.PlayerInfo, 17, 1 }, frame);
    }

    [Fact]
    public void Status_is_encoded_as_a_complete_packet_9_frame()
    {
        byte[] frame = PlayerJoinFrameEncoder.EncodeStatus(sectionCount: 42);

        AssertValidFrame(frame, TerrariaMessageId.StatusTextSize);
        Assert.True(frame.Length > TerrariaFrameDecoderOptions.MinimumFrameLength);
    }

    [Fact]
    public void Status_rejects_negative_work_count_before_reaching_packet_model()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerJoinFrameEncoder.EncodeStatus(-1));
    }

    private static void AssertValidFrame(byte[] frame, TerrariaMessageId messageId)
    {
        Assert.True(frame.Length >= TerrariaFrameDecoderOptions.MinimumFrameLength);
        Assert.Equal(frame.Length, frame[0] | (frame[1] << 8));
        Assert.Equal((byte)messageId, frame[2]);
    }
}
