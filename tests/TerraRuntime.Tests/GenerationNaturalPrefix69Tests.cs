using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationNaturalPrefix69Tests
{
    public static TheoryData<ulong, string, string, string, int, int> OriginalRows => new()
    {
        { 1458UL, "F47E9D0D04E6ECA41F7E91D94FD3F2E7DD987C02398A7B65F83E705B9C082C0D", "65FAB24C068281CBEC11EF7C6E11EDB3F9C514B6285CFEE750859355B3B03227", "3759D84A00707C792800BFEAFDF6804330CE1E665D2008149100FC63CBC3F845", 172, 99 },
        { 42UL, "999D647D04B94ED517555961047E73BB9F1610D9A3701DD6A484A795466E9DC9", "6BD039D8C1A48FA59DFE9C5DB9D2022FB6F729D92D58E26F2EFA7AE6E68254E6", "2B8CF4A0E5ADEDD2A91A832D9C3E4F4604C107592ED3D51FDC4F4544F27B0367", 168, 94 },
    };

    private static readonly IReadOnlyDictionary<string, string> OriginalPhaseHashes = new Dictionary<string, string>
    {
        ["terraria:1.4.5.8/RemoveWaterFromSand"] = "6200FBFFAA1881DDBD7B8644FF79D9910B868006A93FFDE00BC638DCE8BB2466",
        ["terraria:1.4.5.8/SmoothWorld"] = "647F7B2EA31AB12FFD158A7287D4637F25527E4418D1AD26BF815F1237FF6A8C",
        ["terraria:1.4.5.8/Waterfalls"] = "647F7B2EA31AB12FFD158A7287D4637F25527E4418D1AD26BF815F1237FF6A8C",
        ["terraria:1.4.5.8/Ice"] = "36F4A507E48AE2353186B7213345648B65F0A00FA9C26B3AFEEFBDAD6B6FA4D0",
        ["terraria:1.4.5.8/LifeCrystals"] = "0A33660C2C65E228BB8F62D8D98B70ECBCC6FB55E139A1BCC30D6A6F94281C41",
        ["terraria:1.4.5.8/BuriedChests"] = "0A33660C2C65E228BB8F62D8D98B70ECBCC6FB55E139A1BCC30D6A6F94281C41",
        ["terraria:1.4.5.8/SpiderCaves"] = "C3A9E5A56998173D04A497832B5CBAD8B22AFD409B3D8F8A633DF0DFF45A3782",
        ["terraria:1.4.5.8/JungleTrees"] = "63F58AE9DE568BFBA7D4217CF74A6A6397B260D1B41A02F5CF17304FD83F30CD",
    };

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Continuous_generation_through_underground_jungle_trees_matches_original_grid_rng_and_retained_state(
        ulong seed, string expectedGrid, string expectedRandom, string expectedRetained, int chestCount, int protectedCount)
    {
        // Untouched official GenerateWorld: clearWorld/Reset plus all first69native passes. No imported input.
        // Actual original pass-through observations also capture all754 Main.tileSolid values before GenerateWorld
        // restores its arrays; each hook is qualified against the untouched entire final grid and physical RNG.
        var workspace = new Workspace(4200, 1200);
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(provider.Id, "ContinuousPrefix69", seed, 4200, 1200)
        {
            SeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption)
        };
        using var cancel = new CancellationTokenSource();
        var capture = new Capture(workspace, cancel);
        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            new Provider(provider, capture), request, workspace, null, cancel.Token);
        Assert.True(result.Status == WorldGenerationExecutionStatus.Cancelled, $"{result.Status}: {result.Error}");
        Assert.Equal(SourceBackedLateStructures1458.JungleTreesId, result.PassId);
        Assert.Equal(8, capture.PhaseBoundaries);
        Assert.Equal(expectedGrid, capture.Grid);
        Assert.Equal(expectedRandom, capture.Random);
        Assert.Equal(expectedRetained, capture.Retained);
        Assert.Equal(chestCount, workspace.GeneratedChestCount);
        Assert.Equal(protectedCount, capture.ProtectedCount);
    }

    private sealed class Provider(IWorldGenerationProvider provider, Capture capture) : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => provider.Id;
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            provider.BuildPlan(in request, new Builder(builder, capture));
    }
    private sealed class Builder(IWorldGenerationPlanBuilder builder, Capture capture) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            builder.Add(descriptor, new Pass(descriptor, pass, capture));
    }
    private sealed class Pass(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass, Capture capture) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            pass.Execute(context);
            capture.After(descriptor.Id, pass, context);
        }
    }
    private sealed class Capture(Workspace workspace, CancellationTokenSource cancel)
    {
        private ChestPlacementState1458? chestState;
        public string Grid { get; private set; } = string.Empty;
        public string Random { get; private set; } = string.Empty;
        public string Retained { get; private set; } = string.Empty;
        public int ProtectedCount { get; private set; }
        public int PhaseBoundaries { get; private set; }
        public void After(WorldGenerationPassId id, IWorldGenerationPass pass, IWorldGenerationContext context)
        {
            if (OriginalPhaseHashes.TryGetValue(id.Value, out string? expectedPhase))
            {
                byte[] actual = Enumerable.Range(0, 754)
                    .Select(i => (byte)(workspace.IsVanillaGenerationSolid(new TileTypeId(i)) ? 1 : 0)).ToArray();
                Assert.Equal(expectedPhase, Convert.ToHexString(SHA256.HashData(actual)));
                PhaseBoundaries++;
            }
            if (pass is ChestPlacementPass1458)
                chestState = (ChestPlacementState1458)pass.GetType().GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pass)!;
            if (id != SourceBackedLateStructures1458.JungleTreesId) return;
            Grid = GenerationNaturalPrefix49Tests.GridHash(workspace);
            Random = GenerationNaturalPrefix49Tests.RandomHash(context.VanillaRandom!);
            ChestPlacementState1458 state = chestState!;
            var regions = new List<WorldTileRegion> { workspace.VanillaShimmerStructure!.Value };
            foreach (WorldTileRegion r in workspace.VanillaHiveProtectedAreas) regions.Add(r);
            foreach (WorldTileRegion r in workspace.VanillaJungleShrineProtectedAreas) regions.Add(r);
            foreach (CaveHouseRoom1458 r in state.ProtectedHouseRooms) regions.Add(new(r.X, r.Y, r.Width, r.Height));
            ProtectedCount = regions.Count;
            Retained = RetainedHash(workspace, regions);
            cancel.Cancel();
        }
    }

    private static string RetainedHash(Workspace workspace, IReadOnlyList<WorldTileRegion> regions)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        WorldChest[] chests = workspace.CaptureGeneratedChests();
        writer.Write(chests.Length);
        foreach (WorldChest chest in chests)
        {
            writer.Write((int)chest.SlotId); writer.Write(chest.X); writer.Write(chest.Y);
            byte[] name = Encoding.UTF8.GetBytes(chest.Name); writer.Write(name.Length); writer.Write(name);
            writer.Write(chest.Items.Length);
            foreach (WorldChestItem item in chest.Items)
            { writer.Write(item.ItemType); writer.Write(item.Stack); writer.Write((int)item.Prefix); }
        }
        writer.Write(regions.Count);
        foreach (WorldTileRegion r in regions)
        { writer.Write(r.X); writer.Write(r.Y); writer.Write(r.Width); writer.Write(r.Height); }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
}
