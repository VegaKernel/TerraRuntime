using System.Numerics;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldGenerationProvider1458Tests
{
    [Fact]
    public void Built_in_vanilla_generator_creates_deterministic_non_flat_candidate()
    {
        BuiltInWorldGeneratorSource source = BuiltInWorldGeneratorSource.Instance;
        Assert.Contains(VanillaWorldGenerationProvider1458.GeneratorId, source.CaptureWorldGeneratorIds().Span.ToArray());
        Assert.True(source.TryResolveWorldGenerator(VanillaWorldGenerationProvider1458.GeneratorId, out IWorldGenerationProvider? provider));
        Assert.NotNull(provider);

        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Vanilla",
            Seed: 12345,
            WidthTiles: 320,
            HeightTiles: 160)
        {
            SeedText = "12345"
        };
        var first = new RuntimeWorldGenerationWorkspace(request.WidthTiles, request.HeightTiles);
        var second = new RuntimeWorldGenerationWorkspace(request.WidthTiles, request.HeightTiles);

        WorldGenerationExecutionResult firstResult = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            first,
            cancellationToken: TestContext.Current.CancellationToken);
        WorldGenerationExecutionResult secondResult = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            second,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(WorldGenerationExecutionStatus.Completed, firstResult.Status);
        Assert.Equal(WorldGenerationExecutionStatus.Completed, secondResult.Status);
        Assert.Equal(RuntimeWorldGenerationFinalizationStatus.Finalized, RuntimeWorldGenerationFinalizer.Finalize(first).Status);
        Assert.Equal(RuntimeWorldGenerationFinalizationStatus.Finalized, RuntimeWorldGenerationFinalizer.Finalize(second).Status);

        int[] firstSurface = Enumerable.Range(24, request.WidthTiles - 48)
            .Select(x => FindFirstActiveY(first, x))
            .ToArray();
        int[] secondSurface = Enumerable.Range(24, request.WidthTiles - 48)
            .Select(x => FindFirstActiveY(second, x))
            .ToArray();
        Assert.Equal(firstSurface, secondSurface);
        Assert.True(firstSurface.Distinct().Count() > 2);
        Assert.True(CountLiquid(first, WorldGenerationLiquidKind.Water) > 0);
        Assert.True(CountLiquid(first, WorldGenerationLiquidKind.Lava) > 0);
    }

    [Fact]
    public void Skyblock_special_seed_produces_sparse_candidate_and_persists_profile_state()
    {
        var provider = new VanillaWorldGenerationProvider1458();
        var request = new WorldGenerationRequest(
            provider.Id,
            "Skyblock",
            Seed: 1,
            WidthTiles: 240,
            HeightTiles: 128)
        {
            SeedText = "skyblock"
        };
        var workspace = new RuntimeWorldGenerationWorkspace(request.WidthTiles, request.HeightTiles);

        WorldGenerationExecutionResult execution = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            workspace,
            cancellationToken: TestContext.Current.CancellationToken);
        RuntimeWorldGenerationFinalizationResult finalized = RuntimeWorldGenerationFinalizer.Finalize(workspace);

        Assert.Equal(WorldGenerationExecutionStatus.Completed, execution.Status);
        Assert.True(finalized.Succeeded);
        Assert.True(finalized.Metadata.VanillaSeedProfile.HasFlag(VanillaWorldSeedFlags1458.SkyblockWorld));
        int active = CountActive(workspace);
        Assert.True(active > 0);
        Assert.True(active < workspace.TileStore.Count / 10);
        Assert.Equal(0, CountLiquid(workspace, WorldGenerationLiquidKind.Water));
        Assert.Equal(0, CountLiquid(workspace, WorldGenerationLiquidKind.Lava));
    }

    [Fact]
    public void Secret_seed_catalog_accepts_all_37_modifiers_as_one_combined_seed()
    {
        const string allModifiers =
            "1.1.1.0.Abandoned manors|Arachnophobia|Beam me up|Bring a towel|Calm before the storm|" +
            "Double daring dangers|Electric Boogaloo|Fish Mox|Hocus pocus|How did I get here|I am error|" +
            "Invisible plane|Jagged rocks|Jingle all the way|Mole people|Monochrome|More traps please|" +
            "Negative infinity|Night of the Living Dead|Planetoids|Pumpkin season|Purify this|Rainbow Road|" +
            "Royale with cheese|Does that sparkle|Too easy|Waterpark|What a horrible night to have a curse|" +
            "Winter is coming|X-ray vision|Truck stop|Sandy britches|Save the rainforest|Such great heights|" +
            "The Care Bears Movie|Toadstool|We don't even test for that";

        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedProfile1458.Parse(allModifiers, fallbackSeed: 0);

        Assert.Equal(37, BitOperations.PopCount((ulong)profile.SecretModifiers));
        Assert.True(profile.HasModifier(VanillaSecretSeedModifier1458.BeamMeUp));
        Assert.True(profile.HasModifier(VanillaSecretSeedModifier1458.WeDontEvenTestForThat));
        Assert.True(profile.HasFlag(VanillaWorldSeedFlags1458.VampireSeed));
        Assert.True(profile.HasFlag(VanillaWorldSeedFlags1458.InfectedSeed));
        Assert.True(profile.HasFlag(VanillaWorldSeedFlags1458.TeamBasedSpawnsSeed));
        Assert.True(profile.HasFlag(VanillaWorldSeedFlags1458.DualDungeonsSeed));
        Assert.True(profile.HasFlag(VanillaWorldSeedFlags1458.MoreLightningSeed));
        Assert.True(profile.HasFlag(VanillaWorldSeedFlags1458.NoLightningSeed));
    }

    [Fact]
    public void Text_seed_crc_matches_executor_seed_boundary()
    {
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Seed",
            Seed: 987654321,
            WidthTiles: 128,
            HeightTiles: 96)
        {
            SeedText = "not-a-number"
        };

        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedProfile1458.Parse(request.SeedText, request.Seed);

        Assert.Equal(profile.NumericSeed, VanillaWorldSeedResolver1458.Resolve(in request));
    }

    private static int FindFirstActiveY(RuntimeWorldGenerationWorkspace workspace, int x)
    {
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            Assert.True(workspace.TryGetTile(x, y, out WorldGenerationTile tile));
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                return y;
        }

        return -1;
    }

    private static int CountActive(RuntimeWorldGenerationWorkspace workspace)
    {
        int count = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                Assert.True(workspace.TryGetTile(x, y, out WorldGenerationTile tile));
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                    count++;
            }
        }

        return count;
    }

    private static int CountLiquid(RuntimeWorldGenerationWorkspace workspace, WorldGenerationLiquidKind kind)
    {
        int count = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                Assert.True(workspace.TryGetTile(x, y, out WorldGenerationTile tile));
                if (tile.LiquidAmount > 0 && tile.LiquidKind == kind)
                    count++;
            }
        }

        return count;
    }
}
