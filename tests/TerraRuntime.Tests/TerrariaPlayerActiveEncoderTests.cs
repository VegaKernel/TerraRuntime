using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaPlayerActiveEncoderTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Encodes_official_packet14_layout(bool active, byte activeByte)
    {
        byte[] encoded = TerrariaPlayerActiveEncoder.Encode(playerId: 7, active);

        Assert.Equal(
            new byte[] { 5, 0, (byte)TerrariaMessageId.PlayerActive, 7, activeByte },
            encoded);
    }
}
