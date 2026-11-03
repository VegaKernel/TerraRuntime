using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Core.Worlds.Generation;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationCrimsonPrefix1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument source = ReadSource();
        foreach (JsonElement row in source.RootElement.GetProperty("prefixCases").EnumerateArray())
            yield return [row.GetProperty("key").GetString()!];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_executor_reaches_original_evil_and_adjacent_boundaries_without_imports(string key)
    {
        using JsonDocument source = ReadSource();
        JsonElement row = source.RootElement.GetProperty("prefixCases").EnumerateArray().Single(p => p.GetProperty("key").GetString() == key);
        int seed = row.GetProperty("seed").GetInt32(); bool crimson = row.GetProperty("crimson").GetBoolean(), sheet = row.GetProperty("sheet").GetBoolean();
        var workspace = new Workspace(4200, 1200); Assert.True(workspace.TileStore.TryAttachTileEntities([], 1000));
        var npcs = new RuntimeNpcStore(); var projectiles = new RuntimeProjectileStore();
        npcs.SetVanillaSpawnContextSource(() => new VanillaNpcSpawnContext(1, 0, false) { WorldDifficulty = 1 });
        npcs.SetVanillaSpawnWetCollisionSource((x, y, w, h) => false);
        workspace.AttachEntityEffects(new WorldGenerationEntityEffects1458(npcs, projectiles, true), 0, 1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var observer = new Observer(workspace, cancellation, sheet);
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "CrimsonSourcePrefix", (ulong)seed, 4200, 1200)
        { SeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture), Options = new(WorldGenerationGameMode.Classic, crimson ? WorldGenerationEvil.Crimson : WorldGenerationEvil.Corruption) };
        var result = RuntimeWorldGenerationExecutor.Execute(new Provider(new SourceBackedFinal1458(), observer), request, workspace, null, cancellation.Token);
        Assert.Equal("Cancelled", result.Status.ToString());
        foreach (JsonElement point in row.GetProperty("points").EnumerateArray())
        {
            string name = point.GetProperty("name").GetString()!;
            var captured = observer.Points[name];
            Assert.Equal(point.GetProperty("grid").GetString(), captured.Grid);
            Assert.Equal(point.GetProperty("rng").GetString(), captured.Random);
            Assert.Equal(point.GetProperty("solid").GetString(), captured.Solid);
        }
        if (sheet) Assert.Equal(row.GetProperty("inputGrid").GetString(), observer.InputGrid);
        JsonElement actualBootstrap = JsonSerializer.SerializeToElement(workspace.VanillaBootstrapState);
        foreach (JsonProperty property in row.GetProperty("bootstrap").EnumerateObject())
        {
            // A forced evil has no random selection; the original observer marks this N/A while
            // the existing retained bool keeps false. Every actually observed retained value is checked.
            if (property.Name == "RandomEvilCrimson" && property.Value.ValueKind == JsonValueKind.Null) continue;
            Assert.True(JsonElement.DeepEquals(property.Value, actualBootstrap.GetProperty(property.Name)), property.Name);
        }
        Assert.Equal(0, npcs.ActiveCount); Assert.Equal(0, projectiles.ActiveCount);
    }

    private sealed class Provider(IWorldGenerationProvider inner, Observer observer) : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => inner.Id;
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) => inner.BuildPlan(in request, new Builder(builder, observer));
    }
    private sealed class Builder(IWorldGenerationPlanBuilder inner, Observer observer) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => inner.Add(descriptor, new Pass(descriptor, pass, observer));
    }
    private sealed class Pass(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass inner, Observer observer) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context) { observer.Before(descriptor); inner.Execute(context); observer.After(descriptor, context); }
    }
    private sealed class Observer(Workspace workspace, CancellationTokenSource cancellation, bool sheet)
    {
        public readonly Dictionary<string, (string Grid, string Random, string Solid)> Points = [];
        public string? InputGrid;
        public void Before(WorldGenerationPassDescriptor descriptor)
        {
            if (!sheet || !descriptor.Id.Value.EndsWith("/Corruption", StringComparison.Ordinal)) return;
            int y = (int)workspace.VanillaTerrainState!.Value.WorldSurfaceLow - 5;
            for (int x = 380; x < workspace.WidthTiles - 380; x++)
            {
                ref WorldTile tile = ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];
                tile.Flags |= WorldTileFlags.Active; tile.Type = 659;
            }
            InputGrid = GenerationNaturalPrefix49Tests.GridHash(workspace);
        }
        public void After(WorldGenerationPassDescriptor descriptor, IWorldGenerationContext context)
        {
            string name = descriptor.Id.Value.Split('/')[^1];
            if (name is not ("Underworld" or "Corruption" or "Lakes" or "Slush")) return;
            byte[] solid = Enumerable.Range(0, 754).Select(i => (byte)(workspace.IsVanillaGenerationSolid(new((ushort)i)) ? 1 : 0)).ToArray();
            Points.Add(name, (GenerationNaturalPrefix49Tests.GridHash(workspace), GenerationNaturalPrefix49Tests.RandomHash(context.VanillaRandom!, includeReport: false), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(solid))));
            if (name == "Slush") cancellation.Cancel();
        }
    }
    private static JsonDocument ReadSource()
    {
        using Stream compressed = typeof(GenerationCrimsonPrefix1458Tests).Assembly.GetManifestResourceStream("GenerationCrimsonCallGraph1458")!;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }
}
