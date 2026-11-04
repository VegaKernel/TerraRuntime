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

public sealed class GenerationNaturalPrefix66Tests
{
    public static TheoryData<int, ulong, string, string, string, int, int> OriginalRows => new()
    {
        { 65, 1458UL, "3443441E40832AA3D7A3A1B03F6EBE6927E1C8686549A56259085C607B9E9B2A", "CFFA2D2FD5620F89AB7542EF02A581E435A4A8369B6A1791DB944A6A25F03BE7", "E85E5D560510AE9B7ED0B85EA0BE82AF2BEBFF8F1D2AAE385A078CED329B42B1", 168, 99 },
        { 65, 42UL, "1160831467B90BF706A6339940AACAA133B240C8E2E5E043FB0A76328009C4BF", "FBBD34E52D3A6F497172AF703EC9DFE79E0B89AAF90D415A4A209A6DF3398BFA", "5067C1DCC447900E5DA8A47B6B3AA61BB65376B502DBF1C14D97692F86F58E7B", 164, 94 },
        { 66, 1458UL, "B15F547B8F454431163F3E0AF4751119E1786BB06A72CBE90B9752A0954086FC", "786CAEA8F2BA5A311D0FD89A905C38024E2C21971797E90D66C8A6095CA1904B", "E85E5D560510AE9B7ED0B85EA0BE82AF2BEBFF8F1D2AAE385A078CED329B42B1", 168, 99 },
        { 66, 42UL, "D351EDC7EABA1AE0DAE81D32F3D5C1BCAE37B3DB7C61A006B498DB17550F72F6", "9578AC8B7DFCDC3D4837E9951BCD091C4F9EA50667E0AF04B220658A99145719", "5067C1DCC447900E5DA8A47B6B3AA61BB65376B502DBF1C14D97692F86F58E7B", 164, 94 },
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
    };

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Continuous_generation_through_gem_caves_and_moss_matches_original_grid_rng_and_retained_state(
        int native, ulong seed, string expectedGrid, string expectedRandom, string expectedRetained, int chestCount, int protectedCount)
    {
        // Untouched official GenerateWorld: clearWorld/Reset plus all first65/66 native passes. No imported input.
        // Actual original pass-through observations also capture all754 Main.tileSolid values before GenerateWorld
        // restores its arrays; each hook is qualified against the untouched entire final grid and physical RNG.
        var workspace = new Workspace(4200, 1200);
        var npcs = new TerraRuntime.Core.Npcs.RuntimeNpcStore();
        var projectiles = new TerraRuntime.Core.Projectiles.RuntimeProjectileStore();
        var npcContext = new TerraRuntime.Gameplay.Npcs.VanillaNpcSpawnContext(1, 0, false) { WorldDifficulty = 1 };
        npcs.SetVanillaSpawnContextSource(() => npcContext);
        npcs.SetVanillaSpawnWetCollisionSource((x, y, width, height) => VanillaWorldCollision.GetLiquidContacts(workspace.TileStore, x, y, width, height).Wet);
        Assert.True(workspace.TileStore.TryAttachTileEntities([], 1000));
        workspace.AttachEntityEffects(new TerraRuntime.Core.Worlds.Generation.WorldGenerationEntityEffects1458(npcs, projectiles, sourceServer: true), sourceMyPlayer: 0, sourceNpcDifficulty: 1);
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(provider.Id, "ContinuousPrefix66", seed, 4200, 1200)
        {
            SeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption)
        };
        using var cancel = new CancellationTokenSource();
        WorldGenerationPassId stop = native == 65 ? SourceBackedLateStructures1458.GemCavesId : SourceBackedLateStructures1458.MossId;
        var capture = new Capture(workspace, cancel, stop);
        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            new Provider(provider, capture), request, workspace, null, cancel.Token);
        Assert.True(result.Status == WorldGenerationExecutionStatus.Cancelled, $"{result.Status}: {result.Error}");
        Assert.Equal(stop, result.PassId);
        Assert.Equal(7, capture.PhaseBoundaries);
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
    private sealed class Capture(Workspace workspace, CancellationTokenSource cancel, WorldGenerationPassId stop)
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
            if (id != stop) return;
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
