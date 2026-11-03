using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationIslandHouse1458Tests
{
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); foreach (var r in d.RootElement.GetProperty("rows").EnumerateArray()) yield return new object[] { r.GetProperty("id").GetInt32() }; }
    private static JsonDocument Read() { using var s = typeof(GenerationIslandHouse1458Tests).Assembly.GetManifestResourceStream("IslandHouse1458")!; using var z = new GZipStream(s, CompressionMode.Decompress); return JsonDocument.Parse(z); }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Island_house_matches_original_grid_full_chests_counter_and_physical_random(int id)
    {
        using var d = Read(); var row = d.RootElement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id); int seed = row.GetProperty("seed").GetInt32(), mode = row.GetProperty("mode").GetInt32(); const int W = 600, H = 500, X = 300; var w = new Workspace(W, H); w.InitializeVanillaGenerationSolidity(); int type = 0; foreach (var b in d.RootElement.GetProperty("phase").EnumerateArray()) w.SetVanillaGenerationSolid(new TileTypeId(type++), b.GetBoolean());
        w.SetVanillaBuriedChestContext(new BuriedChestContext1458 { Height = H, WorldSurface = 140, RockLayer = 200, LavaLine = 300, CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19, TungstenIsSilverTier = false, DesertHiveLow = 0, DesertHiveHigh = 0, HellChestItem = [220, 274, 275] });
        for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) { var tile = new WorldTile(); if (mode == 10) { tile.TileColor = 7; tile.WallColor = 5; tile.TrySetSourceShape(true, 3); tile.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall | WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow; } w.TileStore.Set(x, y, tile); }
        int Y = mode == 7 ? 200 : 100; w.VanillaSkyIslandHouseCount = mode == 8 ? 3 : mode == 9 ? 4 : 0;
        if (mode is 1 or 2) { for (int x = X - 60; x <= X + 60; x++) { for (int y = 110; y <= 111; y++) At(x, y) = new WorldTile { Type = 1, Wall = 73, Flags = WorldTileFlags.Active }; if (mode == 2) At(x, 110).TrySetSourceShape(false, 1); } }
        if (mode is 3 or 4 or 5 or 6) At(X + (mode == 6 ? 31 : 30), Y).Wall = (ushort)(mode switch { 3 => 87, 4 => 83, 5 => 7, _ => 87 }); if (mode == 11) At(X, Y).Wall = 187;
        if (mode is 12 or 13) { int left = mode == 12 ? 310 : 299, top = 99; for (int xx = 0; xx < 2; xx++) for (int yy = 0; yy < 2; yy++) At(left + xx, top + yy) = new WorldTile { Type = 21, Flags = WorldTileFlags.Active, FrameX = (short)(13 * 36 + xx * 18), FrameY = (short)(yy * 18) }; Assert.True(w.TryAddGeneratedChest(left, top, string.Empty, [])); }
        var random = new FixtureRandom(seed); LateStructurePass1458.PlaceIslandHouseForTesting(w, random, X, Y, 0, 140);
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(row.GetProperty("chests").GetString(), ChestHash(w)); Assert.Equal(row.GetProperty("count").GetInt32(), w.VanillaSkyIslandHouseCount); Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(v => v.GetInt32())), random.Physical()); Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private static string ChestHash(Workspace w) { using var s = new MemoryStream(); using var bw = new BinaryWriter(s); var chests = w.CaptureGeneratedChests(); bw.Write(chests.Length); foreach (var c in chests) { bw.Write((int)c.SlotId); bw.Write(c.X); bw.Write(c.Y); byte[] name = Encoding.UTF8.GetBytes(c.Name); bw.Write(name.Length); bw.Write(name); bw.Write(c.Items.Length); foreach (var i in c.Items) { bw.Write(i.ItemType); bw.Write(i.Stack); bw.Write((int)i.Prefix); } } return Convert.ToHexString(SHA256.HashData(s.ToArray())); }
    private sealed class FixtureRandom(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string Physical() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
