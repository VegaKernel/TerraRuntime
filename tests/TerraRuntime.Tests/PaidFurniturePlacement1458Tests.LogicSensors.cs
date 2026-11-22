using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Tests.Larva1458;
using TerraRuntime.World;

namespace TerraRuntime.Tests.PaidFurniture1458;

public sealed partial class PaidFurniturePlacement1458Tests
{
    private const short SensorX = 102;
    private const short SensorY = 102;
    private static readonly short[] SensorItems = [3613, 3614, 3615, 3726, 3727, 3728, 3729];

    public static IEnumerable<object[]> OriginalSensorPlacementRows()
    {
        using var input = typeof(PaidFurniturePlacement1458Tests).Assembly.GetManifestResourceStream("WiringSensorPlacementLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalSensorPlacementRows))]
    public void Paid_sensor_placement_replays_original_client_and_dedicated_packets(string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        int style = row.GetProperty("style").GetInt32();
        string liquid = row.GetProperty("liquid").GetString()!;
        bool day = row.GetProperty("serverDay").GetBoolean();
        bool above = row.GetProperty("serverAbove").GetBoolean();
        short item = SensorItems[style];
        using var f = CreateSensorPlacementScene(item, 1, liquid, day, above);
        var peer = AttachSensorPlacementObserver(f);
        var beforeRandom = f.Random.Clone();
        var client = row.GetProperty("client");
        if (!client.GetProperty("allowed").GetBoolean())
        {
            // The real client declines lava placement and sends no reports.
            // A stale dry prediction cannot authorize the same wet target.
            SendPredictedSensorPlacement(f, item, style, 0);
            AssertSensorPlacementRefused(f, 1);
            Assert.Equal(WorldLiquidKind.Lava, f.Tiles.Get(SensorX, SensorY).LiquidKind);
            Assert.Empty(Drain(peer));
            Assert.True(f.Random.HasSameState(beforeRandom));
            return;
        }

        foreach (var encoded in client.GetProperty("frames").EnumerateArray())
        {
            Post(f, RebaseSensorPacket(encoded));
            Assert.Equal(0, f.Composition.WorldTileAuthority.CommittedToolActions);
            Assert.False(f.Tiles.Get(SensorX, SensorY).IsActive);
            Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
            Assert.Empty(Drain(peer));
        }
        PayForSensor(f, item);
        Assert.Equal(1, f.Composition.WorldTileAuthority.CommittedToolActions);
        var inventory = Inventory(f.Players);
        Assert.True(inventory.TryGet(f.Connection, 0, out var remaining));
        Assert.Equal(2, remaining.Stack);
        Assert.Equal((short)0, inventory.CaptureFundedQuantities(f.Connection)[0]);

        var phases = row.GetProperty("phases");
        var expectedPackets = phases.EnumerateArray().SelectMany(stage => stage.GetProperty("frames").EnumerateArray())
            .Select(RebaseSensorPacket).ToArray();
        Assert.Equal(expectedPackets.Select(Convert.ToHexString), Drain(peer).Select(Convert.ToHexString));
        Assert.Equal(new[] { Convert.ToHexString(expectedPackets[1]) },
            Drain(f.TileOutbound).Where(frame => frame[2] is 20 or 86).Select(Convert.ToHexString));
        AssertSensorState(f, phases[1], liquid);
        Assert.True(f.Random.HasSameState(beforeRandom));

        UpdateSensorCensus(f, above);
        var next = row.GetProperty("next");
        AssertSensorState(f, next, liquid);
        Assert.Equal(next.GetProperty("frames").EnumerateArray().Select(RebaseSensorPacket).Select(Convert.ToHexString),
            Drain(peer).Select(Convert.ToHexString));
        Assert.True(f.Random.HasSameState(beforeRandom));
    }

    [Theory]
    [InlineData("unfunded")]
    [InlineData("wrong-entity")]
    [InlineData("wrong-fluid-frame")]
    [InlineData("wrong-style")]
    [InlineData("stale-read")]
    public void Sensor_reports_cannot_adopt_without_matching_owned_placement_and_stock(string invalid)
    {
        const int style = 3;
        short item = SensorItems[style];
        short funding = invalid == "unfunded" ? (short)0 : (short)1;
        using var f = CreateSensorPlacementScene(item, funding, "water", true, false);
        var peer = AttachSensorPlacementObserver(f);
        var before = f.Random.Clone();
        Post(f, MakePredictedSensorReport(invalid == "wrong-style" ? style + 1 : style,
            invalid == "wrong-fluid-frame" ? (short)18 : (short)0));
        if (invalid == "stale-read")
            f.Tiles.Set(SensorX + 1, SensorY, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Post(f, SensorEntityReport(invalid == "wrong-entity" ? (byte)4 : (byte)2));
        PayForSensor(f, item);
        AssertSensorPlacementRefused(f, funding);
        Assert.Empty(Drain(peer));
        Assert.True(f.Random.HasSameState(before));
    }

    [Fact]
    public void Sensor_player_check_uses_the_frozen_census_and_duplicate_entity_report_is_inert()
    {
        short item = SensorItems[2];
        using var f = CreateSensorPlacementScene(item, 2, "dry", true, false);
        var peer = AttachSensorPlacementObserver(f);
        var outsideSection = AttachSensorPlacementObserver(f, ownsSection: false);
        var player = f.Member.CaptureSnapshot();
        bool liveAbove = VanillaLogicSensor1458.PlayerAbove(SensorX, SensorY,
            player.PositionX, player.PositionY, 20, 42);
        bool frozenAbove = !liveAbove;
        UpdateSensorCensus(f, frozenAbove);
        Post(f, MakePredictedSensorReport(2, 0));
        Post(f, SensorEntityReport(2));
        PayForSensor(f, item);
        var metadata = f.Tiles.RuntimeTileEntities!;
        Assert.Equal(new WorldLogicSensorPayload(3, frozenAbove), Assert.Single(metadata.CaptureSnapshot()).Payload);
        Assert.Equal(frozenAbove ? (short)18 : (short)0, f.Tiles.Get(SensorX, SensorY).FrameX);
        Assert.Equal(new byte[] { 20, 86 }, Drain(peer).Select(packet => packet[2]));
        Assert.Equal(new byte[] { 86 }, Drain(outsideSection).Select(packet => packet[2]));

        Post(f, SensorEntityReport(2));
        PayForSensor(f, item);
        Assert.Equal(1, f.Composition.WorldTileAuthority.CommittedToolActions);
        Assert.Equal(0, Assert.Single(metadata.CaptureSnapshot()).PersistedId);
        Assert.Equal((short)1, Inventory(f.Players).CaptureFundedQuantities(f.Connection)[0]);
        Assert.Empty(Drain(peer));
        Assert.Empty(Drain(outsideSection));
    }

    private static LarvaRuntimeFixture1458 CreateSensorPlacementScene(short item, short funding, string liquid,
        bool day, bool above)
    {
        var f = CreatePaidScene(item, funding, out var shape, dayTime: day);
        Assert.Equal((byte)1, shape.Width);
        Assert.True(shape.IsLogicSensor);
        f.Tiles.Set(SensorX, SensorY + 1, default); // Sensor placement has no floor anchor.
        f.Tiles.Set(SensorX, SensorY, new WorldTile
        {
            LiquidAmount = liquid == "dry" ? (byte)0 : (byte)255,
            LiquidKind = ParseSensorLiquid(liquid)
        });
        UpdateSensorCensus(f, above);
        return f;
    }

    private static void UpdateSensorCensus(LarvaRuntimeFixture1458 f, bool above)
    {
        var member = f.Member;
        var before = (member.PositionX, member.PositionY);
        try
        {
            member.PositionX = SensorX * 16 + (above ? 2 : -150);
            member.PositionY = SensorY * 16 - 140;
            f.Composition.WorldTileAuthority.TickLogicSensors();
        }
        finally
        {
            (member.PositionX, member.PositionY) = before;
        }
    }

    private static TerrariaConnectionOutboundQueue AttachSensorPlacementObserver(LarvaRuntimeFixture1458 f, bool ownsSection = true)
    {
        var peer = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 8192, 2048));
        var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(ownsSection ? 99003 : 99004),
            new(new PlayerSlotId(ownsSection ? (byte)1 : (byte)0), new(1)));
        Assert.True(f.TileReplication.TryRegister(observer.Source, peer, new SensorSectionVisibility(ownsSection)));
        var playing = new PlayerSpawnCommitRequest(observer.Player.Slot, SensorX, SensorY, 0, 0, 0, 0, 0);
        f.TileReplication.PlayerSpawned(observer, in playing);
        _ = Drain(f.TileOutbound);
        _ = Drain(peer);
        return peer;
    }

