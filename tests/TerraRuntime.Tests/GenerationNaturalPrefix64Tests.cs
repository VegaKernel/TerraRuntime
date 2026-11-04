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

public sealed class GenerationNaturalPrefix64Tests
{
    public static TheoryData<ulong, string, string, string, int, int> OriginalRows => new()
    {
        { 1458UL, "4D9818ABBDA4FE174F7C04F4EDFFA088B2FBBFCFAF0EE43BEEAA71604DAA94D8", "3150EE934C86E6306DF7A43807E404231D0155947B2EF86916262BB23DD18C5E", "E85E5D560510AE9B7ED0B85EA0BE82AF2BEBFF8F1D2AAE385A078CED329B42B1", 168, 99 },
        { 42UL, "14A37DF44DBDD08AFC49FB916AB7975EF487222F458E6B75F0ACD7950000D6C3", "786296D231BC95F096CB86D4C0BCDBC4B8DCF88D8F95A73C2FFC8A98B9CD9CF3", "5067C1DCC447900E5DA8A47B6B3AA61BB65376B502DBF1C14D97692F86F58E7B", 164, 94 },
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
    public void Continuous_generation_through_spider_caves_matches_original_grid_rng_and_retained_state(
        ulong seed, string expectedGrid, string expectedRandom, string expectedRetained, int chestCount, int protectedCount)
    {
        // Untouched official GenerateWorld: clearWorld/Reset plus all first64native passes. No imported input.
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
        var request = new WorldGenerationRequest(provider.Id, "ContinuousPrefix64", seed, 4200, 1200)
        {
            SeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption)
        };
        using var cancel = new CancellationTokenSource();
        var capture = new Capture(workspace, cancel);
        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            new Provider(provider, capture), request, workspace, null, cancel.Token);
        Assert.True(result.Status == WorldGenerationExecutionStatus.Cancelled, $"{result.Status}: {result.Error}");
        Assert.Equal(SourceBackedLateStructures1458.SpiderCavesId, result.PassId);
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
            if (id != SourceBackedLateStructures1458.SpiderCavesId) return;
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
