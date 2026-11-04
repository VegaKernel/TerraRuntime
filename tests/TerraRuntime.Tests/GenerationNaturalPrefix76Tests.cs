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

public sealed class GenerationNaturalPrefix76Tests
{
    public static TheoryData<ulong, string, string, string, int, int> OriginalRows => new()
    {
        { 1458UL, "C6162956D6C7FAE70F759524DF1557A4AEB00299A4DBF53E24F9FB6B88AE1C52", "0C5EB2481417CCA3B0D7B31D860B2F8996B440AC22A626EA208EC90B140CB30C", "C466A05CED889C70A24BF0C2E66CE3A810C31EA6D5B7C61C25DE2408A0F7002F", 175, 99 },
        { 42UL, "9613B2DEDDA2B6534BE64F34D00CDB23CF3BF57F018000B12DA2349C1AB19630", "EBBCF2666355759EEF2B514B7F94888773829EEA3CB30D9365148A9F83F91AF2", "6369E2E972AD5499ED65FC38B6338C850D40945B7E12DBEBE64AE46822D03A5D", 171, 94 },
    };

    private static readonly IReadOnlyDictionary<(ulong Seed, string Pass), (string Grid, string Rng, string Retained, int Chests, int Protected, int Sky)> Original70To76 = new Dictionary<(ulong, string), (string, string, string, int, int, int)>
    {
        [(1458UL, "terraria:1.4.5.8/FloatingIslandHouses")] = ("266B52E4D0A9587A2BEEA5578AFF0B5BCA79BF9A35E4A9AE2899B80D6B7867BB", "0C3496C6A91DC91566E077A7F9C2E840F314F0604EDCEC77D28C5195D743B1E3", "C466A05CED889C70A24BF0C2E66CE3A810C31EA6D5B7C61C25DE2408A0F7002F", 175, 99, 3),
        [(1458UL, "terraria:1.4.5.8/QuickCleanup")] = ("2745142FBF7A19C0E31563F69FBCB81DAC024BF48B90554C1FEA3CB4EF70C6E9", "DEE19B82BF5C9C1AD8CF25DED082B9EB22137B6FA4DF31B1A931EEF5D6C12094", "C466A05CED889C70A24BF0C2E66CE3A810C31EA6D5B7C61C25DE2408A0F7002F", 175, 99, 3),
        [(1458UL, "terraria:1.4.5.8/Pots")] = ("1D18EE5AE759462C309CBCA6817992418DAE3E9F4692BA9A5EFBF42857357C07", "E4709BCEA83C45C59E534802B60ED707C1ADB410845E967C6C588427EFE099EF", "C466A05CED889C70A24BF0C2E66CE3A810C31EA6D5B7C61C25DE2408A0F7002F", 175, 99, 3),
        [(1458UL, "terraria:1.4.5.8/Hellforge")] = ("1C62B5971D52659EF6B5A13397B010CF1A8D6963936DFD776A87799592EE37F4", "C73CE554ED3A31B28F6CE30FF17B5E6CFE781665D29E124E94B5373BC3BF5AEF", "C466A05CED889C70A24BF0C2E66CE3A810C31EA6D5B7C61C25DE2408A0F7002F", 175, 99, 3),
        [(1458UL, "terraria:1.4.5.8/SpreadingGrass")] = ("EDE8CE94276A2895261867DE37B35553AEBB22BFA8FFCD4A428716431EC6C863", "C86B4FF34C59811E81A96FB7A12DFEDA6CA413F8059B167C18F6B80D9A98DEF4", "C466A05CED889C70A24BF0C2E66CE3A810C31EA6D5B7C61C25DE2408A0F7002F", 175, 99, 3),
        [(42UL, "terraria:1.4.5.8/FloatingIslandHouses")] = ("BB8B59FCECE0A8BBEDDD0DA0B7626E07D99C2E1C2DB447D0CAA0EE58A45E63A0", "0BD73D3DFC6D0B5918C5D01B7E3770696537F7D3DA682A243F521C87027434ED", "6369E2E972AD5499ED65FC38B6338C850D40945B7E12DBEBE64AE46822D03A5D", 171, 94, 3),
        [(42UL, "terraria:1.4.5.8/QuickCleanup")] = ("22FC68584B302AFD0B96E3019CE0FC480970DAC004FC5C5728EF8DC39FCD2440", "C02848CE05C15B8E05DFE750D17636EFF1774D4D74A3CAC6692D55CA00B6A072", "6369E2E972AD5499ED65FC38B6338C850D40945B7E12DBEBE64AE46822D03A5D", 171, 94, 3),
        [(42UL, "terraria:1.4.5.8/Pots")] = ("CEEBEEED18EFF006FFD10BCE0BC300AAAA8283FA3AA57952F12A0016CDF5A698", "BD530EC071AB583C45CF8D01653338A986816B41088DF619A466302708854FEC", "6369E2E972AD5499ED65FC38B6338C850D40945B7E12DBEBE64AE46822D03A5D", 171, 94, 3),
        [(42UL, "terraria:1.4.5.8/Hellforge")] = ("45D29AAC57C66AF254BEBDCFF427D68DDD630D5AE4740DE58D489898788CB1D8", "1F19082081F415BD36A9E536E6A4F0C66F24401D9DF174501757BA1BE5A7C358", "6369E2E972AD5499ED65FC38B6338C850D40945B7E12DBEBE64AE46822D03A5D", 171, 94, 3),
        [(42UL, "terraria:1.4.5.8/SpreadingGrass")] = ("54805C3C1D825DB4646BCCE76AFC66C2F0F6B2E00C9C10C63E5398C5BF0F227E", "91DBF53A4AC72B13FDA94F4B20A98D672E03A82FF93ACEBB486C6842F150EA20", "6369E2E972AD5499ED65FC38B6338C850D40945B7E12DBEBE64AE46822D03A5D", 171, 94, 3),
        [(1458UL, "terraria:1.4.5.8/SurfaceOreAndStone")] = ("798CA6B8B3F65C364B5A59DCA1BE4B55F076C05A907C9337661CEDC150835863", "3ACEBEA7BF0818F81CE7E8DEE46DBDA752B6E342E9AB88B16440C8EBFF2B5C43", "C466A05CED889C70A24BF0C2E66CE3A810C31EA6D5B7C61C25DE2408A0F7002F", 175, 99, 3),
        [(1458UL, "terraria:1.4.5.8/PlaceFallenLog")] = ("C6162956D6C7FAE70F759524DF1557A4AEB00299A4DBF53E24F9FB6B88AE1C52", "0C5EB2481417CCA3B0D7B31D860B2F8996B440AC22A626EA208EC90B140CB30C", "C466A05CED889C70A24BF0C2E66CE3A810C31EA6D5B7C61C25DE2408A0F7002F", 175, 99, 3),
        [(42UL, "terraria:1.4.5.8/SurfaceOreAndStone")] = ("8C8FD482FCE8946C3252CC8D336CB0B4AD28B90A619C25C932B9A0F2765FD4B2", "5DC48D0B79C7760887988ED2D8D9C0A9CE4C1C40EAD5E7F65C4810BD68283127", "6369E2E972AD5499ED65FC38B6338C850D40945B7E12DBEBE64AE46822D03A5D", 171, 94, 3),
        [(42UL, "terraria:1.4.5.8/PlaceFallenLog")] = ("9613B2DEDDA2B6534BE64F34D00CDB23CF3BF57F018000B12DA2349C1AB19630", "EBBCF2666355759EEF2B514B7F94888773829EEA3CB30D9365148A9F83F91AF2", "6369E2E972AD5499ED65FC38B6338C850D40945B7E12DBEBE64AE46822D03A5D", 171, 94, 3),
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
        ["terraria:1.4.5.8/QuickCleanup"] = "63F58AE9DE568BFBA7D4217CF74A6A6397B260D1B41A02F5CF17304FD83F30CD",
        ["terraria:1.4.5.8/JungleTrees"] = "63F58AE9DE568BFBA7D4217CF74A6A6397B260D1B41A02F5CF17304FD83F30CD",
    };

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Continuous_generation_through_fallen_logs_matches_original_grid_rng_and_retained_state(
        ulong seed, string expectedGrid, string expectedRandom, string expectedRetained, int chestCount, int protectedCount)
    {
        // Untouched official GenerateWorld: clearWorld/Reset plus all first76native passes. No imported input.
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
        var request = new WorldGenerationRequest(provider.Id, "ContinuousPrefix76", seed, 4200, 1200)
        {
            SeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption)
        };
        using var cancel = new CancellationTokenSource();
        var capture = new Capture(workspace, cancel);
        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            new Provider(provider, capture), request, workspace, null, cancel.Token);
        Assert.True(result.Status == WorldGenerationExecutionStatus.Cancelled, $"{result.Status}: {result.Error}");
        Assert.Equal(new WorldGenerationPassId("terraria:1.4.5.8/PlaceFallenLog"), result.PassId);
        Assert.Equal(9, capture.PhaseBoundaries);
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
            if (!Original70To76.TryGetValue((context.Request.Seed, id.Value), out var expected)) return;
            Grid = GenerationNaturalPrefix49Tests.GridHash(workspace);
            Random = GenerationNaturalPrefix49Tests.RandomHash(context.VanillaRandom!);
            ChestPlacementState1458 state = chestState!;
            var regions = new List<WorldTileRegion> { workspace.VanillaShimmerStructure!.Value };
            foreach (WorldTileRegion r in workspace.VanillaHiveProtectedAreas) regions.Add(r);
            foreach (WorldTileRegion r in workspace.VanillaJungleShrineProtectedAreas) regions.Add(r);
            foreach (CaveHouseRoom1458 r in state.ProtectedHouseRooms) regions.Add(new(r.X, r.Y, r.Width, r.Height));
            ProtectedCount = regions.Count;
            Retained = RetainedHash(workspace, regions);
            Assert.Equal(expected.Grid, Grid);
            Assert.Equal(expected.Rng, Random);
            Assert.Equal(expected.Retained, Retained);
            Assert.Equal(expected.Chests, workspace.GeneratedChestCount);
            Assert.Equal(expected.Protected, ProtectedCount);
            Assert.Equal(expected.Sky, workspace.VanillaSkyIslandHouseCount);
            if (id.Value.EndsWith("/PlaceFallenLog", StringComparison.Ordinal)) cancel.Cancel();
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
