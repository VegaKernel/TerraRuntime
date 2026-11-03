using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationUndergroundFinishOwned1458Tests
{
    private static JsonDocument Read() { using var s = typeof(GenerationUndergroundFinishOwned1458Tests).Assembly.GetManifestResourceStream("GenerationUndergroundFinishOwned1458")!; using var g = new GZipStream(s, CompressionMode.Decompress); return JsonDocument.Parse(g); }
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); for (int i = 0; i < d.RootElement.GetArrayLength(); i++) yield return new object[] { i }; }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Underground_gem_and_larva_placement_matches_actual_original_entire_grid_return_and_full_random(int id)
    {
        using var data = Read(); var row = data.RootElement[id]; int seed = row.GetProperty("seed").GetInt32(), profile = row.GetProperty("profile").GetInt32(); string family = row.GetProperty("family").GetString()!; var w = new Workspace(96, 600); w.InitializeVanillaGenerationSolidity(); foreach (int type in new[] { 162, 229, 232, 481, 482, 483, 659 }) w.SetVanillaGenerationSolid(new(type), false); var random = new RandomAdapter(seed); bool? result = null;
        if (family == "gems")
        {
            int dir = profile / 80, shape = profile / 8 % 10; ushort support = new ushort[] { 1, 19, 137, 225, 659, 484, 380, 226 }[profile % 8]; int dx = dir == 1 ? -1 : dir == 2 ? 1 : 0, dy = dir == 0 ? 1 : dir == 3 ? -1 : 0; ref var anchor = ref At(48 + dx, 300 + dy); anchor.Flags = WorldTileFlags.Active; anchor.Type = support; anchor.TrySetSourceShape(shape >= 5, (byte)(shape % 5)); ref var cell = ref At(48, 300); cell.Type = (ushort)(profile % 2 == 0 ? 178 : 0); cell.TileColor = 7; cell.WallColor = 9; cell.Wall = 4; cell.Flags = WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.Actuator | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightWall; cell.TrySetSourceShape(true, 3); cell.LiquidAmount = (byte)(profile % 3 == 0 ? 127 : 0); cell.LiquidKind = (WorldLiquidKind)(profile % 4); var framing = new GenerationTileFraming1458(w.TileStore, random, w, useWorldGenerationSolidity: true); result = framing.PlaceGemInEmptyCell(48, 300, profile % 7);
        }
        else if (family == "walls")
        {
            for (int x = 0; x < 96; x++) for (int y = 0; y < 225; y++) { ref var cell = ref At(x, y); cell.Wall = (ushort)((x + y) % 4 == 0 ? 2 : (x + y) % 4 == 1 ? 59 : 86); cell.WallColor = 8; cell.Flags = WorldTileFlags.WireRed; }
            if (profile != 0) { int left = profile == 1 ? 5 : 20, right = profile == 2 ? 91 : profile == 3 ? 20 : 75; At(left, profile >= 8 ? 220 : 200).Type = 60; if (profile != 4) At(left, profile >= 8 ? 220 : 200).Flags |= WorldTileFlags.Active; At(right, profile >= 8 ? 220 : 200).Type = 60; if (profile != 5) At(right, profile >= 8 ? 220 : 200).Flags |= WorldTileFlags.Active; }
            Assert.True(w.TrySetLayers(profile >= 8 ? 200.5 : 200, 300)); w.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1458), 4200, false, VanillaSpecialWorldSeed1458.None)); new UndergroundFinishPass1458(UndergroundFinishStage1458.MudsWallsInJungle, new UndergroundFinishState1458()).Execute(new Context(new(Provider1458.GeneratorId, "ActualSourceFixture", (ulong)seed, 96, 600), w, random)); Assert.Equal(row.GetProperty("jungleMin").GetInt32(), w.VanillaSurfaceJungleMinX); Assert.Equal(row.GetProperty("jungleMax").GetInt32(), w.VanillaSurfaceJungleMaxX);
        }
        else
        {
            var anchors = profile == 23 ? Array.Empty<WorldGenerationPoint>() : profile >= 20 ? new[] { new WorldGenerationPoint(48, 300), new(profile == 20 ? 48 : profile == 21 ? 49 : 65, 300) } : new[] { new WorldGenerationPoint(48, 300) }; w.SetVanillaHiveLarvaAnchors(anchors);
            for (int x = 45; x <= 68; x++) for (int y = 296; y <= 303; y++) { ref var cell = ref At(x, y); cell.Type = (ushort)(profile % 2 == 0 ? 178 : 1); cell.Flags = WorldTileFlags.WireRed | WorldTileFlags.WireGreen | WorldTileFlags.Actuator | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightBlock; if ((x + y) % 3 == 0) cell.Flags |= WorldTileFlags.Active; if (profile % 4 == 0) cell.Flags |= WorldTileFlags.Inactive; cell.FrameX = 90; cell.FrameY = 36; cell.TileColor = 6; cell.Wall = 86; cell.WallColor = 8; cell.TrySetSourceShape(profile % 2 == 0, (byte)(profile % 5)); cell.LiquidAmount = (byte)(profile % 3 == 0 ? 123 : 0); cell.LiquidKind = (WorldLiquidKind)(profile % 4); }
            if (profile >= 16 && profile <= 19) { At(47, 301).Flags |= WorldTileFlags.Active; At(47, 301).Type = profile == 16 ? (ushort)19 : (ushort)1; }
            Assert.True(w.TrySetLayers(200, 300)); w.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1458), 4200, false, VanillaSpecialWorldSeed1458.None)); new UndergroundFinishPass1458(UndergroundFinishStage1458.Larva, new UndergroundFinishState1458()).Execute(new Context(new(Provider1458.GeneratorId, "ActualSourceFixture", (ulong)seed, 96, 600), w, random));
        }
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State()); Assert.Equal(row.GetProperty("next").GetInt32(), random.Next()); if (row.GetProperty("result").ValueKind != JsonValueKind.Null) Assert.Equal(row.GetProperty("result").GetBoolean(), result); Assert.Equal(row.GetProperty("phase").EnumerateArray().Select(x => x.GetBoolean()).ToArray(), Enumerable.Range(0, 754).Select(i => w.IsVanillaGenerationSolid(new(i))).ToArray());
        ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private sealed class Context(WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext { public WorldGenerationRequest Request => request; public IWorldGenerationWorkspace Workspace => workspace; public IWorldGenerationMetadataWorkspace Metadata => workspace; public IWorldGenerationRandom Random => throw new NotSupportedException(); public IWorldGenerationVanillaRandom VanillaRandom => random; public CancellationToken CancellationToken => CancellationToken.None; public void ReportProgress(double f, string? m = null) { } }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string State() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
