using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationVegetation86To91Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using JsonDocument source = ReadSource();
        foreach (JsonElement row in source.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetProperty("name").GetString()!, row.GetProperty("seed").GetInt32(), row.GetProperty("gridHash").GetString()!, row.GetProperty("rngHash").GetString()!, row.GetProperty("next").GetInt32(), row.GetProperty("solidityHash").GetString()!, row.GetProperty("logX").GetInt32(), row.GetProperty("logY").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Registered_vegetation_pass_and_source_plant_boundary_match_original(
        string name, int seed, string gridHash, string rngHash, int next, string solidityHash, int logX, int logY)
    {
        using JsonDocument source = ReadSource();
        JsonElement fixture = source.RootElement.GetProperty("definitions").EnumerateArray().Single(d => d.GetProperty("name").GetString() == name);
        int width = fixture.GetProperty("width").GetInt32(), height = fixture.GetProperty("height").GetInt32(), stage = fixture.GetProperty("stage").GetInt32();
        var workspace = new Workspace(width, height);
        workspace.TrySetLayers(250, 300); workspace.TrySetSpawn(500, 200);
        // The original resets a4200-wide world before an authored1000x500 predecessor replaces its grid.
        // Reset state is retained, while RunPass resets the independent per-pass RNG to the requested seed.
        var bootstrap = BootstrapPass1458.Run(new RandomAdapter(seed), 4200, false);
        // Authored predecessor explicitly has a left dungeon; no canonical terrain/dungeon prefix is claimed.
        typeof(VanillaWorldGenerationBootstrapState1458).GetProperty("DungeonLocation")!.SetValue(bootstrap, 0);
        workspace.SetVanillaBootstrapState(bootstrap);
        workspace.InitializeVanillaGenerationSolidity();
        int identity = 0;
        foreach (JsonElement solid in source.RootElement.GetProperty("sourceInputSolidity").EnumerateArray())
            workspace.SetVanillaGenerationSolid(new((ushort)identity++), solid.GetBoolean());
        workspace.SetVanillaLihzahrdAltarState(new(450, 220)); workspace.SetVanillaLiquidLines(320, 360);
        workspace.SetVanillaOasisAnchors([]);
        if (stage == 91 && fixture.GetProperty("variant").GetInt32() % 2 != 0) workspace.SetVanillaFallenLogAnchor(350, 170);
        Assert.True(workspace.TileStore.TryAttachTileEntities([], 1000));
        WorldTileFlags[] masks = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen, WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive, WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock, WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
        foreach (JsonElement cell in fixture.GetProperty("cells").EnumerateArray())
        {
            int[] row = cell.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.True ? 1 : v.ValueKind == JsonValueKind.False ? 0 : v.GetInt32()).ToArray();
            var tile = new WorldTile { Type = (ushort)row[3], Wall = (ushort)row[4], FrameX = (short)row[5], FrameY = (short)row[6], LiquidAmount = (byte)row[7], LiquidKind = (WorldLiquidKind)row[8], TileColor = (byte)row[11], WallColor = (byte)row[12] };
            if (row[2] != 0) tile.Flags |= WorldTileFlags.Active;
            Assert.True(tile.TrySetSourceShape(row[9] != 0, (byte)row[10]));
            for (int bit = 0; bit < 10; bit++) if ((row[13] & (1 << bit)) != 0) tile.Flags |= masks[bit];
            workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(row[0], row[1])] = tile;
        }
        var random = new RandomAdapter(seed);
        if (fixture.TryGetProperty("action", out _))
        {
            foreach (JsonElement target in fixture.GetProperty("targets").EnumerateArray())
                SurfacePlantPass1458.TryPlacePlant(workspace.TileStore, random, target[0].GetInt32(), target[1].GetInt32(), (ushort)target[2].GetInt32(), workspace);
        }
        else
        {
            VegetationStage1458[] stages = [VegetationStage1458.WebsAndHoney, VegetationStage1458.Weeds, VegetationStage1458.GlowingMushroomsAndJunglePlants, VegetationStage1458.JunglePlants, VegetationStage1458.Vines, VegetationStage1458.Flowers];
            var state = new VegetationState1458();
            // These authored predecessor layers are source inputs; no canonical prefix is claimed.
            typeof(VegetationState1458).GetProperty("Bootstrap")!.SetValue(state, bootstrap);
            typeof(VegetationState1458).GetProperty("WorldSurface")!.SetValue(state, 250d);
            typeof(VegetationState1458).GetProperty("RockLayer")!.SetValue(state, 300d);
            var request = new WorldGenerationRequest(Provider1458.GeneratorId, "NativeVegetationBoundary", (ulong)seed, width, height) { Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption) };
            new VegetationPass1458(stages[stage - 86], state).Execute(new Context(request, workspace, random, TestContext.Current.CancellationToken));
        }
        if (logX < 0) Assert.Null(workspace.VanillaFallenLogAnchor);
        else Assert.Equal(new VanillaFallenLogAnchor1458(logX, logY), workspace.VanillaFallenLogAnchor);
        Assert.Equal(gridHash, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(rngHash, GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(next, random.Next());
        byte[] solidBytes = Enumerable.Range(0, 754).Select(i => (byte)(workspace.IsVanillaGenerationSolid(new((ushort)i)) ? 1 : 0)).ToArray();
        Assert.Equal(solidityHash, Convert.ToHexString(SHA256.HashData(solidBytes)));
        Assert.Empty(workspace.CaptureGeneratedChests()); Assert.Equal(0, workspace.GeneratedTownNpcCount);
        Assert.Equal(0, workspace.TileStore.LiquidUpdates.ActiveCount); Assert.Equal(0, workspace.TileStore.LiquidUpdates.BufferedCount);
    }

    private static JsonDocument ReadSource()
    {
        using Stream compressed = typeof(GenerationVegetation86To91Tests).Assembly.GetManifestResourceStream("GenerationVegetation86To91Tests")!;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellation) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => cancellation;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
