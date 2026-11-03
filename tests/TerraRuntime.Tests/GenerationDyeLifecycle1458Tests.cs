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

public sealed class GenerationDyeLifecycle1458Tests
{
    private static JsonDocument Read() { using var stream = typeof(GenerationDyeLifecycle1458Tests).Assembly.GetManifestResourceStream("GenerationDyeLifecycle1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); for (int i = 0; i < d.RootElement.GetArrayLength(); i++) yield return new object[] { i }; }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Dye_callers_maturity_tulips_and_eggs_match_original_grid_return_and_full_random(int id) { using var d = Read(); var row = d.RootElement[id]; string stage = row.GetProperty("stage").GetString()!; int seed = row.GetProperty("seed").GetInt32(); var w = Create(row); var random = new RandomAdapter(seed); var framing = new GenerationTileFraming1458(w.TileStore, random, w, useWorldGenerationSolidity: true); var plants = new GenerationDyePlanting1458(w, random, 200, 250, framing); if (stage == "caller") plants.PlantDye(200, row.GetProperty("band").GetInt32() == 0 ? 199 : 200); else if (stage == "mature") plants.MatureHerbs(); else if (stage == "tulip" || stage == "egg") Assert.Equal(row.GetProperty("returned").GetBoolean(), plants.TryNaturalPlant(200, 200, stage == "egg")); else Assert.Equal(row.GetProperty("returned").GetBoolean(), stage == "tile656" ? framing.TryPlaceGlowTulip(200, 200) : framing.TryPlaceChilletEgg(200, 200)); Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State()); Assert.Equal(row.GetProperty("next").GetInt32(), random.Next()); }
    private static Workspace Create(JsonElement data)
    {
        const int W = 400, H = 500; string stage = data.GetProperty("stage").GetString()!; int seed = data.GetProperty("seed").GetInt32(), ground = data.GetProperty("ground").GetInt32(), band = data.GetProperty("band").GetInt32(), mode = data.GetProperty("mode").GetInt32(), x = 200, y = band == 0 ? 199 : 200; var w = new Workspace(W, H); w.InitializeVanillaGenerationSolidity(); w.SetVanillaGenerationSolid(new(0), !(stage == "mature" && mode == 9)); w.SetVanillaGenerationSolid(new(59), !(stage == "mature" && mode == 11)); w.SetVanillaGenerationSolid(new(225), !(stage.StartsWith("tile", StringComparison.Ordinal) ? mode == 19 : mode == 11)); w.SetVanillaGenerationSolid(new(659), stage.StartsWith("tile", StringComparison.Ordinal) && mode == 20);
        if (stage == "caller")
        {
            At(x, y) = new() { Flags = WorldTileFlags.Active, Type = (ushort)ground }; if (ground == 80) At(x, y + 1) = new() { Flags = WorldTileFlags.Active, Type = 53 };
            ref var target = ref At(x, y - 1); if (mode == 1) target.Wall = 2; if (mode >= 2 && mode <= 5) { target.LiquidAmount = 255; target.LiquidKind = (WorldLiquidKind)(mode - 2); At(x, y - 2).LiquidAmount = 255; At(x, y - 2).LiquidKind = (WorldLiquidKind)(mode - 2); }
            if (mode == 6 || mode == 7) { target.Flags |= WorldTileFlags.Active; target.Type = (ushort)(mode == 6 ? 3 : 697); }
            if (mode == 8) At(x, y).TrySetSourceShape(true, 0); if (mode == 9) At(x, y).Flags |= WorldTileFlags.Inactive;
            if (mode >= 10 && mode <= 13) { ref var marker = ref At(x + (mode == 11 ? 90 : 89), y); marker.Type = 227; marker.FrameX = (short)(mode == 12 ? 272 : 271); if (mode != 13) marker.Flags |= WorldTileFlags.Active; }
            if (mode == 14) { At(x, y - 1) = new() { Flags = WorldTileFlags.Active, Type = 1 }; At(x, y + 1).Flags &= ~WorldTileFlags.Active; }
            if (mode == 15) At(x + 5, y + 15) = new() { Flags = WorldTileFlags.Active, Type = 112 };
        }
        else if (stage == "mature")
        {
            for (int column = 60; column < 63; column++) { ref var ghost = ref At(column, 40); ghost.Type = (ushort)(mode == 2 ? 83 : mode == 3 ? 84 : 82); ghost.FrameX = (short)(column == 60 ? 36 : column == 61 ? 108 : 0); if (mode == 1) ghost.Flags |= WorldTileFlags.Active; }
            if (mode >= 5 && mode <= 14) for (int column = 55; column <= 65; column++) { ref var soil = ref At(column, 301); soil.Type = (ushort)(mode == 10 || mode == 11 ? 59 : 0); if (mode != 14) soil.Flags |= WorldTileFlags.Active; if (mode == 6) soil.TrySetSourceShape(true, 0); if (mode == 7) soil.TrySetSourceShape(false, 1); if (mode == 8) soil.Flags |= WorldTileFlags.Inactive; if (mode == 12) At(column, 300).LiquidAmount = 128; if (mode == 13) At(column, 300) = new() { Flags = WorldTileFlags.Active, Type = 82, FrameX = 36 }; }
            if (mode == 15) for (int column = 55; column <= 65; column++) for (int row = 35; row <= 45; row++) { ref var cell = ref At(column, row); cell.TileColor = 9; cell.WallColor = 5; cell.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall; }
        }
        else if (stage == "tulip" || stage == "egg")
        {
            if (mode != 1) for (int column = 180; column <= 220; column++) for (int row = 180; row <= 220; row += 4) { ref var soil = ref At(column, row); soil.Flags = WorldTileFlags.Active; soil.Type = (ushort)(mode == 11 || mode == 12 ? 225 : 1); if (mode == 2) soil.TrySetSourceShape(true, 0); if (mode == 3) soil.TrySetSourceShape(false, 1); if (mode == 4) soil.TrySetSourceShape(false, 3); if (mode == 5) soil.Flags |= WorldTileFlags.Inactive; }
            if (mode == 6 || mode == 7 || mode == 14) for (int column = 180; column <= 220; column++) for (int row = 180; row <= 220; row++) if (!At(column, row).IsActive) { if (mode == 14) At(column, row).Flags |= WorldTileFlags.Inactive; else { At(column, row).LiquidAmount = 128; At(column, row).LiquidKind = mode == 6 ? WorldLiquidKind.Water : WorldLiquidKind.Lava; } }
            if (mode == 8 || mode == 9 || mode == 10 || mode == 15) { var label = new VanillaUnifiedRandom1458(seed); int cx = label.Next(190, 210), cy = label.Next(190, 210), distance = stage == "egg" ? 20 : 120; ref var marker = ref At(cx + (mode == 10 ? 0 : distance + (mode == 9 ? 1 : 0)), cy); marker.Type = (ushort)(mode == 15 ? 624 : stage == "egg" ? 752 : 656); if (mode != 10) marker.Flags |= WorldTileFlags.Active; else marker.Flags &= ~WorldTileFlags.Active; }
            if (mode == 13) for (int column = 180; column <= 220; column++) for (int row = 180; row <= 220; row++) { ref var cell = ref At(column, row); cell.TileColor = 9; cell.WallColor = 5; cell.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall; }
        }
        else
        {
            y = 200; for (int column = 199; column <= 200; column++) { ref var soil = ref At(column, 201); soil.Flags = WorldTileFlags.Active; soil.Type = (ushort)(mode == 16 || mode == 17 || mode == 23 ? 19 : mode == 18 || mode == 19 ? 225 : mode == 20 ? 659 : 1); if (mode == 17) soil.TrySetSourceShape(false, 1); if (mode == 23 && column == 199) soil.TrySetSourceShape(true, 1); }
            ref var floor = ref At(200, 201); if (mode == 1) floor.TrySetSourceShape(true, 0); if (mode == 2) floor.TrySetSourceShape(false, 1); if (mode == 3) floor.TrySetSourceShape(false, 3); if (mode == 4) floor.Flags |= WorldTileFlags.Inactive; if (mode == 5) floor.Flags &= ~WorldTileFlags.Active;
            ref var target = ref At(200, 200); if (mode >= 6 && mode <= 9) { target.LiquidAmount = 128; target.LiquidKind = (WorldLiquidKind)(mode - 6); }
            if (mode == 10) w.TileStore.LiquidUpdates.TryEnqueue(200, 200);
            if (mode == 11) { target.Type = 54; target.FrameX = 36; target.FrameY = 72; target.TileColor = 9; target.TrySetSourceShape(true, 3); target.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.WireYellow; }
            if (mode >= 12 && mode <= 14) { target.Flags |= WorldTileFlags.Active; target.Type = (ushort)(mode == 12 ? 3 : mode == 13 ? 178 : 488); }
            if (mode == 15) At(199, 199) = new() { Flags = WorldTileFlags.Active, Type = 1 }; if (mode == 21) target.Flags |= WorldTileFlags.Inactive;
            if (mode == 22) for (int column = 199; column <= 200; column++) for (int row = 199; row <= 201; row++) { ref var cell = ref At(column, row); cell.TileColor = 9; cell.WallColor = 5; cell.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall | WorldTileFlags.WireYellow; }
        }
        return w; ref WorldTile At(int column, int row) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(column, row)];
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string State() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
