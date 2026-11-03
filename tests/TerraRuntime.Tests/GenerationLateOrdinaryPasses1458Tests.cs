using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationLateOrdinaryPasses1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using JsonDocument source = ReadSource();
        foreach (JsonElement row in source.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetProperty("name").GetString()!, row.GetProperty("seed").GetInt32(), row.GetProperty("gridHash").GetString()!, row.GetProperty("rngHash").GetString()!, row.GetProperty("next").GetInt32(), row.GetProperty("solidityHash").GetString()!];
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Registered_late_pass_and_sand_plant_boundary_match_original(
        string name, int seed, string gridHash, string rngHash, int next, string solidityHash)
    {
        using JsonDocument source = ReadSource();
        JsonElement fixture = source.RootElement.GetProperty("definitions").EnumerateArray().Single(d => d.GetProperty("name").GetString() == name);
        int width = fixture.GetProperty("width").GetInt32(), height = fixture.GetProperty("height").GetInt32(), stage = fixture.GetProperty("stage").GetInt32();
        var workspace = new Workspace(width, height);
        workspace.TrySetLayers(250, 300); workspace.TrySetSpawn(500, 200);
        // The original resets a4200-wide world before an authored1000x500 predecessor replaces its grid.
        // Reset state is retained, while RunPass resets the independent per-pass RNG to the requested seed.
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(seed), 4200, false));
        workspace.InitializeVanillaGenerationSolidity();
        int identity = 0;
        foreach (JsonElement solid in source.RootElement.GetProperty("sourceInputSolidity").EnumerateArray())
            workspace.SetVanillaGenerationSolid(new((ushort)identity++), solid.GetBoolean());
        workspace.SetVanillaLihzahrdAltarState(new(450, 220)); workspace.SetVanillaLiquidLines(320, 360);
        workspace.SetVanillaOasisAnchors(stage == 100 && fixture.GetProperty("variant").GetInt32() >= 1 ? [new VanillaOasisAnchor1458(500, 220, 20)] : []);
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
        if (stage == 529)
        {
            var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace: workspace, useWorldGenerationSolidity: true);
            foreach (JsonElement target in fixture.GetProperty("framingTargets").EnumerateArray()) framing.TileFrame(target[0].GetInt32(), target[1].GetInt32());
        }
        else
        {
            FinalStage1458[] stages = [FinalStage1458.CactusPalmTreesCoral, FinalStage1458.TileCleanup, FinalStage1458.LihzahrdAltars, FinalStage1458.WaterPlants, FinalStage1458.Stalac, FinalStage1458.RemoveBrokenTraps];
            var request = new WorldGenerationRequest(Provider1458.GeneratorId, "NativeLateBoundary", (ulong)seed, width, height) { Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption) };
            new FinalPass1458(stages[stage - 100], new FinalState1458()).Execute(new Context(request, workspace, random, TestContext.Current.CancellationToken));
        }
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
        using Stream compressed = typeof(GenerationLateOrdinaryPasses1458Tests).Assembly.GetManifestResourceStream("GenerationLateOrdinaryPasses1458")!;
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
