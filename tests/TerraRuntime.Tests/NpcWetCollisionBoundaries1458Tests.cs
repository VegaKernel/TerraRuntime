using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcWetCollisionBoundaries1458Tests
{
    [Fact]
    public void Original_odd_body_liquid_and_inherited_slope_boundaries_use_integer_probe_origins()
    {
        using var stream = typeof(NpcWetCollisionBoundaries1458Tests).Assembly.GetManifestResourceStream("NpcWetCollisionBoundaries1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        int rows = 0, births = 0;
        foreach (var row in json.RootElement.EnumerateArray())
        {
            var tiles = new WorldTileStore(new(100, 80));
            foreach (var source in row.GetProperty("tiles").EnumerateArray())
            {
                int slope = source.GetProperty("slope").GetInt32();
                tiles.Set(source.GetProperty("x").GetInt32(), source.GetProperty("y").GetInt32(), new WorldTile {
                    Type = 1, LiquidAmount = source.GetProperty("amount").GetByte(),
                    LiquidKind = (WorldLiquidKind)source.GetProperty("kind").GetByte(),
                    Shape = slope == 0 ? (byte)0 : (byte)(slope + 1),
                    Flags = (source.GetProperty("active").GetBoolean() ? WorldTileFlags.Active : WorldTileFlags.None) |
                        (source.GetProperty("actuated").GetBoolean() ? WorldTileFlags.Inactive : WorldTileFlags.None) });
            }
            int width = row.GetProperty("width").GetInt32(), height = row.GetProperty("height").GetInt32();
            float x = row.GetProperty("x").GetSingle(), y = row.GetProperty("y").GetSingle();
            bool wet = VanillaWorldCollision.TryGetWetContact(tiles, x, y, width, height, out var kind);
            var actual = (wet, Honey: wet && kind == WorldLiquidKind.Honey, Shimmer: wet && kind == WorldLiquidKind.Shimmer,
                Lava: VanillaWorldCollision.LavaCollision(tiles, x, y, width, height));
            var expected = (row.GetProperty("wet").GetBoolean(), row.GetProperty("honey").GetBoolean(),
                row.GetProperty("shimmer").GetBoolean(), row.GetProperty("lava").GetBoolean());
            Assert.True(expected == actual, $"case{row.GetProperty("case")}: expected{expected}, actual{actual}");
            // This API deliberately aggregates kinds; its Wet geometry still shares WetCollision.
            Assert.Equal(expected.Item1, VanillaWorldCollision.GetLiquidContacts(tiles, x, y, width, height).Wet);
            if (width == 19 && height == 44)
            {
                var npcs = new RuntimeNpcStore();
                _ = new ServerRuntimeState(npcs: npcs, worldTiles: tiles);
                Assert.True(npcs.TrySpawnVanilla(new(3, -27, x, y, 0, 0, 255, default, NpcSimulationState.Initial), out var born));
                Assert.Equal(expected.Item1, born.Simulation.Wet);
                births++;
            }
            rows++;
        }
        Assert.Equal(5824, rows);
        Assert.True(births > 0);
    }
}
