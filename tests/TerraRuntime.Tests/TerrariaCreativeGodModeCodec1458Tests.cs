using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaCreativeGodModeCodec1458Tests
{
    [Theory]
    [InlineData((byte)0, false)]
    [InlineData((byte)7, true)]
    [InlineData((byte)254, true)]
    public void Sync_one_player_matches_TerrariaServer_1458_wire_layout(byte slot, bool enabled)
    {
        byte[] encoded = TerrariaCreativeGodModeCodec1458.EncodeSyncOnePlayer(new PlayerSlotId(slot), enabled);

        Assert.Equal(
            new byte[]
            {
                10, 0,
                (byte)TerrariaMessageId.LoadNetModule,
                6, 0,
                5, 0,
                1,
                slot,
                enabled ? (byte)1 : (byte)0
            },
            encoded);
    }

    [Fact]
    public void Sync_everyone_matches_vanilla_255_player_bitset_layout()
    {
        var states = new bool[TerrariaCreativeGodModeCodec1458.PlayerStateCount];
        states[0] = true;
        states[7] = true;
        states[8] = true;
        states[254] = true;

        byte[] encoded = TerrariaCreativeGodModeCodec1458.EncodeSyncEveryone(states);

        Assert.Equal(40, encoded.Length);
        Assert.Equal((byte)40, encoded[0]);
        Assert.Equal((byte)0, encoded[1]);
        Assert.Equal((byte)TerrariaMessageId.LoadNetModule, encoded[2]);
        Assert.Equal((byte)6, encoded[3]);
        Assert.Equal((byte)0, encoded[4]);
        Assert.Equal((byte)5, encoded[5]);
        Assert.Equal((byte)0, encoded[6]);
        Assert.Equal((byte)0, encoded[7]);
        Assert.Equal((byte)0x81, encoded[8]);
        Assert.Equal((byte)0x01, encoded[9]);
        Assert.All(encoded.AsSpan(10, 29).ToArray(), value => Assert.Equal((byte)0, value));
        Assert.Equal((byte)0x40, encoded[39]);
    }

    [Fact]
    public void Sync_one_player_rejects_slot_255_because_vanilla_power_array_has_255_entries()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TerrariaCreativeGodModeCodec1458.EncodeSyncOnePlayer(new PlayerSlotId(byte.MaxValue), enabled: true));
    }
}
