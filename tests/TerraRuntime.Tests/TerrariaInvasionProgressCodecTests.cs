using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaInvasionProgressCodecTests
{
    [Fact]
    public void Packet_78_encodes_the_official_ten_byte_progress_payload()
    {
        var state = new TerrariaInvasionProgressState(24, 25, 1, 7);

        Assert.True(TerrariaInvasionProgressCodec.TryEncode(in state, out byte[] frame));
        Assert.Equal("0D004E18000000190000000107", Convert.ToHexString(frame));
        Assert.Equal((byte)TerrariaMessageId.ReportInvasionProgress, frame[2]);
        Assert.Equal(24, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3)));
        Assert.Equal(25, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(7)));
    }

    [Fact]
    public void Packet_78_rejects_values_the_source_never_reports()
    {
        var state = new TerrariaInvasionProgressState(-1, 25, 1, 1);
        Assert.False(TerrariaInvasionProgressCodec.TryEncode(in state, out byte[] frame));
        Assert.Empty(frame);
    }
}
