using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Authenticated_pumps_preserve_volume_and_flush_current_liquid_to_both_peers(int kind)
    {
        using var scene = new Scene();
        for (int x = 15; x <= 40; x++) scene.Tiles.Set(x, 30, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(15, 30, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        WiringPump1458Tests.PutPump(scene.Tiles, 24, 30, 142); WiringPump1458Tests.PutPump(scene.Tiles, 40, 30, 143);
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
        {
            var tile = scene.Tiles.Get(24 + dx, 30 + dy); tile.LiquidAmount = 100; tile.LiquidKind = (WorldLiquidKind)kind;
            scene.Tiles.Set(24 + dx, 30 + dy, in tile);
        }
        scene.DrainFrames(scene.OwnerOutbound); scene.DrainFrames(scene.PeerOutbound);
        scene.Press(15, 30);
        Assert.Empty(scene.DrainFrames(scene.OwnerOutbound)); Assert.Equal(new byte[] { 59 }, scene.DrainFrames(scene.PeerOutbound));
        int volume = 0;
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
        {
            Assert.Equal(0, scene.Tiles.Get(24 + dx, 30 + dy).LiquidAmount);
            var tile = scene.Tiles.Get(40 + dx, 30 + dy); volume += tile.LiquidAmount;
            if (tile.LiquidAmount != 0) Assert.Equal((WorldLiquidKind)kind, tile.LiquidKind);
        }
        Assert.Equal(400, volume); Assert.True(scene.Replication.PendingLiquidUpdates > 0);
        while (scene.Replication.PendingLiquidUpdates != 0) scene.Replication.FlushPendingLiquids();
        var owner = GemBunnyWire1458Tests.Drain(scene.OwnerOutbound).Select(Convert.ToHexString).ToArray();
        Assert.NotEmpty(owner); Assert.Equal(owner, GemBunnyWire1458Tests.Drain(scene.PeerOutbound).Select(Convert.ToHexString));
        foreach (var encoded in owner)
        {
            var frame = Decode(Convert.FromHexString(encoded));
            Assert.Equal(TerrariaLiquidDecodeResult.Decoded, TerrariaLiquidCodec.TryDecode(in frame, out var liquid));
            var tile = scene.Tiles.Get(liquid.TileX, liquid.TileY);
            Assert.Equal(tile.LiquidAmount, liquid.Amount); Assert.Equal((byte)tile.LiquidKind, liquid.LiquidKind);
        }
    }
}
