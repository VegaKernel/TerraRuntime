using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationSurfaceFinishOwners1458Tests
{
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); foreach (var r in d.RootElement.GetProperty("rows").EnumerateArray()) yield return new object[] { r.GetProperty("id").GetInt32() }; }
    private static JsonDocument Read() { using var s = typeof(GenerationSurfaceFinishOwners1458Tests).Assembly.GetManifestResourceStream("SurfaceFinishOwners1458")!; using var z = new GZipStream(s, CompressionMode.Decompress); return JsonDocument.Parse(z); }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Surface_owners_match_original_grid_full_physical_random_and_result(int id)
    {
        using var d = Read(); var r = d.RootElement.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id); string stage = r.GetProperty("stage").GetString()!; int seed = r.GetProperty("seed").GetInt32(), floor = r.GetProperty("floor").GetInt32(), mode = r.GetProperty("mode").GetInt32(); var w = new Workspace(120, 100); w.InitializeVanillaGenerationSolidity(); int type = 0; foreach (var b in d.RootElement.GetProperty("phase").EnumerateArray()) w.SetVanillaGenerationSolid(new TileTypeId(type++), b.GetBoolean()); var rng = new FixtureRandom(seed); bool? returned = null;
        if (stage == "quick")
        {
            Floor(1); At(60, 41).Type = (ushort)floor; At(60, 41).Flags = r.GetProperty("floorActive").GetBoolean() ? WorldTileFlags.Active : 0; At(60, 39) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; ref WorldTile t = ref At(60, 40); t.Flags |= WorldTileFlags.Active | WorldTileFlags.WireRed; t.Type = (ushort)r.GetProperty("target").GetInt32(); t.TrySetSourceShape(r.GetProperty("half").GetBoolean(), (byte)r.GetProperty("slope").GetInt32()); t.TileColor = 7;
            // Invoke the same production caller with the direct original fixture's explicit scalar context.
            // The separate continuous macro qualifies normal State.EnsureInitialized and admitted world dimensions.
            var state = new SurfaceFinishState1458(); typeof(SurfaceFinishState1458).GetProperty("WorldSurface")!.SetValue(state, 30d); typeof(SurfaceFinishState1458).GetProperty("RockLayer")!.SetValue(state, 60d); var pass = new SurfaceFinishPass1458(SurfaceFinishStage1458.QuickCleanup, state); typeof(SurfaceFinishPass1458).GetMethod("ApplyQuickCleanup", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pass, [new Context(w, rng, seed), w]);
        }
        else if (stage.StartsWith("pot", StringComparison.Ordinal))
        {
            Floor(2); if (stage == "pot-supported") { At(59, 41) = default; At(61, 41) = new WorldTile { Type = (ushort)floor, Flags = WorldTileFlags.Active }; }
            Prepare(60, 61); if (mode == 8) Decorate(60, 61); if (mode == 9) At(60, 39) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; var pass = new PotScatterPass1458(w.TileStore, rng, 30, 30, 30, 60, DungeonGenerationCatalog1458.BeachDistance, CancellationToken.None, w.IsVanillaGenerationSolid); returned = (bool)typeof(PotScatterPass1458).GetMethod("PlacePot", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(pass, [60, 40, r.GetProperty("target").GetInt32()])!;
        }
        else
        {
            Floor(3); if (floor == 138) { for (int x = 59; x <= 62; x++) { for (int y = 41; y <= 42; y++) At(x, y) = new WorldTile { Type = 138, Flags = WorldTileFlags.Active, FrameX = (short)((x - 59) % 2 * 18), FrameY = (short)((y - 41) * 18) }; At(x, 43) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; } }
            Prepare(59, 61); if (mode == 8) Decorate(59, 61); if (mode == 9) At(59, 39) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; if (mode == 10) { ref WorldTile t = ref At(60, 40); t.Flags |= WorldTileFlags.Active; t.Type = 75; t.FrameX = t.FrameY = -1; t.TrySetSourceShape(true, 3); t.TileColor = 6; }
            if (mode == 11) for (int x = 59; x <= 61; x++) for (int y = 39; y <= 40; y++) At(x, y) = new WorldTile { Type = 77, Flags = WorldTileFlags.Active, FrameX = (short)((x - 59) * 18), FrameY = (short)((y - 39) * 18) };
            if (mode >= 12) { ref WorldTile t = ref At(60, 40); t.Flags |= WorldTileFlags.Active; t.Type = (ushort)(mode switch { 12 => 54, 13 => 328, 14 => 459, _ => 748 }); t.FrameX = t.FrameY = -1; t.TrySetSourceShape(true, 3); t.Wall = 21; }
            if (r.GetProperty("wrapper").GetBoolean()) returned = HellforgePlacement1458.TryPlaceTile(w.TileStore, rng, 60, 40, w); else HellforgePlacement1458.TryPlace(w.TileStore, 60, 40, w);
        }
        Assert.Equal(r.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(r.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', r.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), rng.Physical()); if (r.GetProperty("returned").ValueKind != JsonValueKind.Null) Assert.Equal(r.GetProperty("returned").GetBoolean(), returned); Assert.Equal(r.GetProperty("next").GetInt32(), rng.Next());
        ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
        void Floor(int width) { for (int x = 60 - width / 2; x < 60 - width / 2 + width; x++) At(x, 41) = new WorldTile { Type = (ushort)floor, Flags = WorldTileFlags.Active }; }
        void Prepare(int left, int right) { for (int x = left; x <= right; x++) { ref WorldTile t = ref At(x, 41); bool half = mode is 1 or 3 or 5; byte slope = mode is 2 or 3 ? (byte)1 : mode is 4 or 5 ? (byte)3 : (byte)0; t.TrySetSourceShape(half, slope); if (mode == 6) t.Flags &= ~WorldTileFlags.Active; if (mode == 7) t.Flags |= WorldTileFlags.Inactive; } }
        void Decorate(int left, int right) { for (int x = left; x <= right; x++) for (int y = 39; y <= 40; y++) { ref WorldTile t = ref At(x, y); t.TileColor = 6; t.Wall = 14; t.WallColor = 7; t.TrySetSourceShape(true, 3); t.LiquidAmount = 90; t.LiquidKind = WorldLiquidKind.Lava; t.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.WireRed; } }
    }
    private sealed class Context(Workspace w, IWorldGenerationVanillaRandom rng, int seed) : IWorldGenerationContext { public WorldGenerationRequest Request => new(Provider1458.GeneratorId, "DirectSurface", (ulong)seed, 120, 100); public IWorldGenerationWorkspace Workspace => w; public IWorldGenerationMetadataWorkspace Metadata => w; public IWorldGenerationRandom Random => throw new NotSupportedException(); public IWorldGenerationVanillaRandom VanillaRandom => rng; public CancellationToken CancellationToken => CancellationToken.None; public void ReportProgress(double fraction, string? message = null) { } }
    private sealed class FixtureRandom(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string Physical() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
