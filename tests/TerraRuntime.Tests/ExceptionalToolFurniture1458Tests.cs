using System.IO.Compression;
using System.Buffers;
using TerraRuntime.Network;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Tests.Larva1458;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ExceptionalFurniture1458;

public sealed class ExceptionalToolFurniture1458Tests
{
    [Fact]
    public void Source_furniture_track_pose_and_canonical_metadata_use_one_owned_action()
    {
        ModernAnchors(); Tracks(); MetadataTables(); SourceWorld(); SourcePoses(); PoundCallers(); StatePose(); StateAllFamilies(); MetadataCurrentness(); CrossObjectGuard(); CheckpointAndSection();
    }

    private static JsonDocument Source(string kind, string platform, string run = "First")
    {
        using var input = typeof(ExceptionalToolFurniture1458Tests).Assembly.GetManifestResourceStream("Furniture" + kind + platform + run + "1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }
    private static void Tracks()
    {
        foreach (var platform in new[] { "Linux", "Windows" }) foreach (var run in new[] { "First", "Repeat" })
        {
            using var source = Source("Tracks", platform, run); Assert.Equal(4608, source.RootElement.GetArrayLength());
            foreach (var row in source.RootElement.EnumerateArray())
            {
                var before = row.GetProperty("before");
                var input = new MinecartTrackFrameInput1458(before.GetProperty("frameX").GetInt16(), before.GetProperty("frameY").GetInt16(),
                    (MinecartTrackNeighbors1458)row.GetProperty("mask").GetByte());
                Assert.True(VanillaMinecartTrackFrame1458.TryPlan(in input, row.GetProperty("pound").GetBoolean(), true, out var result));
                Assert.Equal(row.GetProperty("result").GetBoolean(), result.SourceResult);
                Assert.Equal(row.GetProperty("after").GetProperty("frameX").GetInt16(), result.Front);
                Assert.Equal(row.GetProperty("after").GetProperty("frameY").GetInt16(), result.Back);
            }
        }
    }
    private static void ModernAnchors()
    {
        foreach (var platform in new[] { "Linux", "Windows" })
        {
            using var source = Source("Anchors", platform);
            foreach (var row in source.RootElement.EnumerateArray())
            {
                using var f = new LarvaRuntimeFixture1458(platform == "Windows");
                foreach (var cell in row.GetProperty("before").EnumerateArray()) f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), Cell(cell));
                Assert.True(f.Tiles.TryAttachTileEntities(Entities(row.GetProperty("beforeEntities")), 100000));
                var world = f.Composition.WorldTileAuthority; Assert.True(world.TryCaptureToolTreeWorld(out var context));
                Assert.True(f.Players.TryCaptureToolTreeActors(out var actors)); var overlay = new ToolWorldTileOverlay1458(f.Tiles);
                Assert.True(world.TryCreateToolMutationPlan(overlay, out var plan));
                using (plan!)
                {
                    bool doll = row.GetProperty("family").GetString() == "doll";
                    var random = doll ? ReadRandom(row.GetProperty("beforeRandom")) : plan!.SourceRandom;
                    if (!doll) random.CopyStateFrom(ReadRandom(row.GetProperty("beforeRandom")));
                    Assert.True(world.TryCreateToolSourcePass(overlay, random, doll, false, context!.Facts, actors!.Actors, doll ? null : plan, out var pass));
                    if (doll)
                    {
                        Assert.True(pass!.TryChangeDisplayDollPose(row.GetProperty("clickX").GetInt32(), row.GetProperty("clickY").GetInt32(), out int id, out byte pose));
                        Assert.True(row.GetProperty("result").GetBoolean() == (id >= 0), platform + "/" + row.GetProperty("scenario") + " source pose eligibility");
                        if (id >= 0) Assert.Equal(Convert.FromHexString(row.GetProperty("frames")[0].GetString()!), TerrariaDollPoseCodec1458.Encode(new(0, id, pose, pose)));
                    }
                    else
                    {
                        Assert.True(pass!.TryKillTile(50, 50, false, false, false, out _));
                        foreach (var cell in row.GetProperty("after").EnumerateArray())
                        {
                            Assert.True(overlay.TryRead(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), out var actual));
                            Assert.Equal(cell.GetProperty("active").GetBoolean(), actual.IsActive); Assert.Equal(cell.GetProperty("type").GetUInt16(), actual.Type);
                        }
                        Assert.True(plan!.TrySeal()); Assert.True(plan.TryAdoptWholeUnpublished(out _));
                        SameEntities(Entities(row.GetProperty("entities")), f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
                        Assert.Equal(row.GetProperty("items").GetArrayLength(), f.Items.ActiveCount);
                    }
                    Assert.True(random.HasSameState(ReadRandom(row.GetProperty("afterRandom"))), "modern anchor exact source RNG");
                }
            }
        }
    }
    private static void MetadataTables()
    {
        Assert.False(VanillaFurnitureItemFacts1458.IsCanonicalItemOrAir(new(6171)));
        Assert.False(VanillaFurnitureItemFacts1458.IsCanonicalItemOrAir(new(226)));
        Assert.True(VanillaFurnitureItemFacts1458.IsCanonicalItemOrAir(new(8)));
        foreach (var platform in new[] { "Linux", "Windows" }) foreach (var run in new[] { "First", "Repeat" })
        {
            using var source = Source("Metadata", platform, run); var data = source.RootElement;
            string[] names = ["headTypes", "bodyTypes", "legTypes"];
            for (int row = 0; row < names.Length; row++)
                for (int index = 0; index < data.GetProperty(names[row]).GetArrayLength(); index++)
                { Assert.True(VanillaFurnitureItemFacts1458.TryGetLegacyArmor(row, index, out var item)); Assert.Equal(data.GetProperty(names[row])[index].GetInt32(), item.Value); }
            for (int item = 0; item < 6196; item++) Assert.Equal(data.GetProperty("noGravity")[item].GetBoolean(), VanillaFurnitureItemFacts1458.IsNoGravity(new(item)));
        }
    }
    private static ToolHitState1458 HitImage(JsonElement row)
    {
        var data = row.GetProperty("data").EnumerateArray().Select(e => new ToolHitEntry1458(e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32(),
            e.GetProperty("damage").GetInt32(), e.GetProperty("type").GetInt32(), e.GetProperty("ttl").GetInt32())).ToArray();
        Assert.True(ToolHitState1458.TryCreateSnapshot(data, row.GetProperty("order").EnumerateArray().Select(e => e.GetInt32()).ToArray(), row.GetProperty("buffer").GetInt32(), out var result)); return result!;
    }
    private static void PoundCallers()
    {
        foreach (var platform in new[] { "Linux", "Windows" })
        {
            using var source = Source("Callers", platform);
            foreach (var row in source.RootElement.EnumerateArray())
            {
                using var f = new LarvaRuntimeFixture1458(platform == "Windows");
                bool doll = row.GetProperty("family").GetString() == "doll";
                int x = 50, y = 50;
                if (doll)
                {
                    for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++) f.Tiles.Set(50 + dx, 50 + dy,
                        new WorldTile { Type = 470, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) });
                    for (int dx = 0; dx < 2; dx++) f.Tiles.Set(50 + dx, 53, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                    Assert.True(f.Tiles.TryAttachTileEntities(Entities(row.GetProperty("beforeEntities")), 100000));
                    x += row.GetProperty("dx").GetInt32(); y += row.GetProperty("dy").GetInt32();
                }
                else
                {
                    foreach (var cell in row.GetProperty("before").EnumerateArray()) f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), Cell(cell));
                    Assert.True(f.Tiles.TryAttachTileEntities([], 100000));
                }
                var world = f.Composition.WorldTileAuthority; Assert.True(world.TryCaptureToolTreeWorld(out var context));
                Assert.True(f.Players.TryCaptureToolTreeActors(out var actors));
                var localTiles = new ToolWorldTileOverlay1458(f.Tiles); var serverTiles = new ToolWorldTileOverlay1458(f.Tiles);
                Assert.True(world.TryCreateToolMutationPlan(serverTiles, out var plan));
                using (plan!)
                {
                    var random = ReadRandom(row.GetProperty("beforeRandom"));
                    Assert.True(world.TryCreateToolSourcePass(localTiles, random, true, false, context!.Facts, actors!.Actors, null, out var client));
                    Assert.True(world.TryCreateToolSourcePass(serverTiles, plan!.SourceRandom, false, false, context.Facts, actors.Actors, plan, out var server));
                    var hits = HitImage(row.GetProperty("beforeHits")); int slot = hits.Find(x, y, 1); Assert.True(slot >= 0);
                    var steps = new WorldTileAuthority.ToolActionSteps1458(world, client!, server!, plan, world.CreateToolReadPreparation()!, random,
                        new(600, 300, 80, 100, false, false, false, false, false), hits, 0, false);
                    Assert.True(steps.TryPoundAt(x, y, slot, out bool performed, out bool consumed)); Assert.True(performed); Assert.True(consumed);
                    Assert.True(random.HasSameState(ReadRandom(row.GetProperty("afterRandom"))), platform + "/" + row.GetProperty("family") + " RNG");
                    var expected = HitImage(row.GetProperty("afterHits")); Assert.Equal(expected.BufferLocation, steps.Hits.BufferLocation);
                    for (int index = 0; index < ToolHitState1458.SlotCount; index++) { Assert.Equal(expected.GetEntry(index), steps.Hits.GetEntry(index)); Assert.Equal(expected.GetOrder(index), steps.Hits.GetOrder(index)); }
                    var frames = row.GetProperty("frames").EnumerateArray().Select(e => Convert.FromHexString(e.GetString()!)).ToArray();
                    if (doll) Assert.Equal(Assert.Single(frames), TerrariaDollPoseCodec1458.Encode(steps.ExpectedDollPose!.Value));
                    else
                    {
                        Assert.Equal(frames.Length, steps.ExpectedCount);
                        for (int index = 0; index < frames.Length; index++)
                        { Assert.Equal(TerrariaTileManipulationEncodeResult.Encoded, TerrariaTileManipulationCodec.TryEncode(steps.ExpectedObservations[index].Tile!.Value, out var encoded)); Assert.Equal(frames[index], encoded); }
                    }
                    var before = row.GetProperty("beforeClock");
                    var clock = new ToolClockState1458(new(before.GetProperty("itemTime").GetInt32(), before.GetProperty("itemTimeMax").GetInt32(),
                        before.GetProperty("itemAnimation").GetInt32(), before.GetProperty("itemAnimationMax").GetInt32(), false, 0, 0), before.GetProperty("toolTime").GetInt32(), true, false);
                    Assert.True(VanillaToolCatalog1458.TryGet(new(7), out var hammer));
                    Assert.True(VanillaToolUse1458.TryApplyActionTime(in hammer, in clock, ToolActionKind1458.Pound, 1f, platform == "Windows", out clock));
                    Assert.Equal(row.GetProperty("afterClock").GetProperty("itemTime").GetInt32(), clock.Selected.ItemTime);
                }
            }
        }
    }
    private static WorldTile Cell(JsonElement row) => new()
    {
        Type = row.GetProperty("type").GetUInt16(), Wall = row.GetProperty("wall").GetUInt16(),
        FrameX = row.GetProperty("frameX").GetInt16(), FrameY = row.GetProperty("frameY").GetInt16(),
        Flags = row.GetProperty("active").GetBoolean() ? WorldTileFlags.Active : 0
    };
    private static WorldTileEntityItem? Item(JsonElement row) => row.GetProperty("type").GetInt32() == 0 ? null :
        new(row.GetProperty("type").GetInt16(), row.GetProperty("prefix").GetByte(), row.GetProperty("stack").GetInt16());
    private static WorldTileEntity[] Entities(JsonElement rows) => rows.EnumerateArray().Select(row =>
    {
        var kind = (WorldTileEntityKind)row.GetProperty("kind").GetByte();
        WorldTileEntityPayload payload = kind switch
        {
            WorldTileEntityKind.DisplayDoll => new WorldDisplayDollPayload(row.GetProperty("pose").GetByte(),
                row.GetProperty("equipment").EnumerateArray().Select(Item).ToArray(), row.GetProperty("dyes").EnumerateArray().Select(Item).ToArray(), Item(row.GetProperty("misc")[0])),
            WorldTileEntityKind.HatRack => new WorldHatRackPayload(row.GetProperty("hats").EnumerateArray().Select(Item).ToArray(),
                row.GetProperty("dyes").EnumerateArray().Select(Item).ToArray()),
            WorldTileEntityKind.WeaponsRack => new WorldItemTileEntityPayload(Item(row.GetProperty("item")).GetValueOrDefault()),
            _ => throw new InvalidOperationException()
        };
        return new WorldTileEntity(row.GetProperty("id").GetInt32(), row.GetProperty("position").GetProperty("X").GetInt16(),
            row.GetProperty("position").GetProperty("Y").GetInt16(), kind, payload);
    }).ToArray();
    private static void SourceWorld()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = Source("World", platform);
            int count = 0;
            foreach (var row in source.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("type", out _)) continue;
                using var f = new LarvaRuntimeFixture1458(platform == "Windows");
                foreach (var tile in row.GetProperty("before").EnumerateArray()) f.Tiles.Set(tile.GetProperty("x").GetInt32(), tile.GetProperty("y").GetInt32(), Cell(tile));
                Assert.True(f.Tiles.TryAttachTileEntities(Entities(row.GetProperty("beforeEntities")), 100000));
                var world = f.Composition.WorldTileAuthority;
                Assert.True(world.TryCaptureToolTreeWorld(out var context));
                Assert.True(f.Players.TryCaptureToolTreeActors(out var actors));
                var overlay = new ToolWorldTileOverlay1458(f.Tiles);
                Assert.True(world.TryCreateToolMutationPlan(overlay, out var plan));
                using (plan!)
                {
                    bool client = row.GetProperty("role").GetInt32() == 1;
                    var random = client ? ReadRandom(row.GetProperty("beforeRandom")) : plan!.SourceRandom;
                    if (!client) random.CopyStateFrom(ReadRandom(row.GetProperty("beforeRandom")));
                    Assert.True(world.TryCreateToolSourcePass(overlay, random, client, false, context!.Facts, actors!.Actors, client ? null : plan, out var pass));
                    string key = platform + "/" + count + "/" + row.GetProperty("type") + "/role" + row.GetProperty("role");
                    Assert.True(pass!.TryKillTile(50 + row.GetProperty("dx").GetInt32(), 50 + row.GetProperty("dy").GetInt32(),
                        row.GetProperty("fail").GetBoolean(), false, false, out _), key);
                    Assert.True(random.HasSameState(ReadRandom(row.GetProperty("afterRandom"))), key + " RNG");
                    foreach (var expected in row.GetProperty("after").EnumerateArray())
                    {
                        Assert.True(overlay.TryRead(expected.GetProperty("x").GetInt32(), expected.GetProperty("y").GetInt32(), out var actual));
                        Assert.Equal(expected.GetProperty("active").GetBoolean(), actual.IsActive);
                        Assert.Equal(expected.GetProperty("type").GetUInt16(), actual.Type);
                        // Non-important Stone frames are renderer sidecars, not WorldTile save/wire state.
                        if (!actual.IsActive || VanillaWorldFrameImportance326.IsFrameImportant(actual.Type))
                        {
                            Assert.True(expected.GetProperty("frameX").GetInt16() == actual.FrameX, key + " frameX at " + expected.GetProperty("x") + "," + expected.GetProperty("y"));
                            Assert.Equal(expected.GetProperty("frameY").GetInt16(), actual.FrameY);
                        }
                        Assert.Equal(expected.GetProperty("wall").GetUInt16(), actual.Wall);
                    }
                    Assert.True(pass.IsCurrentOwned); Assert.Equal(0, f.Items.ActiveCount);
                    if (!client)
                    {
                        Assert.True(plan!.TrySeal(), key + " seal"); Assert.True(plan.TryAdoptWholeUnpublished(out var publication), key + " adoption");
                        Assert.NotNull(publication);
                        var born = new WorldItemSnapshot[f.Items.ActiveCount]; Assert.Equal(born.Length, f.Items.CopyActive(born));
                        var expectedItems = row.GetProperty("items"); Assert.Equal(expectedItems.GetArrayLength(), born.Length);
                        for (int n = 0; n < born.Length; n++)
                        {
                            var expected = expectedItems[n]; var item = expected.GetProperty("item");
                            Assert.Equal(item.GetProperty("type").GetInt16(), born[n].ItemNetId);
                            Assert.Equal(item.GetProperty("stack").GetInt16(), born[n].Stack);
                            Assert.Equal(item.GetProperty("prefix").GetByte(), born[n].Prefix);
                            var position = Pair(expected.GetProperty("position").GetString()!);
                            var velocity = Pair(expected.GetProperty("velocity").GetString()!);
                            Assert.Equal(position.X, born[n].PositionX); Assert.Equal(position.Y, born[n].PositionY);
                            Assert.Equal(velocity.X, born[n].VelocityX); Assert.Equal(velocity.Y, born[n].VelocityY);
                        }
                        SameEntities(Entities(row.GetProperty("entities")), f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
                    }
                }
                count++;
            }
            Assert.Equal(384, count);
        }
    }
    private static (float X, float Y) Pair(string value)
    {
        var split = value.Split(','); return (float.Parse(split[0], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(split[1], System.Globalization.CultureInfo.InvariantCulture));
    }
    private static void SameEntities(WorldTileEntity[] expected, WorldTileEntity[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int n = 0; n < actual.Length; n++)
        {
            Assert.Equal(expected[n].PersistedId, actual[n].PersistedId); Assert.Equal(expected[n].Kind, actual[n].Kind);
            switch (expected[n].Payload)
            {
                case WorldDisplayDollPayload e:
                    var a = Assert.IsType<WorldDisplayDollPayload>(actual[n].Payload);
                    Assert.Equal(e.Pose, a.Pose); Assert.Equal(e.Equipment, a.Equipment); Assert.Equal(e.Dyes, a.Dyes); Assert.Equal(e.Misc, a.Misc); break;
                case WorldHatRackPayload e:
                    var h = Assert.IsType<WorldHatRackPayload>(actual[n].Payload); Assert.Equal(e.Items, h.Items); Assert.Equal(e.Dyes, h.Dyes); break;
                default: Assert.Equal(expected[n].Payload, actual[n].Payload); break;
            }
        }
    }
    private static void SourcePoses()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = Source("World", platform);
            foreach (var row in source.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("action", out _)) continue;
                byte pose = 0;
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    Assert.True(VanillaFurnitureItemFacts1458.TryGetNextDollPose(pose, new(row.GetProperty("miscItem").GetUInt16()), 1, out pose));
                    Assert.Equal(step.GetProperty("entities")[0].GetProperty("pose").GetByte(), pose);
                    Assert.Equal(Convert.FromHexString(step.GetProperty("frames")[0].GetString()!), TerrariaDollPoseCodec1458.Encode(new(0, 0, pose, pose)));
                }
            }
        }
    }
    private static void StatePose()
    {
        using var f = new LarvaRuntimeFixture1458(OperatingSystem.IsWindows());
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
            f.Tiles.Set(102 + dx, 100 + dy, new WorldTile { Type = 470, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) });
        var doll = new WorldTileEntity(0, 102, 100, WorldTileEntityKind.DisplayDoll,
            new WorldDisplayDollPayload(0, new WorldTileEntityItem?[9], new WorldTileEntityItem?[9], null));
        Assert.True(f.Tiles.TryAttachTileEntities([doll], 100000));
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 1, 0, 7, 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        f.State.Tick(); f.Move(true);
        Send(f, TerrariaDollPoseCodec1458.Encode(new(f.Connection.Player.Slot.Value, 0, 1, 1)));
        Assert.Equal((byte)0, Assert.IsType<WorldDisplayDollPayload>(Assert.Single(f.Tiles.RuntimeTileEntities!.CaptureSnapshot()).Payload).Pose);
        f.State.Tick();
        Assert.Equal((byte)1, Assert.IsType<WorldDisplayDollPayload>(Assert.Single(f.Tiles.RuntimeTileEntities!.CaptureSnapshot()).Payload).Pose);
        Assert.Equal(1UL, f.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
        Send(f, TerrariaDollPoseCodec1458.Encode(new(f.Connection.Player.Slot.Value, 0, 1, 1)));
        Assert.Equal(1UL, f.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
    }
    private static void MetadataCurrentness()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 300));
        var doll = new WorldTileEntity(0, 50, 50, WorldTileEntityKind.DisplayDoll,
            new WorldDisplayDollPayload(0, new WorldTileEntityItem?[9], new WorldTileEntityItem?[9], null));
        Assert.True(tiles.TryAttachTileEntities([doll], 100000));
        var owner = tiles.RuntimeTileEntities!;
        Assert.True(owner.TryPrepare(4096, out var first)); Assert.True(owner.TryPrepare(4096, out var stale));
        Assert.True(first!.TryStageDisplayDollPose(50, 50, 0, 0, 1));
        var overlay = new ToolWorldTileOverlay1458(tiles);
        var section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, 50, 50); long before = tiles.GetSectionVersion(section);
        Assert.True(overlay.TryPrepareMetadata(first)); Assert.True(overlay.TryAdoptUnpublished(first));
        Assert.False(stale!.IsCurrentOwned); Assert.True(tiles.GetSectionVersion(section) > before);
        Assert.True(overlay.IsAcceptedCurrent); Assert.False(first.TryAdoptUnpublished());
        var saved = owner.CaptureSnapshot(); var mutable = Assert.IsType<WorldDisplayDollPayload>(saved[0].Payload);
        mutable.Equipment[0] = new(8, 0, 1);
        Assert.Null(Assert.IsType<WorldDisplayDollPayload>(owner.CaptureSnapshot()[0].Payload).Equipment[0]);
    }
    private static void StateAllFamilies()
    {
        foreach (ushort type in new ushort[] { 128, 269, 334, 470, 471, 475, 314 })
        {
            using var f = new LarvaRuntimeFixture1458(OperatingSystem.IsWindows());
            int width = type is 128 or 269 or 470 ? 2 : type == 314 ? 1 : 3;
            int height = type == 475 ? 4 : type == 314 ? 1 : 3;
            int top = 103 - height;
            short targetY = (short)(type == 334 ? top + 1 : top); // GetPickaxeDamage relocates legacy334 top/bottom cache entries; use its stable middle row.
            for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++)
                f.Tiles.Set(102 + dx, top + dy, new WorldTile { Type = type, Flags = WorldTileFlags.Active,
                    Wall = type is 334 or 471 ? (ushort)1 : (ushort)0, FrameX = (short)(dx * 18), FrameY = type == 314 ? (short)-1 : (short)(dy * 18) });
            WorldTileEntityPayload? payload = type switch
            {
                470 => new WorldDisplayDollPayload(0, new WorldTileEntityItem?[9], new WorldTileEntityItem?[9], null),
                475 => new WorldHatRackPayload(new WorldTileEntityItem?[2], new WorldTileEntityItem?[2]),
                471 => new WorldItemTileEntityPayload(default), _ => null
            };
            Assert.True(f.Tiles.TryAttachTileEntities(payload is null ? [] :
                [new WorldTileEntity(0, 102, (short)top, type == 470 ? WorldTileEntityKind.DisplayDoll : type == 475 ? WorldTileEntityKind.HatRack : WorldTileEntityKind.WeaponsRack, payload)], 100000));
            short tool = type == 314 ? (short)7 : (short)3509;
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection)); f.State.Tick();
            f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 1, 0, tool, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection)); f.Move(false); f.State.Tick(); f.State.Tick();
            if (type == 314) Assert.NotNull(f.Member.ItemPhase!.Value.ToolUse!.ReleaseWitness);
            f.Move(true); f.State.Tick();
            var image = Inventory(f.Players); Assert.True(image.TryCaptureShopJournal(f.Connection, out var inventory));
            var beforeItems = inventory!.Before.ToArray(); var beforeQ = inventory.Quantity.ToArray(); var beforeCopper = inventory.Copper.ToArray();
            int published = 0;
            var previousSink = ItemSink(f.Items);
            ItemSink(f.Items) = new ItemObserver(previousSink, (kind, born) =>
            {
                if (kind != WorldItemStateCommitKind.Drop) return;
                published++;
                Assert.Equal(3UL, f.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
                for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++) Assert.False(f.Tiles.Get(102 + dx, top + dy).IsActive);
                Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
                Assert.Equal((byte)100, born.EnemyGrabDelayTime); // case17 DefaultAssignNewItemsToPlayer scope
                var serial = f.Member.ItemPhase!.Value.ToolUse!.ActionSerial;
                f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, new(type is 470 or 475 ? (byte)20 : (byte)0, 102, targetY, 0, 0)));
                Assert.Equal(serial, f.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
            });
            if (type == 314)
            {
                // Import the independently captured canonical front for this
                // topology. Raw front0 is not a source-framed through track.
                using var trackSource = Source("Tracks", "Linux");
                var sourceTrack = trackSource.RootElement.EnumerateArray().First(r => r.GetProperty("mask").GetInt32() == 26 &&
                    r.GetProperty("front").GetInt32() == 0 && r.GetProperty("pound").GetBoolean());
                var center = f.Tiles.Get(102, 102); center.FrameX = sourceTrack.GetProperty("before").GetProperty("frameX").GetInt16();
                f.Tiles.Set(102, 102, center);
                f.Tiles.Set(101, 102, new WorldTile { Type = 314, Flags = WorldTileFlags.Active, FrameX = 0, FrameY = -1 });
                f.Tiles.Set(103, 102, new WorldTile { Type = 314, Flags = WorldTileFlags.Active, FrameX = 0, FrameY = -1 });
                f.Tiles.Set(103, 101, new WorldTile { Type = 314, Flags = WorldTileFlags.Active, FrameX = 0, FrameY = -1 });
                Send(f, new TerrariaTileManipulationState(15, 102, (short)top, 1, 0));
                Assert.Equal(1UL, f.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
                Assert.True(f.Tiles.Get(102, top).IsActive);
            }
            else
            {
                for (int hit = 0; hit < 3; hit++)
                {
                    Send(f, new TerrariaTileManipulationState(type is 470 or 475 && hit == 2 ? (byte)20 : (byte)0,
                        102, targetY, hit < 2 ? (short)1 : (short)0, 0));
                    if (hit < 2) Send(f, new TerrariaTilePickingState1458(f.Connection.Player.Slot.Value, 102, targetY, 35));
                    Assert.True((ulong)(hit + 1) == f.Member.ItemPhase!.Value.ToolUse!.ActionSerial, $"type{type}/hit{hit}: serial{f.Member.ItemPhase!.Value.ToolUse!.ActionSerial}, retained{f.Composition.WorldTileAuthority.RetainedToolReports}, rejected{f.Composition.WorldTileAuthority.RejectedToolReports}, input{f.Member.ItemPhase!.Value.ToolUse!.Input?.Count}");
                    if (hit < 2)
                    {
                        Assert.True(f.Tiles.Get(102, top).IsActive);
                        int budget = 60;
                        do { f.State.Tick(); Assert.True(budget-- > 0); } while (f.Member.ItemPhase!.Value.ToolUse!.Clocks.ToolTime > 0);
                        Assert.True(budget > 0);
                    }
                }
                for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++) Assert.False(f.Tiles.Get(102 + dx, top + dy).IsActive);
                Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot()); Assert.Equal(1, f.Items.ActiveCount); Assert.Equal(1, published);
            }
            Assert.True(image.TryCaptureShopJournal(f.Connection, out var after));
            Assert.Equal(beforeItems, after!.Before.ToArray()); Assert.Equal(beforeQ, after.Quantity.ToArray()); Assert.Equal(beforeCopper, after.Copper.ToArray());
        }
    }
    private static void Send(LarvaRuntimeFixture1458 fixture, TerrariaTileManipulationState state)
    {
        Assert.Equal(TerrariaTileManipulationEncodeResult.Encoded, TerrariaTileManipulationCodec.TryEncode(in state, out var bytes)); Send(fixture, bytes);
    }
    private static void Send(LarvaRuntimeFixture1458 fixture, TerrariaTilePickingState1458 state) => Send(fixture, TerrariaTilePickingCodec1458.Encode(in state));
    private static void Send(LarvaRuntimeFixture1458 fixture, byte[] bytes)
    {
        var pool = new PlayerSlotPool(3); Assert.True(pool.TryAcquireConnection(out var a)); Assert.True(pool.TryAcquireConnection(out var b));
        using (a!) using (b!)
        using (var bootstrap = new PlayerBootstrapFrameSink(pool, new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 8192, 2048)),
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData }, [], new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }), fixture.Connection.Source, new Spawn()))
        {
            Frame(bootstrap, (byte)TerrariaMessageId.Hello, [11, (byte)'T',(byte)'e',(byte)'r',(byte)'r',(byte)'a',(byte)'r',(byte)'i',(byte)'a',(byte)'3',(byte)'2',(byte)'6']);
            Frame(bootstrap, (byte)TerrariaMessageId.RequestWorldData, []); Frame(bootstrap, (byte)TerrariaMessageId.SpawnTileData, new byte[9]);
            var spawn = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength]; spawn[0] = fixture.Connection.Player.Slot.Value;
            Frame(bootstrap, (byte)TerrariaMessageId.PlayerSpawn, spawn); Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
            var inbox = new Inbox(); var sink = new TileManipulationFrameSink(fixture.Connection.Source, bootstrap, new Pass(), new RuntimeTileNetworkIngress(inbox));
            var packet = new TerrariaFrame((ushort)bytes.Length, bytes[2], new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in packet)); Assert.NotNull(inbox.Command); fixture.Apply(inbox.Command!);
        }
    }
    private static void Frame(ITerrariaFrameSink sink, byte id, byte[] payload)
    {
        byte[] bytes = new byte[payload.Length + 3]; bytes[0] = (byte)bytes.Length; bytes[1] = (byte)(bytes.Length >> 8); bytes[2] = id; payload.CopyTo(bytes, 3);
        var frame = new TerrariaFrame((ushort)bytes.Length, id, new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(payload)); Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
    }
    private sealed class Spawn : IPlayerSpawnCommitIngress
    {
        public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request) => session.TryCommitSpawn(request.ClaimedSlot) == PlayerSpawnCommitResult.Committed;
    }
    private sealed class Pass : ITerrariaFrameSink { public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue; }
    private sealed class Inbox : IGameCommandIngress<RuntimeCommand>
    {
        internal RuntimeCommand? Command;
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { Assert.Null(Command); Command = command; return true; }
    }
    private static void CrossObjectGuard()
    {
        using var f = new LarvaRuntimeFixture1458(false);
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++) f.Tiles.Set(50 + dx, 50 + dy,
            new WorldTile { Type = 470, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) });
        for (int dx = 0; dx < 2; dx++) f.Tiles.Set(50 + dx, 53, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        // Deliberately configured unsupported neighbouring Larva. Source
        // destroyObject skips Check3x3 while the doll's recursive clear runs.
        for (int dx = 0; dx < 3; dx++) for (int dy = 0; dy < 3; dy++) f.Tiles.Set(52 + dx, 50 + dy,
            new WorldTile { Type = 231, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) });
        Assert.True(f.Tiles.TryAttachTileEntities([new(0, 50, 50, WorldTileEntityKind.DisplayDoll,
            new WorldDisplayDollPayload(0, new WorldTileEntityItem?[9], new WorldTileEntityItem?[9], null))], 100000));
        var world = f.Composition.WorldTileAuthority; Assert.True(world.TryCaptureToolTreeWorld(out var context));
        Assert.True(f.Players.TryCaptureToolTreeActors(out var actors)); var overlay = new ToolWorldTileOverlay1458(f.Tiles);
        Assert.True(world.TryCreateToolMutationPlan(overlay, out var plan));
        using (plan!)
        {
            Assert.True(world.TryCreateToolSourcePass(overlay, plan!.SourceRandom, false, false, context!.Facts, actors!.Actors, plan, out var source));
            Assert.True(source!.TryKillTile(50, 50, false, false, false, out _));
            Assert.True(plan.TrySeal()); Assert.True(plan.TryAdoptWholeUnpublished(out _));
            Assert.Equal(0, f.Npcs.ActiveCount);
            for (int dx = 0; dx < 3; dx++) for (int dy = 0; dy < 3; dy++) Assert.True(f.Tiles.Get(52 + dx, 50 + dy).IsActive);
            Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
        }
    }
    private sealed class ItemObserver(IWorldItemStateCommitSink? inner, Action<WorldItemStateCommitKind, WorldItemSnapshot> action) : IWorldItemStateCommitSink
    {
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot, int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        { action(kind, snapshot); inner?.WorldItemStateCommitted(kind, in snapshot, reservationOffer, excludedSource); }
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_commitSink")] private static extern ref IWorldItemStateCommitSink? ItemSink(RuntimeWorldItemStore owner);
    private static void CheckpointAndSection()
    {
        byte[] original = TerraRuntime.Tests.DurableWorldChestCustody.WorldFixture.CreateCompleteCurrentWorld(100, 100);
        var limits = TerraRuntime.Tests.DurableWorldChestCustody.WorldFixture.CreateLimits() with { MaxTileCount = 10000 };
        Assert.True(WorldFileLoader.TryLoad(original, limits, out var world).IsLoaded); Assert.NotNull(world);
        Assert.True(WorldFilePreservedSections.TryCapture(original, world.Envelope, out var preserved));
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++) world.Tiles.Set(50 + dx, 50 + dy,
            new WorldTile { Type = 470, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) });
        var doll = new WorldTileEntity(0, 50, 50, WorldTileEntityKind.DisplayDoll,
            new WorldDisplayDollPayload(0, new WorldTileEntityItem?[9], new WorldTileEntityItem?[9], null));
        Assert.True(world.Tiles.TryAttachTileEntities([doll], 100000));
        var metadata = world.Tiles.RuntimeTileEntities!; Assert.True(metadata.TryPrepare(4096, out var pose));
        Assert.True(pose!.TryStageDisplayDollPose(50, 50, 0, 0, 1));
        var overlay = new ToolWorldTileOverlay1458(world.Tiles); Assert.True(overlay.TryPrepareMetadata(pose)); Assert.True(overlay.TryAdoptUnpublished(pose));
        var snapshots = new RuntimeWorldCheckpointSnapshotSource(world.Tiles, new RuntimeChestStore([]), 8);
        while (!snapshots.IsTileShadowReady) snapshots.CaptureTileBootstrap(8);
        Assert.True(snapshots.TryCapture(out var snapshot)); Assert.NotNull(snapshot); Assert.NotNull(snapshot.TileEntities);
        Assert.Equal((byte)1, Assert.IsType<WorldDisplayDollPayload>(Assert.Single(snapshot.TileEntities!).Payload).Pose);
        Assert.True(metadata.TryPrepare(4096, out var next)); Assert.True(next!.TryStageDisplayDollPose(50, 50, 0, 1, 2));
        var second = new ToolWorldTileOverlay1458(world.Tiles); Assert.True(second.TryPrepareMetadata(next)); Assert.True(second.TryAdoptUnpublished(next));
        using var entityBytes = new MemoryStream(); Assert.Equal(WorldFileTileEntityEncodeResult.Encoded,
            WorldFileTileEntityEncoder.TryEncode(snapshot.TileEntities, world.Header.Dimensions, 100000, entityBytes, out _));
        using var target = new MemoryStream();
        var bits = world.Envelope.FrameImportanceBits.ToArray();
        for (int type = 0; type < world.Envelope.FrameImportanceCount; type++) if (VanillaWorldFrameImportance326.IsFrameImportant(type)) bits[type >> 3] |= (byte)(1 << (type & 7));
        var envelope = new WorldFileEnvelope(world.Envelope.FormatVersion, world.Envelope.Revision, world.Envelope.FavoriteFlags,
            world.Envelope.SectionOffsets.ToArray(), world.Envelope.FrameImportanceCount, bits);
        Assert.Equal(WorldFileTileChestRewriteResult.Rewritten, WorldFileTileChestRewriter.TryRewrite(envelope, world.Header, preserved!.Header.Span,
            preserved, snapshot.Tiles, snapshot.Chests, preserved.Signs.Span, preserved.Npcs.Span, preserved.TownRooms.Span,
            target, out _, null, null, entityBytes.ToArray()));
        Assert.True(WorldFileLoader.TryLoad(target.ToArray(), limits, out var restored).IsLoaded); Assert.NotNull(restored);
        Assert.Equal((byte)1, Assert.IsType<WorldDisplayDollPayload>(Assert.Single(restored.TileEntities).Payload).Pose);
        Assert.Equal((byte)2, Assert.IsType<WorldDisplayDollPayload>(Assert.Single(metadata.CaptureSnapshot()).Payload).Pose);
        Assert.Equal((short)18, restored.Tiles.Get(51, 50).FrameX);
        Assert.True(world.Tiles.TryCaptureSectionSnapshot(new(0, 0), out var section));
        Assert.Equal(WorldSectionPacketSnapshotCaptureResult.Captured, WorldSectionPacketSnapshotCapture.TryCapture(world, section!, WorldSectionEncodingContext.Capture(world), out var packet));
        Assert.NotNull(packet); Assert.NotEmpty(packet.ObjectMetadata.ToArray());
        Assert.True(metadata.TryPrepare(4096, out var removal)); Assert.True(removal!.TryStageRemoval(50, 50, WorldTileEntityKind.DisplayDoll));
        var removed = new ToolWorldTileOverlay1458(world.Tiles); Assert.True(removed.TryPrepareMetadata(removal)); Assert.True(removed.TryAdoptUnpublished(removal));
        Assert.Equal(WorldSectionPacketSnapshotCaptureResult.StaleTileSnapshot, WorldSectionPacketSnapshotCapture.TryCapture(world, section!, WorldSectionEncodingContext.Capture(world), out _));
        Assert.Empty(metadata.CaptureSnapshot());
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")] private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    private static VanillaUnifiedRandom1458 ReadRandom(JsonElement row)
    {
        var random = new VanillaUnifiedRandom1458(0); Cursor(random) = row.GetProperty("cursor").GetUInt32();
        row.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo(Seed(random), 0); return random;
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")] private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")] private static extern ref int[] Seed(VanillaUnifiedRandom1458 random);
}
