using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    public static IEnumerable<object[]> OriginalLandMineRows()
    {
        using var input = typeof(RuntimeWiring1458Tests).Assembly.GetManifestResourceStream("WiringLandMineLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var data = JsonDocument.Parse(gzip);
        foreach (var row in data.RootElement.GetProperty("rows").EnumerateArray())
            if (row.GetProperty("mode").GetString() != "client-contact") yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalLandMineRows))]
    public void Land_mine_world_contact_and_first_projectile_update_match_original_dedicated_source(string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var random = new VanillaUnifiedRandom1458(1458);
        var projectiles = new RuntimeProjectileReplicationRegistry();
        var shots = new RuntimeProjectileStore(1001, projectiles);
        var npcReplication = new RuntimeNpcReplicationRegistry();
        var npcs = new RuntimeNpcStore(commitSink: npcReplication);
        var itemReplication = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(itemReplication);
        using var scene = CreateLandMineScene(random, shots, projectiles, npcs, npcReplication, items, itemReplication);
        var graph = Graph(scene);
        var owner = graph.WorldTileAuthority;
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", hidden)!.GetValue(owner)!;
        string mode = row.GetProperty("mode").GetString()!;
        string target = row.GetProperty("target").GetString()!;
        LoadLandMineBoard(scene, row.GetProperty("before").GetProperty("cells"));
        if (mode == "ordinary-kill") scene.State.Apply(new PlayerDisconnectRuntimeCommand(scene.Owner));
        if (target is "npc" or "npc-out")
        {
            var update = new NpcStateUpdate(1, 1, target == "npc" ? 484 : 584, 640, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = 1000, LifeMax = 1000, Immortal = false, DefenseOverride = 2 });
            Assert.True(npcs.TrySpawnVanilla(in update, out _));
        }
        if (target is "player" or "player-out")
        {
            Assert.True(graph.Players.TryGet(scene.Owner.Player, out var member));
            Assert.True(member.TryAdvanceRevision());
            member.PositionX = target == "player" ? 480 : 580;
            member.PositionY = 611;
            scene.State.Apply(new PlayerHealthRuntimeCommand(scene.Owner, new(scene.Owner.Player.Slot, 400, 400)));
        }
        _ = GemBunnyWire1458Tests.Drain(scene.PeerOutbound);
        random.CopyStateFrom(new VanillaUnifiedRandom1458(1458));

        if (mode == "direct") Assert.True(circuit.HitSwitch(30, 40));
        else if (mode == "packet59") scene.Press(30, 40);
        else if (mode == "wire") scene.Press(27, 40);
        else if (mode == "ordinary-kill")
        {
            var (plan, source) = PrepareTriggerDestruction(scene);
            using (plan)
            {
                Assert.True(source.TryKillTile(30, 40, false, false, false, out var kill) && kill.Changed);
                Assert.True(plan.TrySeal());
                Assert.True(plan.TryAdoptWholeUnpublished(out var publication));
                Assert.NotNull(publication);
                var end = plan.CapturePublicationOffset();
                Assert.True(publication.TryPublishThrough(in end));
            }
        }
        else
        {
            int width = row.GetProperty("layout").GetString() == "plate" ? 52 : 20;
            float oldX = mode == "contact-old-overlap" ? 480 : 410;
            var actor = (WorldPressurePlateActor1458)row.GetProperty("actor").GetByte();
            if (actor == WorldPressurePlateActor1458.ProjectileOrBoulder)
                owner.ApplyProjectilePressurePlate(480, 611, width, 42, oldX, 611);
            else
            {
                var mine = typeof(WorldTileAuthority).GetMethod("ApplyNonTerminatingWiringContact", hidden)!
                    .CreateDelegate<Action<ushort, int, int>>(owner);
                bool hit = WorldPressurePlateContacts1458.TryFindContact(scene.Tiles, 480, 611, width, 42, oldX, 611,
                    actor, out var contact, mine);
                Assert.Equal(row.GetProperty("trigger").GetProperty("contact").GetBoolean(), hit);
                if (hit) owner.PulseProjectilePressurePlate(in contact);
            }
        }

        AssertLandMineImage(scene, shots, npcs, items, random, row.GetProperty("trigger"), target);
        foreach (var step in row.GetProperty("ticks").EnumerateArray())
        {
            owner.TickMechanisms();
            Assert.True(graph.Projectiles.TryTickState(graph.Npcs.ProjectileNpcCombat));
            AssertLandMineImage(scene, shots, npcs, items, random, step, target);
        }
    }

    [Fact]
    public void Mounted_player_crossing_a_mine_and_geyser_offers_both_in_source_scan_order()
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var replication = new RuntimeProjectileReplicationRegistry();
        var shots = new RuntimeProjectileStore(1001, replication);
        using var scene = CreateLandMineScene(random, shots, replication);
        var solid = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        scene.Tiles.Set(30, 40, solid with { Type = 210 });
        scene.Tiles.Set(30, 41, solid);
        scene.Tiles.Set(31, 41, solid);
        scene.Tiles.Set(30, 42, solid with { Type = 443, FrameX = 72 });
        scene.Tiles.Set(31, 42, solid with { Type = 443, FrameX = 90 });
        _ = GemBunnyWire1458Tests.Drain(scene.PeerOutbound);
        random.CopyStateFrom(new VanillaUnifiedRandom1458(1458));

        var before = default(PlayerStateSnapshot) with
        {
            Player = scene.Owner.Player, PositionX = 450, PositionY = 611,
            HasMount = true, MountType = 10
        };
        Graph(scene).WorldTileAuthority.ApplyPlayerPressurePlate(before, before with { PositionX = 487 });

        // Two exact Collision.SwitchTiles captures with the pinned original Linux server.
        // Mine clears and births first. Both geyser contacts send 59 with one shared birth.
        string[] original =
        [
            "0D00141E002800010100000000",
            "20001BFF0004000000D443000012440000000000000000A40030FA0000002041",
            "07003B1E002800",
            "20001BFF0104000080F0430040284400000000000000418E0230140000000040",
            "07003B1E002A00",
            "07003B1F002A00"
        ];
        Assert.Equal(original, GemBunnyWire1458Tests.Drain(scene.PeerOutbound).Select(Convert.ToHexString));
        Assert.False(scene.Tiles.Get(30, 40).IsActive);
        Assert.Equal(2, shots.ActiveCount);
        Assert.True(random.HasSameState(new VanillaUnifiedRandom1458(1458)));
    }

    [Fact]
    public void Land_mine_rejects_an_unqualified_detonation_without_clearing_or_spawning()
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var shots = new RuntimeProjectileStore(1001);
        using var scene = CreateLandMineScene(random, shots);
        scene.Put(30, 40, 210);
        scene.Put(30, 41, 1);
        var liquid = new WorldTile { LiquidAmount = 200, LiquidKind = WorldLiquidKind.Water };
        scene.Tiles.Set(31, 40, in liquid);
        var mine = scene.Tiles.Get(30, 40);
        var before = random.Clone();
        scene.Press(30, 40);
        Assert.Equal(mine, scene.Tiles.Get(30, 40));
        Assert.Equal(0, shots.ActiveCount);
        Assert.Empty(scene.DrainFrames(scene.PeerOutbound));
        Assert.True(random.HasSameState(before));
        Assert.Equal(1, scene.State.RejectedClientTileManipulations);
    }

    private static Scene CreateLandMineScene(VanillaUnifiedRandom1458 random, RuntimeProjectileStore shots,
        RuntimeProjectileReplicationRegistry? shotReplication = null, RuntimeNpcStore? npcs = null,
        RuntimeNpcReplicationRegistry? npcReplication = null, RuntimeWorldItemStore? items = null,
        RuntimeWorldItemReplicationRegistry? itemReplication = null)
    {
        var tiles = new WorldTileStore(new(80, 100));
        Assert.True(tiles.TryAttachWorldSurface(15));
        var facts = new RuntimePlayerUpdateWorld1458(80, 100, 15, false, false)
        {
            GetGoodWorld = false, InfectedSeed = false, NotTheBeesWorld = false, NoTrapsWorld = false,
            TenthAnniversaryWorld = false, DontStarveWorld = false, DrunkWorld = false, DualDungeonsSeed = false,
            JourneyMode = false, HallowBackground = 0, OnlyShimmerOceanWorlds = false
        };
        var scene = new Scene(sourceWorld: facts, calendar: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 15 },
            wiringRandom: random, clock: new RuntimeWorldClock(1000, true, default, 0, 0, freezeWind: true, freezeRain: true),
            worldTiles: tiles, projectiles: shots, projectileReplication: shotReplication,
            npcs: npcs, npcReplication: npcReplication, items: items, itemReplication: itemReplication);
        var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(8001), new(new PlayerSlotId(1), new(1)));
        var request = new PlayerSpawnCommitRequest(observer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        if (shotReplication is not null)
        {
            Assert.True(shotReplication.TryRegister(observer.Source, scene.PeerOutbound));
            shotReplication.PlayerSpawned(observer, request);
        }
        if (npcReplication is not null)
        {
            Assert.True(npcReplication.TryRegister(observer.Source, scene.PeerOutbound));
            npcReplication.PlayerSpawned(observer, request);
        }
        if (itemReplication is not null)
        {
            Assert.True(itemReplication.TryRegister(observer.Source, scene.PeerOutbound));
            itemReplication.PlayerSpawned(observer, request);
        }
        scene.State.Apply(new PlayerEquipmentRuntimeCommand(scene.Owner, new(scene.Owner.Player.Slot, 0, 0, 0, 0, 0)));
        return scene;
    }

    private static void LoadLandMineBoard(Scene scene, JsonElement cells)
    {
        foreach (var cell in cells.EnumerateArray())
            scene.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(),
                WiringLogicGate1458Tests.Decode(cell.GetProperty("tile")));
    }

    private static void AssertLandMineImage(Scene scene, RuntimeProjectileStore shots, RuntimeNpcStore npcs,
        RuntimeWorldItemStore items, VanillaUnifiedRandom1458 random, JsonElement image, string target)
    {
        foreach (var cell in image.GetProperty("cells").EnumerateArray())
        {
            var expected = WiringLogicGate1458Tests.Decode(cell.GetProperty("tile"));
            var actual = scene.Tiles.Get(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32());
            if (expected.IsActive && !VanillaWorldFrameImportance326.IsFrameImportant(expected.Type))
                (expected.FrameX, expected.FrameY) = (actual.FrameX, actual.FrameY);
            Assert.Equal(expected, actual);
        }
        Assert.Equal(image.GetProperty("frames").EnumerateArray().Select(packet => packet.GetString()),
            GemBunnyWire1458Tests.Drain(scene.PeerOutbound).Select(Convert.ToHexString));
        AssertPlateRandom(image.GetProperty("random"), random);
        var expectedShots = image.GetProperty("shots");
        Assert.Equal(expectedShots.GetArrayLength(), shots.ActiveCount);
        foreach (var expected in expectedShots.EnumerateArray())
        {
            Assert.True(shots.TryGetActive(expected.GetProperty("slot").GetUInt16(), out var shot));
            Assert.True(shots.TryGetLifecycle(shot.Handle, out var life));
            Assert.Equal(expected.GetProperty("type").GetUInt16(), shot.Type.Value);
            Assert.Equal(expected.GetProperty("owner").GetByte(), shot.Spawner);
            Assert.Equal(expected.GetProperty("x").GetSingle(), shot.PositionX);
            Assert.Equal(expected.GetProperty("y").GetSingle(), shot.PositionY);
            Assert.Equal(expected.GetProperty("damage").GetInt16(), shot.Damage);
            Assert.Equal(expected.GetProperty("knockBack").GetSingle(), shot.KnockBack);
            Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
        }
        var expectedItems = image.GetProperty("items");
        Assert.Equal(expectedItems.GetArrayLength(), items.ActiveCount);
        foreach (var expected in expectedItems.EnumerateArray())
        {
            Assert.True(items.TryGetActive(expected.GetProperty("slot").GetInt16(), out var item));
            Assert.Equal(expected.GetProperty("type").GetInt16(), item.ItemNetId);
            Assert.Equal(expected.GetProperty("stack").GetInt16(), item.Stack);
        }
        var health = image.GetProperty("health");
        if (target is "npc" or "npc-out")
        {
            Assert.True(npcs.TryGetActive(0, out var npc));
            Assert.Equal(health.GetProperty("npc").GetProperty("life").GetInt32(), npc.Simulation.Life);
            Assert.Equal(health.GetProperty("npc").GetProperty("velocity").GetSingle(), npc.VelocityX);
        }
        if (target is "player" or "player-out")
        {
            Assert.True(Graph(scene).Players.TryGet(scene.Owner.Player, out var member));
            Assert.Equal(health.GetProperty("player").GetProperty("life").GetInt32(), member.CaptureSnapshot().Life);
        }
    }
}
