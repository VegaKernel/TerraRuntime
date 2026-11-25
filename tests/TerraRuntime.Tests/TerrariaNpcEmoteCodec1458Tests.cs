using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaNpcEmoteCodec1458Tests
{
    [Theory]
    [InlineData(150, "0D005B78563412003400341296")]
    [InlineData(255, "0F005B785634120034003412FF0000")]
    public void EncodesProtocol326NpcEmotes(byte emote, string expected)
    {
        Assert.True(TerrariaNpcEmoteCodec1458.TryEncode(0x12345678, 0x34, 0x1234, emote, out byte[] frame));
        Assert.Equal(Convert.FromHexString(expected), frame);
    }
}
