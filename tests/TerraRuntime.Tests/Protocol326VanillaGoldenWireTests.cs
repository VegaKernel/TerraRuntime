using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

/// <summary>
/// Independent wire fixtures transcribed from the official TerrariaServer 1.4.5.8 decompile:
/// NetMessage.SendData cases 17, 19, 47 and 79, plus matching MessageBuffer.GetData cases 17, 19, 46, 47 and 79.
/// These vectors intentionally do not use Multiplicity or TerraRuntime encoders to construct expected bytes.
/// </summary>
public sealed class Protocol326VanillaGoldenWireTests
{
    [Fact]
    public void Packet17_tile_manipulation_matches_official_wire_vector()
    {
        var state = new TerrariaTileManipulationState(5, -123, 456, 789, 11);
        byte[] expected = [0x0B, 0x00, 0x11, 0x05, 0x85, 0xFF, 0xC8, 0x01, 0x15, 0x03, 0x0B];

        Assert.Equal(
            TerrariaTileManipulationEncodeResult.Encoded,
            TerrariaTileManipulationCodec.TryEncode(in state, out byte[] actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Packet19_door_toggle_matches_official_wire_vector()
    {
        var state = new TerrariaDoorToggleState(
            (byte)TerrariaDoorToggleAction.OpenDoor,
            TileX: 123,
            TileY: 456,
            DirectionX: 1);
        byte[] expected = [0x09, 0x00, 0x13, 0x00, 0x7B, 0x00, 0xC8, 0x01, 0x01];

        Assert.Equal(
            TerrariaDoorToggleEncodeResult.Encoded,
            TerrariaDoorToggleCodec.TryEncode(in state, out byte[] actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Packet46_sign_read_decodes_official_wire_vector()
    {
        byte[] payload = [0xD2, 0x04, 0xBF, 0xFE];
        TerrariaFrame frame = Frame(TerrariaMessageId.RequestSign, payload);

        Assert.Equal(
            TerrariaSignDecodeResult.Decoded,
            TerrariaSignCodec.TryDecodeReadRequest(in frame, out TerrariaSignReadRequest request));
        Assert.Equal(new TerrariaSignReadRequest(1234, -321), request);
    }

    [Fact]
    public void Packet47_sign_state_matches_official_wire_vector()
    {
        var state = new TerrariaSignState(2, 10, 20, "x", 3, 1);
        byte[] expected = [0x0D, 0x00, 0x2F, 0x02, 0x00, 0x0A, 0x00, 0x14, 0x00, 0x01, 0x78, 0x03, 0x01];

        byte[] actual = TerrariaSignCodec.EncodeState(in state);

        Assert.Equal(expected, actual);
        Assert.True(state.SuppressOpen);
    }

    [Fact]
    public void Packet79_place_object_matches_official_wire_vector()
    {
        var state = new TerrariaPlaceObjectState(-123, 456, 21, 0, 0, -1, true);
        byte[] expected = [0x0E, 0x00, 0x4F, 0x85, 0xFF, 0xC8, 0x01, 0x15, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x01];

        Assert.Equal(
            TerrariaPlaceObjectEncodeResult.Encoded,
            TerrariaPlaceObjectCodec.TryEncode(in state, out byte[] actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Packet79_nonzero_boolean_matches_MessageBuffer_ReadBoolean_semantics()
    {
        byte[] payload = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2];
        TerrariaFrame frame = Frame(TerrariaMessageId.PlaceObject, payload);

        Assert.Equal(
            TerrariaPlaceObjectDecodeResult.Decoded,
            TerrariaPlaceObjectCodec.TryDecode(in frame, out TerrariaPlaceObjectState state));
        Assert.True(state.Direction);
    }

    private static TerrariaFrame Frame(TerrariaMessageId messageId, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)messageId,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));
}
