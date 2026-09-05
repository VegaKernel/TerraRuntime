using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedUndergroundMorphologyTests
{
    [Fact]
    public void Canonical_world_sizes_have_scaled_multi_family_underground_plans()
    {
        var fixtures = new (int Width, int Height, ulong Seed)[]
        {
            (4200, 1200, 0x0F7145EDUL),
            (6400, 1800, 0x1234ABCDUL),
            (8400, 2400, 0x987654321UL)
        };

        int previousFeatures = 0;
        foreach ((int width, int height, ulong seed) in fixtures)
        {
            (int surface, int rock, int underworld, int ocean) = Layers(width, height);
            UndergroundMorphology.PlanMetrics first =
                UndergroundMorphology.AnalyzePlan(seed, width, height, surface, rock, underworld, ocean);
            UndergroundMorphology.PlanMetrics replay =
                UndergroundMorphology.AnalyzePlan(seed, width, height, surface, rock, underworld, ocean);

            Assert.Equal(first, replay);
            Assert.True(first.CheeseCaverns >= 8);
            Assert.True(first.SpaghettiTunnels >= 10);
            Assert.True(first.NoodleTunnels >= 12);
            Assert.Equal(first.CheeseCaverns - 1, first.ConnectorTunnels);
            Assert.True(first.HorizontalSectors >= 10, $"{width}x{height} covers only {first.HorizontalSectors} horizontal sectors.");
            Assert.Equal(4, first.VerticalBands);
            Assert.True(first.MinimumY > surface);
            Assert.True(first.MaximumY < underworld);
            Assert.True(first.TotalFeatures > previousFeatures, $"{width}x{height} did not scale its underground feature budget.");
            previousFeatures = first.TotalFeatures;
        }
    }

    [Fact]
    public void Different_seeds_change_underground_plan_fingerprint()
    {
        const int width = 4200;
        const int height = 1200;
        (int surface, int rock, int underworld, int ocean) = Layers(width, height);

        ulong first = UndergroundMorphology.AnalyzePlan(1UL, width, height, surface, rock, underworld, ocean).Fingerprint;
        ulong second = UndergroundMorphology.AnalyzePlan(2UL, width, height, surface, rock, underworld, ocean).Fingerprint;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Final_optimized_plan_orders_surface_and_underground_morphology_before_walkers()
    {
        var request = new WorldGenerationRequest(
            OptimizedProvider.GeneratorId,
            "Optimized cave ordering",
            Seed: 0x5EEDC0DEUL,
            WidthTiles: 640,
            HeightTiles: 320);
        var capture = new CapturePlanBuilder();
        new SurfaceDecorationProvider().BuildPlan(in request, capture);

        int biomes = capture.IndexOf("terraruntime:optimized/biomes");
        int terrain = capture.IndexOf("terraruntime:optimized/terrain-morphology-v2");
        int underground = capture.IndexOf("terraruntime:optimized/underground-morphology-v2");
        int walkers = capture.IndexOf("terraruntime:optimized/caves");

        Assert.True(biomes >= 0 && terrain > biomes && underground > terrain && walkers > underground);
        Assert.Contains(
            new WorldGenerationPassId("terraruntime:optimized/terrain-morphology-v2"),
            capture.Entries[underground].Descriptor.RequiredAfter.Span.ToArray());
        Assert.Contains(
            new WorldGenerationPassId("terraruntime:optimized/underground-morphology-v2"),
            capture.Entries[walkers].Descriptor.RequiredAfter.Span.ToArray());
    }

    [Fact]
    public void Algorithm_version_is_explicit()
    {
        Assert.Equal(2, UndergroundMorphology.AlgorithmVersion);
    }

    private static (int Surface, int Rock, int Underworld, int Ocean) Layers(int width, int height)
    {
        int surface = Math.Clamp((int)Math.Round(height * 0.30d), 64, height - 150);
        int rock = Math.Clamp((int)Math.Round(height * 0.52d), surface + 40, height - 90);
        int underworld = Math.Clamp((int)Math.Round(height * 0.84d), rock + 40, height - 45);
        int ocean = Math.Clamp(width / 12, 48, 360);
        return (surface, rock, underworld, ocean);
    }

    private readonly record struct CapturedPass(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly List<CapturedPass> entries = [];
        public IReadOnlyList<CapturedPass> Entries => entries;

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => entries.Add(new(descriptor, pass));

        public int IndexOf(string id)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Descriptor.Id == new WorldGenerationPassId(id))
                    return i;
            }
            return -1;
        }
    }
}
