using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    public static IEnumerable<object[]> OriginalActuationRodRows()
    {
        using var input = typeof(RuntimeWiring1458Tests).Assembly.GetManifestResourceStream("WiringActuationRodLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalActuationRodRows))]
    public void Dedicated_actuation_rod_replays_literal_packets_framing_drops_and_rng(string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        int layout = row.GetProperty("scene").GetInt32();
        var random = new VanillaUnifiedRandom1458(1458);
        var registry = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(registry);
        var tiles = new WorldTileStore(new(80, 100));
        Assert.True(tiles.TryAttachWorldSurface(15));
        var world = new RuntimePlayerUpdateWorld1458(80, 100, 15, false, false)
        {
            GetGoodWorld = false, InfectedSeed = false, NotTheBeesWorld = false, NoTrapsWorld = false,
            TenthAnniversaryWorld = false, DontStarveWorld = false, DrunkWorld = false, DualDungeonsSeed = false,
            JourneyMode = false, HallowBackground = 0, OnlyShimmerOceanWorlds = false
        };
        var calendar = default(RuntimeTownCommerceWorldFacts1458) with { DownedPlantera = layout == 8, WorldSurface = 15 };
        var clock = new RuntimeWorldClock(1000, true, default, 0, 0, freezeWind: true, freezeRain: true);
        using var scene = new Scene(sourceWorld: world, items: items, itemReplication: registry, wiringRandom: random,
            worldTiles: tiles, calendar: calendar, clock: clock);
        scene.State.Apply(new PlayerEquipmentRuntimeCommand(scene.Owner, new(scene.Owner.Player.Slot, 0, 0, 0, 0, 0)));
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(8001), new(new PlayerSlotId(1), new(1)));
        Assert.True(registry.TryRegister(peer.Source, scene.PeerOutbound));
        var joined = new PlayerSpawnCommitRequest(peer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(peer, in joined);
        foreach (var cell in row.GetProperty("first").EnumerateArray())
            tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(),
                WiringLogicGate1458Tests.Decode(cell.GetProperty("tile")));

        scene.DrainFrames(scene.PeerOutbound);
        scene.DrainFrames(scene.OwnerOutbound);
        random.CopyStateFrom(new VanillaUnifiedRandom1458(1458));
        int x = row.GetProperty("x").GetInt32(), y = row.GetProperty("y").GetInt32();
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            Assert.Empty(step.GetProperty("exceptions").EnumerateArray());
            var before = tiles.Get(x, y);
            var frame = Decode(Convert.FromHexString(step.GetProperty("packet").GetString()!));
            Assert.Equal(TerrariaFrameSinkResult.Continue, scene.Sink.OnFrame(in frame));
            Assert.Equal(before, tiles.Get(x, y));
            scene.Drain();
            foreach (var cell in step.GetProperty("cells").EnumerateArray())
            {
                int cellX = cell.GetProperty("x").GetInt32(), cellY = cell.GetProperty("y").GetInt32();
                var expected = WiringLogicGate1458Tests.Decode(cell.GetProperty("tile"));
                var actual = tiles.Get(cellX, cellY);
                if (VanillaTileDefinitionCatalog.TryGet(expected.TileType, out var definition) && !definition.IsFrameImportant)
                    (expected.FrameX, expected.FrameY) = (actual.FrameX, actual.FrameY);
                Assert.True(expected.Equals(actual), $"Layout {layout}, tile at ({cellX},{cellY}): expected {expected}, actual {actual}");
            }

            var sent = step.GetProperty("frames").EnumerateArray().Select(v => v.GetString()).ToArray();
            Assert.Equal(sent, GemBunnyWire1458Tests.Drain(scene.PeerOutbound).Select(Convert.ToHexString));
            Assert.Equal(sent.Where(v => Convert.FromHexString(v!)[2] == 20),
                GemBunnyWire1458Tests.Drain(scene.OwnerOutbound).Select(Convert.ToHexString));
            var entropy = step.GetProperty("random");
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.Equal(entropy.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", hidden)!.GetValue(random)));
            Assert.Equal(entropy.GetProperty("state").EnumerateArray().Select(v => v.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", hidden)!.GetValue(random)!);

            var drops = step.GetProperty("drops").EnumerateArray().ToArray();
            Assert.Equal(drops.Length, items.ActiveCount);
            foreach (var drop in drops)
            {
                Assert.True(items.TryGetActive(drop.GetProperty("slot").GetInt16(), out var actual));
                Assert.Equal(drop.GetProperty("type").GetInt16(), actual.ItemNetId);
                Assert.Equal(drop.GetProperty("stack").GetInt16(), actual.Stack);
                Assert.Equal(drop.GetProperty("owner").GetByte(), actual.OwnerPlayerId);
                Assert.Equal(drop.GetProperty("reservation").GetInt32(), actual.TimeToKeepReservation);
                Assert.Equal(drop.GetProperty("grabPlayer").GetByte(), actual.GrabDelayPlayer);
                Assert.Equal(drop.GetProperty("grabTime").GetInt32(), actual.GrabDelayTime);
                Assert.Equal(drop.GetProperty("enemy").GetInt32(), actual.EnemyGrabDelayTime);
            }
        }
    }
}
