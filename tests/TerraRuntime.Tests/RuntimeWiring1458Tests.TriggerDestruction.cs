using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    public static IEnumerable<object[]> OriginalWiringTriggerDestruction()
    {
        foreach (string resource in new[] { "WiringTriggerDestructionLinux1458", "WiringLiveTriggerDestructionLinux1458" })
        {
            using var input = typeof(RuntimeWiring1458Tests).Assembly.GetManifestResourceStream(resource)!;
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var json = JsonDocument.Parse(gzip);
            foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
                yield return [row.GetRawText()];
        }
    }

    [Theory, MemberData(nameof(OriginalWiringTriggerDestruction))]
    public void Dedicated_kill_transaction_matches_source_wire_release_drops_and_metadata(string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        ushort type = row.GetProperty("type").GetUInt16();
        int style = row.GetProperty("style").GetInt32();
        bool on = row.GetProperty("on").GetBoolean();
        bool noItem = row.GetProperty("noItem").GetBoolean();
        bool occupiedCapture = type == 428 && row.TryGetProperty("beforePressed", out _);
        bool reloaded = occupiedCapture && row.GetProperty("reload").GetBoolean();
        int colors = row.GetProperty("colors").GetInt32();
        int killY = row.GetProperty("supportOnly").GetBoolean() ? 41 : 40;
        var random = new VanillaUnifiedRandom1458(1458);
        var itemReplication = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(itemReplication);
        using var scene = CreateDisposableScene(random, items, itemReplication: itemReplication);
        // The dedicated capture has a packet recorder but no active item-owner
        // candidates. Keep the observation endpoint separate from actor state.
        if (!occupiedCapture) scene.State.Apply(new PlayerDisconnectRuntimeCommand(scene.Owner));
        var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(8001), new(new PlayerSlotId(1), new(1)));
        Assert.True(itemReplication.TryRegister(observer.Source, scene.PeerOutbound));
        var spawn = new PlayerSpawnCommitRequest(observer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        itemReplication.PlayerSpawned(observer, in spawn);
        var plate = new WorldTile
        {
            Type = type,
            FrameX = (short)(type == 423 && on ? 18 : 0),
            FrameY = (short)(style * 18),
            Flags = WorldTileFlags.Active
        };
        scene.Tiles.Set(30, 40, in plate);
        scene.Tiles.Set(33, 40, new WorldTile { Type = 4, Flags = WorldTileFlags.Active });
        scene.Tiles.Set(30, 41, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 30; x <= 33; x++)
        {
            var tile = scene.Tiles.Get(x, 40);
            if (colors != 0) tile.TrySetFlags(WorldTileFlags.WireRed, true);
            if (colors == 15)
                tile.TrySetFlags(WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow, true);
            scene.Tiles.Set(x, 40, tile);
        }
        WorldPressurePlate[] pressure = type == 428 && on && (!occupiedCapture || reloaded) ? [new(30, 40)] : [];
        WorldTileEntity[] entities = type == 423
            ? [new(0, 30, 40, WorldTileEntityKind.LogicSensor, new WorldLogicSensorPayload((byte)(style + 1), on))]
            : [];
        Assert.True(scene.Tiles.TryAttachPressurePlates(pressure, 16, fromWorldFile: reloaded));
        if (occupiedCapture)
        {
            Assert.True(Graph(scene).Players.TryGet(scene.Owner, out var player));
            player.PositionX = 481;
            player.PositionY = 613;
            if (!reloaded)
                Assert.True(scene.Tiles.RuntimePressurePlates!.Update(scene.Owner.Player, 481, 613, 20, 42, static (_, _) => { }));
        }
        Assert.True(scene.Tiles.TryAttachTileEntities(entities, 16));
        scene.DrainFrames(scene.PeerOutbound);
        random.CopyStateFrom(new VanillaUnifiedRandom1458(1458));
        var (plan, source) = PrepareTriggerDestruction(scene);
        using (plan)
        {
            Assert.True(source.TryKillTile(30, killY, false, false, noItem, out var result));
            Assert.True(result.Changed);
            Assert.True(plan.TrySeal());
            Assert.True(plan.TryAdoptWholeUnpublished(out var publication));
            Assert.NotNull(publication);
            var end = plan.CapturePublicationOffset();
            Assert.True(publication.TryPublishThrough(in end));
        }
        Assert.Equal(WiringLogicGate1458Tests.Decode(row.GetProperty("point")), scene.Tiles.Get(30, 40));
        var expectedSupport = WiringLogicGate1458Tests.Decode(row.GetProperty("support"));
        // Active non-frame-important stone retains client cosmetic frame fields in the capture.
        var support = scene.Tiles.Get(30, 41);
        Assert.Equal(expectedSupport.Type, support.Type);
        Assert.Equal(expectedSupport.IsActive, support.IsActive);
        Assert.Equal(WiringLogicGate1458Tests.Decode(row.GetProperty("torch")), scene.Tiles.Get(33, 40));
        Assert.Equal(row.GetProperty("sensorCount").GetInt32(), scene.Tiles.RuntimeTileEntities!.CaptureSnapshot().Length);
        Assert.Equal(row.GetProperty("pressed").EnumerateArray().Select(p => new WorldPressurePlate(
            p.GetProperty("x").GetInt16(), p.GetProperty("y").GetInt16())), scene.Tiles.RuntimePressurePlates!.CaptureSnapshot());
        var expectedItems = row.GetProperty("items");
        Assert.Equal(expectedItems.GetArrayLength(), items.ActiveCount);
        foreach (var expected in expectedItems.EnumerateArray())
        {
            Assert.True(items.TryGetActive(expected.GetProperty("slot").GetInt16(), out var item));
            Assert.Equal(expected.GetProperty("type").GetInt32(), item.ItemNetId);
            Assert.Equal(expected.GetProperty("stack").GetInt32(), item.Stack);
        }
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(f => f.GetString()),
            GemBunnyWire1458Tests.Drain(scene.PeerOutbound).Select(Convert.ToHexString));
        AssertPlateRandom(row.GetProperty("random"), random);
    }

    [Fact]
    public void New_pressure_occupancy_invalidates_a_sealed_drop_before_any_state_is_adopted()
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var items = new RuntimeWorldItemStore();
        using var scene = CreateDisposableScene(random, items);
        scene.Put(30, 40, 428);
        Assert.True(scene.Tiles.TryAttachPressurePlates([], 16));
        var (plan, source) = PrepareTriggerDestruction(scene);
        using (plan)
        {
            Assert.True(source.TryKillTile(30, 40, false, false, false, out var result));
            Assert.True(result.Changed);
            Assert.True(plan.TrySeal());
            // Occupancy can change the source KillTile branch without changing a tile section.
            Assert.True(scene.Tiles.RuntimePressurePlates!.Update(scene.Owner.Player, 481, 613, 20, 42, static (_, _) => { }));
            Assert.False(plan.TryAdoptWholeUnpublished(out _));
        }
        Assert.True(scene.Tiles.Get(30, 40).IsActive);
        Assert.Equal(0, items.ActiveCount);
        Assert.Single(scene.Tiles.RuntimePressurePlates!.CaptureSnapshot());
    }

    [Theory]
    [InlineData(423)]
    [InlineData(428)]
    public void Unsupported_device_after_a_torch_rejects_the_whole_release(ushort type)
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var items = new RuntimeWorldItemStore();
        using var scene = CreateDisposableScene(random, items);
        var root = new WorldTile { Type = type, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
            FrameX = (short)(type == 423 ? 18 : 0), FrameY = (short)(type == 423 ? 36 : 0) };
        var torch = new WorldTile { Type = 4, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
        scene.Tiles.Set(30, 40, in root);
        scene.Tiles.Set(31, 40, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(32, 40, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(33, 40, in torch);
        scene.Tiles.Set(34, 40, new WorldTile { Type = 137, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        WorldPressurePlate[] pressure = type == 428 ? [new(30, 40)] : [];
        WorldTileEntity[] entities = type == 423
            ? [new(0, 30, 40, WorldTileEntityKind.LogicSensor, new WorldLogicSensorPayload(3, true))] : [];
        Assert.True(scene.Tiles.TryAttachPressurePlates(pressure, 16));
        Assert.True(scene.Tiles.TryAttachTileEntities(entities, 16));
        scene.DrainFrames(scene.PeerOutbound);
        var beforeRandom = random.Clone();
        var (plan, source) = PrepareTriggerDestruction(scene);
        using (plan)
        {
            Assert.False(source.TryKillTile(30, 40, false, false, false, out _));
            Assert.False(plan.TrySeal());
        }
        Assert.Equal(root, scene.Tiles.Get(30, 40));
        Assert.Equal(torch, scene.Tiles.Get(33, 40));
        Assert.Equal(pressure, scene.Tiles.RuntimePressurePlates!.CaptureSnapshot());
        Assert.Equal(entities, scene.Tiles.RuntimeTileEntities!.CaptureSnapshot());
        Assert.Empty(scene.DrainFrames(scene.PeerOutbound));
        Assert.Equal(0, items.ActiveCount);
        Assert.True(random.HasSameState(beforeRandom));
    }

    [Fact]
    public void A_loaded_key_acquiring_a_live_occupant_refuses_a_prepared_release()
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var items = new RuntimeWorldItemStore();
        using var scene = CreateDisposableScene(random, items);
        var root = new WorldTile { Type = 428, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
        var torch = new WorldTile { Type = 4, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
        scene.Tiles.Set(30, 40, in root);
        scene.Tiles.Set(31, 40, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(32, 40, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(33, 40, in torch);
        Assert.True(scene.Tiles.TryAttachPressurePlates([new(30, 40)], 16, fromWorldFile: true));
        scene.DrainFrames(scene.PeerOutbound);
        var beforeRandom = random.Clone();
        var (plan, source) = PrepareTriggerDestruction(scene);
        using (plan)
        {
            Assert.True(source.TryKillTile(30, 40, false, false, false, out var result));
            Assert.True(result.Changed);
            Assert.True(plan.TrySeal());
            Assert.True(scene.Tiles.RuntimePressurePlates!.Update(scene.Owner.Player, 481, 613, 20, 42, static (_, _) => { }));
            Assert.False(plan.TryAdoptWholeUnpublished(out _));
        }
        Assert.Equal(root, scene.Tiles.Get(30, 40));
        Assert.Equal(torch, scene.Tiles.Get(33, 40));
        Assert.Single(scene.Tiles.RuntimePressurePlates!.CaptureSnapshot());
        Assert.Empty(scene.DrainFrames(scene.PeerOutbound));
        Assert.Equal(0, items.ActiveCount);
        Assert.True(random.HasSameState(beforeRandom));
    }

    [Fact]
    public void Failed_framing_invalidates_a_release_already_staged_in_the_tool_plan()
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var items = new RuntimeWorldItemStore();
        using var scene = CreateDisposableScene(random, items);
        var root = new WorldTile { Type = 428, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
        var torch = new WorldTile { Type = 4, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
        scene.Tiles.Set(30, 40, in root);
        scene.Tiles.Set(31, 40, in torch);
        Assert.True(scene.Tiles.TryAttachPressurePlates([new(30, 40)], 16));
        scene.DrainFrames(scene.PeerOutbound);
        var beforeRandom = random.Clone();
        var (plan, source) = PrepareTriggerDestruction(scene);
        using (plan)
        {
            Assert.False(source.TryKillTile(30, 40, false, false, false, out _));
            Assert.False(plan.TrySeal());
            Assert.False(plan.TryAdoptWholeUnpublished(out _));
        }
        Assert.Equal(root, scene.Tiles.Get(30, 40));
        Assert.Equal(torch, scene.Tiles.Get(31, 40));
        Assert.Single(scene.Tiles.RuntimePressurePlates!.CaptureSnapshot());
        Assert.Empty(scene.DrainFrames(scene.PeerOutbound));
        Assert.Equal(0, items.ActiveCount);
        Assert.True(random.HasSameState(beforeRandom));
    }

    private static (WorldTileAuthority.ToolWorldMutationPlan Plan, WorldTileAuthority.ToolWorldSourcePass1458 Source)
        PrepareTriggerDestruction(Scene scene)
    {
        var graph = Graph(scene);
        var owner = graph.WorldTileAuthority;
        Assert.True(owner.TryCaptureToolTreeWorld(out var context));
        Assert.NotNull(context);
        Assert.True(graph.Players.TryCaptureToolTreeActors(out var actors));
        Assert.NotNull(actors);
        var overlay = new ToolWorldTileOverlay1458(scene.Tiles);
        Assert.True(owner.TryCreateToolMutationPlan(overlay, out var plan));
        Assert.NotNull(plan);
        Assert.True(owner.TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode,
            context.Facts, actors.Actors, plan, out var source));
        Assert.NotNull(source);
        return (plan, source);
    }
}
