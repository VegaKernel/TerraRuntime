using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    public static IEnumerable<object[]> OriginalCutterRows()
    {
        using var input = typeof(RuntimeWiring1458Tests).Assembly.GetManifestResourceStream("WiringCutterLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalCutterRows))]
    public void Dedicated_packet17_removal_drops_reservations_rng_and_literal_peer_frames_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var random = new VanillaUnifiedRandom1458(1458);
        var registry = new RuntimeWorldItemReplicationRegistry(); var items = new RuntimeWorldItemStore(registry);
        using var scene = new Scene(items: items, itemReplication: registry, wiringRandom: random);
        Assert.True(registry.TryRegister(scene.Owner.Source, scene.OwnerOutbound));
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(8001), new(new PlayerSlotId(1), new(1)));
        Assert.True(registry.TryRegister(peer.Source, scene.PeerOutbound));
        var spawn = new PlayerSpawnCommitRequest(scene.Owner.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(scene.Owner, spawn); registry.PlayerSpawned(peer, spawn with { ClaimedSlot = peer.Player.Slot });
        var before = WiringLogicGate1458Tests.Decode(row.GetProperty("before"));
        int x = row.GetProperty("x").GetInt32(), y = row.GetProperty("y").GetInt32(); scene.Tiles.Set(x, y, before);
        var frame = Decode(Convert.FromHexString(row.GetProperty("packet").GetString()!));
        Assert.Equal(TerraRuntime.Network.TerrariaFrameSinkResult.Continue, scene.Sink.OnFrame(frame)); scene.Drain();
        Assert.Equal(WiringLogicGate1458Tests.Decode(row.GetProperty("after")), scene.Tiles.Get(x, y));
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(v => v.GetString()), GemBunnyWire1458Tests.Drain(scene.PeerOutbound).Select(Convert.ToHexString));
        var expectedRandom = row.GetProperty("random"); const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(expectedRandom.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", hidden)!.GetValue(random)));
        Assert.Equal(expectedRandom.GetProperty("state").EnumerateArray().Select(v => v.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", hidden)!.GetValue(random)!);
        var drops = row.GetProperty("drops").EnumerateArray().ToArray(); Assert.Equal(drops.Length, items.ActiveCount);
        foreach (var drop in drops)
        {
            Assert.True(items.TryGetActive(drop.GetProperty("slot").GetInt16(), out var actual));
            Assert.Equal(drop.GetProperty("type").GetInt16(), actual.ItemNetId); Assert.Equal(drop.GetProperty("stack").GetInt16(), actual.Stack);
            Assert.Equal(drop.GetProperty("x").GetSingle(), actual.PositionX); Assert.Equal(drop.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(drop.GetProperty("vx").GetSingle(), actual.VelocityX); Assert.Equal(drop.GetProperty("vy").GetSingle(), actual.VelocityY);
            Assert.Equal(drop.GetProperty("owner").GetByte(), actual.OwnerPlayerId); Assert.Equal(drop.GetProperty("reservation").GetInt32(), actual.TimeToKeepReservation);
            Assert.Equal(drop.GetProperty("grabPlayer").GetByte(), actual.GrabDelayPlayer); Assert.Equal(drop.GetProperty("grabTime").GetInt32(), actual.GrabDelayTime);
            Assert.Equal(drop.GetProperty("enemy").GetByte(), actual.EnemyGrabDelayTime);
        }
        scene.DrainFrames(scene.PeerOutbound);
        Assert.Equal(TerraRuntime.Network.TerrariaFrameSinkResult.Continue, scene.Sink.OnFrame(frame)); scene.Drain();
        Assert.Equal(drops.Length, items.ActiveCount);
    }
}
