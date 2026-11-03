using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationNaturalSpecialPrefix19Tests
{
    private static JsonDocument Read()
    {
        using var stream = typeof(GenerationNaturalSpecialPrefix19Tests).Assembly.GetManifestResourceStream("GenerationNaturalSpecialPrefix19")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    public static IEnumerable<object[]> OriginalRows()
    {
        using var data = Read();
        for (int i = 0; i < data.RootElement.GetArrayLength(); i++) yield return new object[] { i };
    }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Continuous_basic_special_early_passes_match_original_grid_full_random_and_retained_producers(int id)
    {
        using var data = Read(); JsonElement row = data.RootElement[id];
        int width = row.GetProperty("width").GetInt32(), height = width == 4200 ? 1200 : width == 6400 ? 1800 : 2400;
        var workspace = new Workspace(width, height);
        // Actual clearWorld+Reset was also captured with these stale producer facts preloaded.
        if (id == 0)
        {
            workspace.AddVanillaLandmass(new(VanillaLandmassKind1458.SkyblockIsland, 50, 50, 21, 4, 7));
            workspace.MarkVanillaSpawnRandomized();
        }
        var npcs = new TerraRuntime.Core.Npcs.RuntimeNpcStore(); var projectiles = new TerraRuntime.Core.Projectiles.RuntimeProjectileStore();
        var npcContext = new TerraRuntime.Gameplay.Npcs.VanillaNpcSpawnContext(1, 0, false) { WorldDifficulty = 1 };
        npcs.SetVanillaSpawnContextSource(() => npcContext);
        npcs.SetVanillaSpawnWetCollisionSource((x, y, w, h) => VanillaWorldCollision.GetLiquidContacts(workspace.TileStore, x, y, w, h).Wet);
        Assert.True(workspace.TileStore.TryAttachTileEntities([], 1000));
        workspace.AttachEntityEffects(new TerraRuntime.Core.Worlds.Generation.WorldGenerationEntityEffects1458(npcs, projectiles, sourceServer: true), sourceMyPlayer: 0);
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(provider.Id, "ActualSpecialEarly", 0, width, height)
        { SeedText = row.GetProperty("text").GetString(), Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption) };
        using var cancellation = new CancellationTokenSource();
        var capture = new Capture(workspace, row.GetProperty("boundaries"), row.GetProperty("profile").GetString() == "Skyblock", cancellation);
        var result = RuntimeWorldGenerationExecutor.Execute(new Provider(provider, capture), request, workspace, null, cancellation.Token);
        Assert.True(result.Status == WorldGenerationExecutionStatus.Cancelled, $"{result.Status}: {result.Error}");
        Assert.Equal("terraria:1.4.5.8/MudCavesToGrass", result.PassId.Value); Assert.Equal(19, capture.Count);
        Assert.Equal(0, npcs.ActiveCount); Assert.Equal(0, projectiles.ActiveCount); Assert.Empty(workspace.CaptureGeneratedTileEntities());
    }
    private sealed class Provider(IWorldGenerationProvider provider, Capture capture) : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => provider.Id;
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) => provider.BuildPlan(in request, new Builder(builder, capture));
    }
    private sealed class Builder(IWorldGenerationPlanBuilder builder, Capture capture) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => builder.Add(descriptor, new Pass(descriptor, pass, capture));
    }
    private sealed class Pass(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass, Capture capture) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context) { pass.Execute(context); capture.After(descriptor, context); }
    }
    private sealed class Capture(Workspace workspace, JsonElement boundaries, bool skyblock, CancellationTokenSource cancellation)
    {
        public int Count { get; private set; }
        public void After(WorldGenerationPassDescriptor descriptor, IWorldGenerationContext context)
        {
            if (descriptor.Id.Value is "terraria:1.4.5.8/Reset" or "terraria:1.4.5.8/TerrainLayers") return;
            JsonElement expected = boundaries[Count++];
            Assert.Equal(expected.GetProperty("id").GetString(), descriptor.Id.Value);
            Assert.Equal(expected.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var random = (VanillaUnifiedRandom1458)context.VanillaRandom!.GetType().GetField("random", flags)!.GetValue(context.VanillaRandom)!;
            var cursorField = typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!;
            var slotsField = typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!;
            Assert.Equal(expected.GetProperty("cursor").GetUInt32(), (uint)cursorField.GetValue(random)!);
            Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()), (int[])slotsField.GetValue(random)!);
            var clone = new VanillaUnifiedRandom1458(0); cursorField.SetValue(clone, cursorField.GetValue(random));
            Array.Copy((int[])slotsField.GetValue(random)!, (int[])slotsField.GetValue(clone)!, 56);
            Assert.Equal(expected.GetProperty("next").GetInt32(), clone.Next());
            Assert.Equal(expected.GetProperty("phase").EnumerateArray().Select(x => x.GetBoolean()), Enumerable.Range(0, 754).Select(x => workspace.IsVanillaGenerationSolid(new(x))));
            Assert.True(workspace.TryGetLayers(out var layers));
            Assert.Equal(expected.GetProperty("surface").GetDouble(), layers.WorldSurface); Assert.Equal(expected.GetProperty("rock").GetDouble(), layers.RockLayer);
            Assert.True(ReferenceEquals(workspace.TileStore, typeof(VanillaStructureMap1458).GetField("<tiles>P", flags)!.GetValue(workspace.VanillaStructureMap)));
            Regions(expected.GetProperty("structures"), workspace.VanillaStructureMap.CaptureStructures());
            Regions(expected.GetProperty("protected"), workspace.VanillaStructureMap.CaptureProtectedStructures());
            Assert.Equal(expected.GetProperty("pyramids").EnumerateArray().Select(p => (p.GetProperty("X").GetInt32(), p.GetProperty("Y").GetInt32())), workspace.CaptureVanillaPyramidCandidates().Select(p => (p.X, p.Y)));
            var land = workspace.CaptureVanillaLandmasses(); Assert.Equal(expected.GetProperty("landmasses").GetArrayLength(), land.Length);
            for (int i = 0; i < land.Length; i++)
            {
                JsonElement value = expected.GetProperty("landmasses")[i];
                Assert.Equal(value.GetProperty("DataType").GetInt32(), (int)land[i].Kind);
                Assert.Equal(value.GetProperty("Position").GetProperty("X").GetSingle(), land[i].X); Assert.Equal(value.GetProperty("Position").GetProperty("Y").GetSingle(), land[i].Y);
                Assert.Equal(value.GetProperty("RadiusOrHalfSize").GetInt32(), land[i].RadiusOrHalfSize); Assert.Equal(value.GetProperty("Style").GetInt32(), land[i].Style); Assert.Equal(value.GetProperty("ExtraData").GetInt32(), land[i].ExtraData);
            }
            Assert.Equal(expected.GetProperty("randomizedSpawn").GetBoolean(), workspace.VanillaSpawnHasBeenRandomized);
            if (workspace.VanillaSpawnHasBeenRandomized)
            {
                Assert.True(workspace.TryGetSpawn(out var spawn)); Assert.Equal(expected.GetProperty("spawnX").GetInt32(), spawn.X); Assert.Equal(expected.GetProperty("spawnY").GetInt32(), spawn.Y);
            }
            if (expected.TryGetProperty("dungeon", out var dungeon))
            {
                DungeonSetupProfile1458 setup = workspace.VanillaDungeonSetupProfile!.Value; DungeonPalette1458 p = setup.Palette;
                Assert.Equal(dungeon.GetProperty("palette").EnumerateArray().Select(x => x.GetInt32()), new[] { p.Color, p.BrickTileType, p.BrickWallType, p.CrackedBrickTileType, p.WindowGlassWallType, p.WindowClosedGlassWallType, p.WindowEdgeWallType, p.WindowPlatformItemType });
                Assert.Equal(dungeon.GetProperty("kind").GetInt32(), (int)setup.EntranceKind); Assert.Equal(dungeon.GetProperty("seed").GetInt32(), setup.EntranceRandomSeed);
            }
            if (!skyblock && expected.TryGetProperty("tunnels", out var tunnels)) Assert.Equal(tunnels.EnumerateArray().Select(x => x.GetInt32()), workspace.VanillaTunnelColumns.ToArray());
            if (expected.TryGetProperty("mountains", out var mountains)) Assert.Equal(mountains.EnumerateArray().Select(p => (p.GetProperty("X").GetInt32(), p.GetProperty("Y").GetInt32())), workspace.VanillaMountainCaves.ToArray().Select(p => (p.X, p.Y)));
            if (expected.TryGetProperty("snow", out var snow))
            {
                Assert.Equal(snow.GetProperty("top").GetInt32(), workspace.VanillaSnowTop); Assert.Equal(snow.GetProperty("bottom").GetInt32(), workspace.VanillaSnowBottom);
                if (!skyblock)
                {
                    Assert.Equal(snow.GetProperty("left").EnumerateArray().Select(x => x.GetInt32()), workspace.VanillaSnowRows.ToArray().Select(x => x.Left));
                    Assert.Equal(snow.GetProperty("right").EnumerateArray().Select(x => x.GetInt32()), workspace.VanillaSnowRows.ToArray().Select(x => x.Right));
                }
            }
            Assert.Equal(expected.GetProperty("chests").GetInt32(), workspace.GeneratedChestCount);
            Assert.Equal(expected.GetProperty("tileEntities").GetInt32(), workspace.CaptureGeneratedTileEntities().Length);
            var liquids = workspace.TileStore.LiquidUpdates; Assert.Equal(expected.GetProperty("activeLiquids").GetInt32(), liquids.ActiveCount); Assert.Equal(expected.GetProperty("bufferedLiquids").GetInt32(), liquids.BufferedCount);
            Assert.Equal(0, expected.GetProperty("checkingLiquidCells").GetInt32()); Assert.Equal(0, expected.GetProperty("skipLiquidCells").GetInt32());
            foreach (string name in new[] { "_activeMembership", "_bufferMembership", "_skipNextUpdate" })
                if (typeof(WorldLiquidUpdateQueue).GetField(name, flags)!.GetValue(liquids) is BitArray bits) Assert.All(bits.Cast<bool>(), value => Assert.False(value));
            if (Count == 19) cancellation.Cancel();
        }
        private static void Regions(JsonElement expected, WorldTileRegion[] actual) => Assert.Equal(
            expected.EnumerateArray().Select(r => (r.GetProperty("X").GetInt32(), r.GetProperty("Y").GetInt32(), r.GetProperty("Width").GetInt32(), r.GetProperty("Height").GetInt32())), actual.Select(r => (r.X, r.Y, r.Width, r.Height)));
    }
}
