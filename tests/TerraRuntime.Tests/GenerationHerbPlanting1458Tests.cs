using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Core;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationHerbPlanting1458Tests
{
    private static JsonDocument Read() { using var stream = typeof(GenerationHerbPlanting1458Tests).Assembly.GetManifestResourceStream("GenerationHerbPlanting1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); for (int i = 0; i < d.RootElement.GetArrayLength(); i++) yield return new object[] { i }; }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Herb_placement_suitability_and_offers_match_original_entire_grid_and_full_random(int id) { using var d = Read(); var row = d.RootElement[id]; var w = Create(row); var random = new RandomAdapter(row.GetProperty("seed").GetInt32()); var herb = new GenerationHerbPlanting1458(w.TileStore, random, 80, 120, 380); string stage = row.GetProperty("stage").GetString()!; if (stage == "raw") Assert.Equal(row.GetProperty("returned").GetBoolean(), herb.Place(row.GetProperty("location").GetInt32(), 120, row.GetProperty("style").GetInt32())); else if (stage == "suitable") herb.PlaceSuitable(row.GetProperty("location").GetInt32(), 121); else herb.Offer(); Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State()); Assert.Equal(row.GetProperty("next").GetInt32(), random.Next()); }
    private static Workspace Create(JsonElement data)
    {
        string stage = data.GetProperty("stage").GetString()!; int seed = data.GetProperty("seed").GetInt32(), ground = data.GetProperty("ground").GetInt32(), mode = data.GetProperty("mode").GetInt32(), location = data.GetProperty("location").GetInt32(), width = stage == "raw" ? 120 : 800, height = stage == "offer" ? 240 : 160; var w = new Workspace(width, height);
        if (stage != "offer")
        {
            At(location, 121) = new() { Flags = WorldTileFlags.Active, Type = (ushort)ground };
            ref var floor = ref At(location, 121); ref var cell = ref At(location, 120);
            if (stage == "raw")
            {
                if (mode == 1) floor.TrySetSourceShape(true, 0); if (mode == 2) floor.TrySetSourceShape(false, 3); if (mode == 3) floor.Flags |= WorldTileFlags.Inactive; if (mode == 4) { cell.Flags |= WorldTileFlags.Active; cell.Type = 1; }
                if (mode == 5) cell.LiquidAmount = 128; if (mode == 6) { cell.LiquidAmount = 128; cell.LiquidKind = WorldLiquidKind.Lava; }
                if (mode == 7) { cell.Type = 54; cell.FrameX = 36; cell.FrameY = 72; cell.TrySetSourceShape(true, 3); cell.TileColor = 9; cell.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.WireYellow; floor.TileColor = 7; floor.Flags |= WorldTileFlags.FullbrightBlock; }
            }
            else { if (mode == 1) cell.LiquidAmount = 128; if (mode == 2) floor.TrySetSourceShape(true, 0); if (mode == 3) { cell.Type = 54; cell.FrameX = 36; cell.FrameY = 72; cell.TrySetSourceShape(true, 3); cell.TileColor = 9; cell.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.WireYellow; floor.TileColor = 7; floor.Flags |= WorldTileFlags.FullbrightBlock; } if (mode == 4) floor.Flags |= WorldTileFlags.Inactive; if (mode == 5) { cell.Wall = 2; cell.LiquidKind = WorldLiquidKind.Lava; floor.Flags |= WorldTileFlags.Actuator; } }
        }
        else
        {
            ushort material = (ushort)(mode == 12 ? 147 : mode == 13 ? 59 : mode == 14 ? 633 : mode == 15 ? 53 : mode == 16 ? 109 : mode == 17 ? 1 : mode == 18 ? 477 : 2);
            for (int column = 0; column < width; column++) foreach (int row in new[] { 100, 200 }) { ref var floor = ref At(column, row); floor.Flags = WorldTileFlags.Active; floor.Type = material; if (mode == 1 || mode == 19) floor.Flags |= WorldTileFlags.Inactive; if (mode == 2) floor.TrySetSourceShape(true, 0); if (mode == 3) floor.TrySetSourceShape(false, 1); ref var cell = ref At(column, row - 1); if (mode == 4) cell.LiquidAmount = 128; if (mode == 5) { cell.LiquidAmount = 128; cell.LiquidKind = WorldLiquidKind.Lava; } }
            int first = new VanillaUnifiedRandom1458(seed).Next(20, width - 20);
            if (mode >= 6 && mode <= 11) foreach (int row in new[] { 98, 198 }) for (int dx = -2; dx <= (mode == 7 ? 1 : 2); dx++) { ref var herb = ref At(first + dx, row); herb.Type = (ushort)(mode == 9 ? 83 : mode == 10 ? 84 : mode == 11 ? 81 : 82); herb.FrameX = 72; herb.FrameY = 36; if (mode == 8) herb.Flags |= WorldTileFlags.Active; }
        }
        return w; ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string State() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
