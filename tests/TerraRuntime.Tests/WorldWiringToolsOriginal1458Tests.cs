using System.IO.Compression;
using System.Buffers.Binary;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Tests.Larva1458;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldWiringToolsOriginal1458Tests
{
    [Fact]
    public void Original_full_ItemCheck_installation_frames_match_shared_clock_tail_and_independent_packet_bytes()
    {
        using JsonDocument reference = Load();
        Assert.Empty(reference.RootElement.GetProperty("errors").EnumerateArray());
        int count = 0, successful = 0;
        foreach (JsonElement row in reference.RootElement.GetProperty("rows").EnumerateArray())
        {
            int tool = I(row, "tool");
            if (tool == 510) continue; // Qualified client cutter deliberately excluded: see the visual-state Fact.
            count++;
            Assert.True(VanillaWiringToolCatalog1458.TryGet(new(tool), out var definition));
            JsonElement metadata = row.GetProperty("metadata"), before = row.GetProperty("before"), after = row.GetProperty("after"), profile = row.GetProperty("profile");
            Assert.Equal(I(metadata, "useTime"), definition.UseTime);
            Assert.Equal(I(metadata, "useAnimation"), definition.UseAnimation);
            Assert.Equal(I(metadata, "tileBoost"), definition.TileBoost);
            Assert.Equal(metadata.GetProperty("consumable").GetBoolean(), definition.Kind == WiringToolKind1458.Actuator);
            Assert.Equal((byte)I(metadata, "useStyle"), definition.Clocks.UseStyle);
            Assert.Equal(metadata.GetProperty("autoReuse").GetBoolean(), definition.Clocks.AutoReuse);
            var random = new VanillaUnifiedRandom1458(1458);
            int crit = random.Next(3);
            var facts = new ToolFrameFacts1458(true, B(profile, "Control"), B(before, "lastSuccess"),
                B(before, "noItems"), B(before, "cced"), false, false, 1f,
                B(before, "allowed"), B(before, "justDropped"));
            Assert.True(VanillaLocalItemUse1458.TryAdvanceFrame(definition.Clocks, Clock(before), facts, crit, out var frame));
            ToolClockState1458 result;
            byte[][] reports = row.GetProperty("frames").EnumerateArray().Select(v => Convert.FromHexString(v.GetString()!))
                .Where(v => v[2] == 17).ToArray();
            if (reports.Length > 0)
            {
                successful++;
                byte[] actual = Assert.Single(reports);
                int selectedStack = before.GetProperty("inventory").EnumerateArray().Single(v => I(v, "slot") == 0).GetProperty("stack").GetInt32();
                Assert.True(VanillaWiringTool1458.TryPlanTail(definition, frame.State, selectedStack, out result));
                var state = new TerrariaTileManipulationState(definition.PlaceAction,
                    (short)I(profile, "TargetX"), (short)I(profile, "TargetY"), 0, 0);
                Assert.Equal(TerrariaTileManipulationEncodeResult.Encoded, TerrariaTileManipulationCodec.TryEncode(state, out var encoded));
                Assert.Equal(actual, encoded);
                WorldTile tile = ReadTile(before.GetProperty("tile"));
                var request = new WorldWiringMutationRequest(definition.Kind == WiringToolKind1458.Actuator
                    ? WorldWiringMutationKind.PlaceActuator : WorldWiringMutationKind.PlaceWire,
                    0, 0, (WorldWireChannel)definition.Channel);
                var mutation = VanillaWorldWiringMutationService.Evaluate(request, tile);
                Assert.True(mutation.Applied);
                AssertTile(after.GetProperty("tile"), mutation.After);
            }
            else Assert.True(VanillaLocalItemUse1458.TryEnterOwnerStage(definition.Clocks, frame.State, frame.LocalToolStage, out result));
            AssertClock(after, result);
            Assert.Equal(I(after, "next"), random.Next());
        }
        Assert.Equal(26, count);
        Assert.Equal(14, successful);
    }

    [Fact]
    public void Real_client_cutter_visual_pool_changes_RNG_and_prevents_qualifying_it_from_the_headless_projection()
    {
        using JsonDocument reference = Load();
        JsonElement[] rows = reference.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        JsonElement visible = rows.Single(v => I(v, "tool") == 510 && v.GetProperty("profile").GetProperty("Name").GetString() == "funded");
        JsonElement full = rows.Single(v => I(v, "tool") == 510 && v.GetProperty("profile").GetProperty("Name").GetString() == "visible-full-dust-pool");
        Assert.Equal(visible.GetProperty("after").GetProperty("tile").GetRawText(), full.GetProperty("after").GetProperty("tile").GetRawText());
        Assert.Equal(visible.GetProperty("frames").GetRawText(), full.GetProperty("frames").GetRawText());
        Assert.Equal(5, I(visible.GetProperty("after"), "time")); Assert.Equal(5, I(full.GetProperty("after"), "time"));
        Assert.Equal(31, I(visible.GetProperty("after").GetProperty("random"), "cursor"));
        Assert.Equal(1, I(full.GetProperty("after").GetProperty("random"), "cursor"));
        Assert.NotEqual(I(visible.GetProperty("after"), "next"), I(full.GetProperty("after"), "next"));
        Assert.False(VanillaWiringToolCatalog1458.TryGet(new(510), out _));
        Assert.False(VanillaWiringToolCatalog1458.TryGet(new(3611), out _));
        Assert.False(VanillaWiringToolCatalog1458.TryGet(new(3625), out _));
    }

    [Fact]
    public void Original_air_and_inactive_actuator_and_four_wire_cycles_match_the_shared_primitive_owner()
    {
        using JsonDocument reference = Load();
        int count = 0;
        foreach (JsonElement row in reference.RootElement.GetProperty("mutations").EnumerateArray())
        {
            count++;
            bool wire = row.GetProperty("kind").GetString() == "wire-cycle";
            var tiles = new WorldTileStore(new WorldDimensions(80, 100));
            WorldTile original = ReadTile(row.GetProperty("before")); tiles.Set(20, 20, original);
            var service = new VanillaWorldWiringMutationService(tiles);
            var request = new WorldWiringMutationRequest(wire ? WorldWiringMutationKind.PlaceWire : WorldWiringMutationKind.PlaceActuator,
                20, 20, wire ? (WorldWireChannel)I(row, "color") : default);
            Assert.Equal(B(row, "placed"), service.Apply(request).Applied);
            AssertTile(row.GetProperty("installed"), tiles.Get(20, 20));
            Assert.Equal(B(row, "duplicate"), service.Apply(request).Applied);
            request = request with { Kind = wire ? WorldWiringMutationKind.KillWire : WorldWiringMutationKind.KillActuator };
            Assert.Equal(B(row, "removed"), service.Apply(request).Applied);
            AssertTile(row.GetProperty("after"), tiles.Get(20, 20));
            Assert.Equal(B(row, "absent"), service.Apply(request).Applied);
            // This primitive owns tile bits only. Original server drop/RNG/bytes remain in the fixture;
            // the source-framing Fact below exercises their existing production owners through Apply.
        }
        Assert.Equal(6, count);
    }

    [Fact]
    public void Real_switch_command_matches_original_ABI_projection_drop_body_owner_packet_and_public_RNG_offer()
    {
        using JsonDocument reference = Load();
        int count = 0;
        foreach (JsonElement row in reference.RootElement.GetProperty("framing").EnumerateArray().Where(v => I(v, "seed") == 7))
        {
            count++;
            using var scene = new LarvaRuntimeFixture1458(windows: true, sourcePlayerZero: true);
            int sourceX = I(row, "triggerX"), supportX = I(row, "supportX");
            JsonElement before = row.GetProperty("before"), after = row.GetProperty("after");
            WorldTile trigger = ReadTile(before.GetProperty("trigger")); trigger.Wall = 1;
            scene.Tiles.Set(sourceX, 21, trigger);
            for (int x = sourceX + 1; x < supportX; x++) scene.Tiles.Set(x, 21, new WorldTile { Flags = WorldTileFlags.WireRed });
            scene.Tiles.Set(supportX, 21, ReadTile(before.GetProperty("stone")));
            scene.Tiles.Set(supportX, 20, ReadTile(before.GetProperty("attachment")));
            Assert.Equal(I(before, "next"), scene.Random.Clone().Next());
            scene.Apply(new ClientHitSwitchRuntimeCommand1458(scene.Connection, new((short)sourceX, 21)));
            // TileFrameCosmetic computes the isolated stone's source atlas coordinates even while
            // actuated. Existing ToolSourcePass deliberately omits non-frame-important merge frames,
            // which canonical tile and wire codecs omit too. Pin the original observation without
            // claiming that unrepresented rendering state; attachment frames remain exact below.
            Assert.Equal(162, I(after.GetProperty("stone"), "frameX"));
            Assert.Equal(54, I(after.GetProperty("stone"), "frameY"));
            AssertTile(after.GetProperty("stone"), scene.Tiles.Get(supportX, 21), includeFrames: false);
            AssertTile(after.GetProperty("attachment"), scene.Tiles.Get(supportX, 20));
            AssertTile(after.GetProperty("trigger"), scene.Tiles.Get(sourceX, 21));
            Assert.Equal(I(after, "next"), scene.Random.Clone().Next());
            JsonElement[] drops = after.GetProperty("drops").EnumerateArray().ToArray();
            var actual = new WorldItemSnapshot[4]; int actualCount = scene.Items.CopyActive(actual);
            Assert.Equal(drops.Length, actualCount);
            for (int i = 0; i < actualCount; i++)
            {
                JsonElement expected = drops[i]; WorldItemSnapshot item = actual[i];
                Assert.Equal((short)I(expected, "type"), item.ItemNetId); Assert.Equal((short)I(expected, "stack"), item.Stack);
                Assert.Equal(expected.GetProperty("x").GetSingle(), item.PositionX); Assert.Equal(expected.GetProperty("y").GetSingle(), item.PositionY);
                Assert.Equal(expected.GetProperty("vx").GetSingle(), item.VelocityX); Assert.Equal(expected.GetProperty("vy").GetSingle(), item.VelocityY);
                Assert.Equal((byte)I(expected, "owner"), item.OwnerPlayerId);
                // ReserveFor's wire argument does not replace the server field. Both roles
                // remain independently pinned: raw snapshot0 and original live packet22 offer15.
                Assert.Equal(0, I(expected, "keep"));
                Assert.Equal(I(expected, "keep"), item.TimeToKeepReservation);
                AssertOriginalOwnerPacket(row.GetProperty("frames"), item);
                Assert.Equal((byte)I(expected, "enemyDelay"), item.EnemyGrabDelayTime);
                Assert.Equal((byte)I(expected, "grabPlayer"), item.GrabDelayPlayer); Assert.Equal(I(expected, "grabTime"), item.GrabDelayTime);
            }
        }
        Assert.Equal(2, count);
    }

    private static ToolClockState1458 Clock(JsonElement state) => new(new(I(state,"time"),I(state,"timeMax"),
        I(state,"animation"),I(state,"animationMax"),B(state,"release"),0,I(state,"revolver")),I(state,"toolTime"),B(state,"pound"),B(state,"channel")) { ReuseDelay=I(state,"reuse") };
    private static void AssertClock(JsonElement expected, ToolClockState1458 actual)
    {
        Assert.Equal(I(expected,"time"),actual.Selected.ItemTime);Assert.Equal(I(expected,"timeMax"),actual.Selected.ItemTimeMax);
        Assert.Equal(I(expected,"animation"),actual.Selected.Animation);Assert.Equal(I(expected,"animationMax"),actual.Selected.AnimationMax);
        Assert.Equal(I(expected,"toolTime"),actual.ToolTime);Assert.Equal(B(expected,"release"),actual.Selected.ReleaseUseItem);
        Assert.Equal(I(expected,"revolver"),actual.Selected.RevolverCritBonus);
    }
    private static WorldTile ReadTile(JsonElement cell)
    {
        WorldTileFlags flags=0;
        if(B(cell,"active"))flags|=WorldTileFlags.Active;if(B(cell,"actuator"))flags|=WorldTileFlags.Actuator;if(B(cell,"inactive"))flags|=WorldTileFlags.Inactive;
        string[] channels=["red","blue","green","yellow"];
        for(int i=0;i<4;i++)if(B(cell,channels[i]))flags|=(WorldTileFlags)(1<<(i+1));
        return new(){Type=(ushort)I(cell,"type"),FrameX=(short)I(cell,"frameX"),FrameY=(short)I(cell,"frameY"),Flags=flags};
    }
    private static void AssertTile(JsonElement expected, WorldTile actual, bool includeFrames=true)
    {
        WorldTile tile=ReadTile(expected);
        Assert.Equal(tile.Type,actual.Type);
        if(includeFrames){Assert.Equal(tile.FrameX,actual.FrameX);Assert.Equal(tile.FrameY,actual.FrameY);}
        Assert.Equal(tile.Flags,actual.Flags & (WorldTileFlags.Active|WorldTileFlagMasks.Wires|WorldTileFlagMasks.Actuation));
    }
    private static void AssertOriginalOwnerPacket(JsonElement frames, WorldItemSnapshot item)
    {
        byte[][] candidates=frames.EnumerateArray().Select(v=>Convert.FromHexString(v.GetString()!))
            .Where(v=>v[2]==22 && BinaryPrimitives.ReadInt16LittleEndian(v.AsSpan(3))==item.Handle.Slot).ToArray();
        byte[] bytes=Assert.Single(candidates);
        using var stream=new MemoryStream(bytes,writable:false);
        using var reader=new BinaryReader(stream);
        Assert.Equal(bytes.Length,reader.ReadUInt16());Assert.Equal((byte)22,reader.ReadByte());
        Assert.Equal(item.Handle.Slot,reader.ReadInt16());Assert.Equal(item.OwnerPlayerId,reader.ReadByte());
        int advertisedHold=reader.Read7BitEncodedInt();
        Assert.Equal(15,advertisedHold);Assert.Equal(0,item.TimeToKeepReservation);
        Assert.Equal(item.GrabDelayPlayer,reader.ReadByte());Assert.Equal(item.GrabDelayTime,reader.Read7BitEncodedInt());
        Assert.Equal(item.PositionX,reader.ReadSingle());Assert.Equal(item.PositionY,reader.ReadSingle());
        Assert.Equal(stream.Length,stream.Position);
    }
    private static int I(JsonElement value,string name)=>value.GetProperty(name).GetInt32();
    private static bool B(JsonElement value,string name)=>value.GetProperty(name).GetBoolean();
    private static JsonDocument Load()
    {
        using Stream stream=typeof(WorldWiringToolsOriginal1458Tests).Assembly.GetManifestResourceStream("WiringToolsOriginalLinux1458")!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);
        var document=JsonDocument.Parse(gzip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034",document.RootElement.GetProperty("sha256").GetString());
        return document;
    }
}
