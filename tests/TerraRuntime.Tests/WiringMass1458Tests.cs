using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WiringMass1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using var resource = typeof(WiringMass1458Tests).Assembly.GetManifestResourceStream("WiringMassLinux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Detached_L_path_partial_exhaustion_order_and_returned_resources_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100));
        foreach (var cell in row.GetProperty("before").EnumerateArray())
        {
            var tile = WiringLogicGate1458Tests.Decode(cell.GetProperty("tile"));
            tile.Wall = cell.GetProperty("tile").GetProperty("wall").GetUInt16();
            tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), in tile);
        }
        if (row.GetProperty("scene").GetInt32() == 2)
            for (int y = 35; y < 46; y++) { var tile = tiles.Get(1, y); tile.Wall = 350; tiles.Set(1, y, in tile); }
        int stock = row.GetProperty("stock").GetInt32();
        Assert.True(WorldMassWiring1458.TryPlan(tiles, row.GetProperty("sx").GetInt32(), row.GetProperty("sy").GetInt32(),
            row.GetProperty("ex").GetInt32(), row.GetProperty("ey").GetInt32(), row.GetProperty("direction").GetInt32() == 1,
            row.GetProperty("mode").GetByte(), stock, stock, (_, _, tile) => tile.Wall != 350, out var planned));
        var plan = Assert.IsType<WorldMassWiringPlan1458>(planned);
        var actualFrames = new List<string>();
        foreach (var edit in plan.Edits)
        {
            Assert.Equal(edit.Before, tiles.Get(edit.X, edit.Y));
            tiles.Set(edit.X, edit.Y, edit.After);
            Assert.Equal(TerrariaTileManipulationEncodeResult.Encoded, TerrariaTileManipulationCodec.TryEncode(new(edit.Action, (short)edit.X, (short)edit.Y, 0, 0), out var bytes));
            actualFrames.Add(Convert.ToHexString(bytes));
        }
        var sourceFrames = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();
        Assert.Equal(sourceFrames.Where(x => x[2] == 17).Select(Convert.ToHexString), actualFrames);
        var payments = sourceFrames.Where(x => x[2] == 110).ToArray();
        Assert.Equal(2, payments.Length);
        Assert.Equal(530, BinaryPrimitives.ReadInt16LittleEndian(payments[0].AsSpan(3)));
        Assert.Equal(849, BinaryPrimitives.ReadInt16LittleEndian(payments[1].AsSpan(3)));
        Assert.Equal(plan.WiresConsumed, BinaryPrimitives.ReadInt16LittleEndian(payments[0].AsSpan(5)));
        Assert.Equal(plan.ActuatorsConsumed, BinaryPrimitives.ReadInt16LittleEndian(payments[1].AsSpan(5)));
        Assert.All(payments, frame => Assert.Equal((byte)1, frame[7]));
        var expected = row.GetProperty("after").EnumerateArray().ToDictionary(c => (c.GetProperty("x").GetInt32(), c.GetProperty("y").GetInt32()), c => WiringLogicGate1458Tests.Decode(c.GetProperty("tile")));
        const WorldTileFlags mask = WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow | WorldTileFlags.Actuator;
        for (int x = 0; x < 80; x++) for (int y = 0; y < 100; y++)
            Assert.Equal(expected.GetValueOrDefault((x, y)).Flags & mask, tiles.Get(x, y).Flags & mask);
        var drops = row.GetProperty("drops").EnumerateArray().ToArray();
        Assert.Equal(plan.WireDrops, drops.Where(d => d.GetProperty("type").GetInt32() == 530).Sum(d => d.GetProperty("stack").GetInt32()));
        Assert.Equal(plan.ActuatorDrops, drops.Where(d => d.GetProperty("type").GetInt32() == 849).Sum(d => d.GetProperty("stack").GetInt32()));
    }

    [Theory]
    [InlineData(int.MinValue, int.MaxValue, 0)]
    [InlineData(0, 16_384, 0)]
    [InlineData(0, 1, 64)]
    public void Hostile_mode_and_path_refuse_without_reading_or_mutating_world(int start, int end, byte mode)
    {
        var tiles = new WorldTileStore(new(80, 100));
        Assert.False(WorldMassWiring1458.TryPlan(tiles, start, 40, end, 40, true, mode, 100, 100,
            (_, _, _) => throw new InvalidOperationException("Invalid paths must not invoke permission reads."), out var plan));
        Assert.Null(plan);
    }
}
