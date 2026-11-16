using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    [Fact]
    public void Authenticated_logic_pulse_replicates_tiles_before_the_outer_switch_to_both_clients()
    {
        using var scene = new Scene();
        scene.Put(20, 40, 136);
        for (int x = 20; x <= 26; x++)
            scene.Tiles.Set(x, 40, new WorldTile { Type = (ushort)(x == 20 ? 136 : x == 26 ? 419 : 0),
                Flags = WorldTileFlags.WireBlue | (x is 20 or 26 ? WorldTileFlags.Active : 0) });
        scene.Tiles.Set(26, 41, new WorldTile { Type = 420, FrameY = 18, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        scene.Tiles.Set(27, 41, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(28, 41, new WorldTile { Type = 149, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        scene.DrainFrames(scene.OwnerOutbound); scene.DrainFrames(scene.PeerOutbound);
        scene.Press(20, 40);
        Assert.Equal(18, scene.Tiles.Get(26, 40).FrameX); Assert.Equal(18, scene.Tiles.Get(26, 41).FrameX);
        Assert.Equal(54, scene.Tiles.Get(28, 41).FrameX);
        var owner = scene.DrainFrames(scene.OwnerOutbound); var peer = scene.DrainFrames(scene.PeerOutbound);
        Assert.Equal(new byte[] { 20, 20, 20 }, owner);
        Assert.Equal(new byte[] { 20, 20, 20, 59 }, peer);
    }
    [Fact]
    public void Real_feedback_publishes_source_smoke_and_tiles_before_the_peer_switch()
    {
        using var capture = System.Text.Json.JsonDocument.Parse((string)WiringLogicGate1458Tests.OriginalRows().First(data =>
        {
            if (!(bool)data[1]) return false;
            using var json = System.Text.Json.JsonDocument.Parse((string)data[0]); var row = json.RootElement;
            return row.GetProperty("kind").GetString() == "chain" && row.GetProperty("points").EnumerateArray()
                .Any(point => point.GetProperty("frames").EnumerateArray().Any(frame => Convert.FromHexString(frame.GetString()!)[2] == 106));
        })[0]);
        var row = capture.RootElement;
        using var scene = new Scene();
        foreach (var entry in row.GetProperty("points")[0].GetProperty("before").GetProperty("cells").EnumerateArray())
        {
            var tile = WiringLogicGate1458Tests.Decode(entry.GetProperty("tile"));
            scene.Tiles.Set(entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32(), in tile);
        }
        scene.DrainFrames(scene.OwnerOutbound); scene.DrainFrames(scene.PeerOutbound);
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            scene.Press(row.GetProperty("originX").GetInt16(), row.GetProperty("originY").GetInt16());
            var expected = point.GetProperty("frames").EnumerateArray().Select(frame => Convert.FromHexString(frame.GetString()!)[2]).ToArray();
            Assert.Equal(expected, scene.DrainFrames(scene.OwnerOutbound));
            Assert.Equal(expected.Append((byte)59), scene.DrainFrames(scene.PeerOutbound));
        }
    }
}