    private sealed class SensorSectionVisibility(bool ownsSection) : IPlayerSectionVisibility
    {
        public bool OwnsSectionAtTile(int tileX, int tileY) => ownsSection;
    }

    private static void SendPredictedSensorPlacement(LarvaRuntimeFixture1458 f, short item, int style, short frameX)
    {
        Post(f, MakePredictedSensorReport(style, frameX));
        Post(f, SensorEntityReport(2));
        PayForSensor(f, item);
    }

    private static void PayForSensor(LarvaRuntimeFixture1458 f, short item) =>
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 2, 0, item, 0)));

    private static byte[] MakePredictedSensorReport(int style, short frameX)
    {
        // Literal client packet20 from the source capture, with selected frames varied.
        byte[] bytes = [19, 0, 20, 102, 0, 102, 0, 1, 1, 0, 1, 0, 0, 167, 1, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(15), frameX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(17), checked((short)(style * 18)));
        return bytes;
    }

    private static byte[] SensorEntityReport(byte kind) => [8, 0, 87, 102, 0, 102, 0, kind];

    private static byte[] RebaseSensorPacket(JsonElement encoded)
    {
        var bytes = Convert.FromHexString(encoded.GetString()!);
        int offset = bytes[2] == 86 ? 9 : 3;
        Assert.Equal((short)30, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset)));
        Assert.Equal((short)40, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 2)));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), SensorX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 2), SensorY);
        return bytes;
    }

    private static void AssertSensorPlacementRefused(LarvaRuntimeFixture1458 f, short funding)
    {
        Assert.Equal(0, f.Composition.WorldTileAuthority.CommittedToolActions);
        Assert.False(f.Tiles.Get(SensorX, SensorY).IsActive);
        Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
        Assert.Equal(funding, Inventory(f.Players).CaptureFundedQuantities(f.Connection)[0]);
    }

    private static void AssertSensorState(LarvaRuntimeFixture1458 f, JsonElement source, string liquid)
    {
        var tile = f.Tiles.Get(SensorX, SensorY);
        var point = source.GetProperty("point");
        Assert.True(tile.IsActive);
        Assert.Equal((ushort)423, tile.Type);
        Assert.Equal(point.GetProperty("frameX").GetInt16(), tile.FrameX);
        Assert.Equal(point.GetProperty("frameY").GetInt16(), tile.FrameY);
        Assert.Equal(liquid == "dry" ? (byte)0 : (byte)255, tile.LiquidAmount);
        Assert.Equal(ParseSensorLiquid(liquid), tile.LiquidKind);
        var entity = Assert.Single(f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
        var sourceEntity = Assert.Single(source.GetProperty("entities").EnumerateArray());
        Assert.Equal(sourceEntity.GetProperty("id").GetInt32(), entity.PersistedId);
        Assert.Equal(WorldTileEntityKind.LogicSensor, entity.Kind);
        Assert.Equal((SensorX, SensorY), (entity.X, entity.Y));
        Assert.Equal(new WorldLogicSensorPayload(sourceEntity.GetProperty("check").GetByte(),
            sourceEntity.GetProperty("on").GetBoolean()), entity.Payload);
    }

    private static WorldLiquidKind ParseSensorLiquid(string liquid) => liquid switch
    {
        "lava" => WorldLiquidKind.Lava,
        "honey" => WorldLiquidKind.Honey,
        "shimmer" => WorldLiquidKind.Shimmer,
        _ => WorldLiquidKind.Water
    };
}
