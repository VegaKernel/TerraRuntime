using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{

    public static IEnumerable<object[]> OriginalOrdinaryPlateContacts()
    {
        foreach (var row in PlateRows("contact")) yield return [row.GetRawText()];
    }

    public static IEnumerable<object[]> OriginalServerDisposablePlateContacts()
    {
        foreach (var row in PlateRows("contact"))
            if (row.GetProperty("style").GetInt32() == 7 && row.GetProperty("actor").GetInt32() == 5)
                yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalOrdinaryPlateContacts))]
    public void All_ordinary_plate_styles_and_actor_filters_match_original_contact_detection(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100));
        Put(tiles, 30, 40, 135, frameY: row.GetProperty("style").GetInt32() * 18);
        var actor = (WorldPressurePlateActor1458)row.GetProperty("actor").GetByte();
        Assert.Equal(row.GetProperty("points")[0].GetProperty("hit").GetBoolean(),
            WorldPressurePlateContacts1458.TryFindContact(tiles, 480, 611, 20, 42, 450, 611, actor, out var contact));
        if (row.GetProperty("points")[0].GetProperty("hit").GetBoolean())
        {
            Assert.Equal(30, contact.X); Assert.Equal(40, contact.Y);
            Assert.Equal(row.GetProperty("style").GetInt32() == 7, contact.DestroyAfterPulse);
        }
        tiles.Set(30, 40, WiringLogicGate1458Tests.Decode(row.GetProperty("points")[0].GetProperty("point")));
        Assert.Equal(row.GetProperty("points")[1].GetProperty("hit").GetBoolean(),
            WorldPressurePlateContacts1458.TryFindContact(tiles, 480, 611, 20, 42, 480, 611, actor, out _));
    }

    public static IEnumerable<object[]> OriginalDisposablePlatePackets()
    {
        foreach (var row in PlateRows("packets")) yield return [row.GetRawText()];
    }

    private static IEnumerable<JsonElement> PlateRows(string mode)
    {
        using var input = typeof(RuntimeWiring1458Tests).Assembly.GetManifestResourceStream("WiringContactLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            if (row.GetProperty("mode").GetString() == mode) yield return row;
    }

    [Theory, MemberData(nameof(OriginalServerDisposablePlateContacts))]
    public void Server_owned_player_impulse_then_disposal_matches_original_dedicated_collision(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        var tiles = new WorldTileStore(new(100, 100));
        var slots = new PlayerSlotPool(2);
        var identities = new ServerPlayerSlotRegistry(slots);
        var serverPlayers = new ServerPlayerAuthority(new ServerPlayerStateStore(identities, 2), identities, tiles);
        var random = new VanillaUnifiedRandom1458(1458); var items = new RuntimeWorldItemStore();
        using var scene = CreateDisposableScene(random, items, serverPlayers, slots, tiles);
        PrepareDisposableScene(scene, row, random);
        Assert.True(serverPlayers.Create(new ServerPlayerId("test:disposable-plate"), 480, 609).IsCreated);
        var graph = Graph(scene);
        // The real physics owner crosses the plate's strip under gravity.
        for (int step = 0; step < 4; step++) serverPlayers.TickPhysics(graph.PlayerSnapshots);
        AssertDisposablePlateResult(scene, row, random, items);
        Assert.Equal(row.GetProperty("points")[0].GetProperty("frames").EnumerateArray().Select(f => f.GetString()),
            GemBunnyWire1458Tests.Drain(scene.OwnerOutbound).Select(Convert.ToHexString));
    }

    [Theory, MemberData(nameof(OriginalDisposablePlatePackets))]
    public void Automatic_disposable_plate_reports_need_no_tool_and_match_original_server_frames_and_rng(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var random = new VanillaUnifiedRandom1458(1458); var items = new RuntimeWorldItemStore();
        using var scene = CreateDisposableScene(random, items);
        PrepareDisposableScene(scene, row, random);
        scene.Press(30, 40);
        // Literal original client contact sequence, with empty hands and no ItemCheck.
        scene.State.Apply(new ClientTileManipulationRuntimeCommand(scene.Owner, new(0, 30, 40, 0, 0)));
        AssertDisposablePlateResult(scene, row, random, items);
        Assert.DoesNotContain((byte)17, scene.DrainFrames(scene.OwnerOutbound));
    }

    private static Scene CreateDisposableScene(VanillaUnifiedRandom1458 random, RuntimeWorldItemStore items,
        ServerPlayerAuthority? serverPlayers = null, PlayerSlotPool? slots = null, WorldTileStore? tiles = null,
        RuntimeWorldItemReplicationRegistry? itemReplication = null)
    {
        var world = new RuntimePlayerUpdateWorld1458(100, 100, 15, false, false)
        {
            GetGoodWorld = false, InfectedSeed = false, NotTheBeesWorld = false, NoTrapsWorld = false,
            TenthAnniversaryWorld = false, DontStarveWorld = false, DrunkWorld = false, DualDungeonsSeed = false,
            JourneyMode = false, HallowBackground = 0, OnlyShimmerOceanWorlds = false
        };
        return new Scene(sourceWorld: world, calendar: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 15 },
            items: items, wiringRandom: random, clock: new RuntimeWorldClock(1000, true, default, 0, 0, freezeWind: true, freezeRain: true),
            serverPlayers: serverPlayers, playerSlots: slots, worldTiles: tiles, itemReplication: itemReplication);
    }

    private static void PrepareDisposableScene(Scene scene, JsonElement row, VanillaUnifiedRandom1458 random)
    {
        scene.State.Apply(new PlayerEquipmentRuntimeCommand(scene.Owner, new(scene.Owner.Player.Slot, 0, 0, 0, 0, 0)));
        Put(scene.Tiles, 30, 40, 135, frameY: 126); Put(scene.Tiles, 33, 40, 4);
        int colors = row.GetProperty("colors").GetInt32();
        for (int x = 30; x <= 33; x++)
        {
            var tile = scene.Tiles.Get(x, 40);
            tile.TrySetFlags(WorldTileFlags.WireRed, true);
            if (colors == 15) tile.TrySetFlags(WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow, true);
            scene.Tiles.Set(x, 40, tile);
        }
        int support = row.GetProperty("support").GetInt32();
        if (support != 0) scene.Tiles.Set(30, 41, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        if (support == 2)
        {
            scene.Tiles.Set(29, 41, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            scene.Tiles.Set(31, 41, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        scene.DrainFrames(scene.PeerOutbound); scene.DrainFrames(scene.OwnerOutbound);
        random.CopyStateFrom(new VanillaUnifiedRandom1458(1458));
    }

    private static void AssertDisposablePlateResult(Scene scene, JsonElement row, VanillaUnifiedRandom1458 random,
        RuntimeWorldItemStore items)
    {
        var point = row.GetProperty("points")[0];
        Assert.Equal(WiringLogicGate1458Tests.Decode(point.GetProperty("point")), scene.Tiles.Get(30, 40));
        Assert.Equal(WiringLogicGate1458Tests.Decode(point.GetProperty("torch")), scene.Tiles.Get(33, 40));
        Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(f => f.GetString()),
            GemBunnyWire1458Tests.Drain(scene.PeerOutbound).Select(Convert.ToHexString));
        Assert.Empty(point.GetProperty("items").EnumerateArray()); Assert.Equal(0, items.ActiveCount);
        AssertPlateRandom(point.GetProperty("random"), random);
    }

    private static void AssertPlateRandom(JsonElement rng, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(rng.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", hidden)!.GetValue(random)));
        Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(v => v.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", hidden)!.GetValue(random)!);
    }
}
