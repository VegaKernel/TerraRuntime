using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class FishingRules1458Tests
{
    [Fact]
    public void Oversized_connected_pond_refuses_with_bounded_reads_and_small_pond_keeps_geometry()
    {
        int reads = 0;
        var refused = FishingRules1458.MeasurePond(4200, 50, 8400, 2400, (_, _) =>
        { reads++; return new(255, 0, false); });
        Assert.Equal(default(FishingPond1458), refused);
        // Width probes are bounded separately by the actual world's width;
        // the source vertical column census has the explicit runtime ceiling.
        Assert.InRange(reads, FishingRules1458.MaximumPondCellReads,
            FishingRules1458.MaximumPondCellReads + 8400);
        var small = FishingRules1458.MeasurePond(22, 10, 100, 100,
            (x, y) => x is >= 20 and <= 24 && y is >= 10 and <= 14
                ? new(255, 0, false) : default);
        Assert.Equal(new FishingPond1458(25, false, false, 0), small);
        var honey = FishingRules1458.MeasurePond(22, 10, 100, 100,
            (x, y) => x is >= 20 and <= 24 && y is >= 10 and <= 14
                ? new(255, 2, false) : default);
        Assert.Equal(new FishingPond1458(37, false, true, 0), honey);
    }

    [Fact]
    public void Original_conditions_bait_payment_and_current_fish_database_preserve_order_and_rng()
    {
        using Stream compressed = typeof(FishingRules1458Tests).Assembly
            .GetManifestResourceStream("FishingRulesLinux1458")!;
        using var stream = new GZipStream(compressed, CompressionMode.Decompress);
        using JsonDocument source = JsonDocument.Parse(stream);
        int metadata = 0, power = 0, payment = 0, drops = 0;
        foreach (JsonElement row in source.RootElement.GetProperty("metadata").EnumerateArray())
        {
            int type = Int(row, "id");
            Assert.Equal(Int(row, "bait"), FishingRules1458.GetBaitPower(type));
            if (Int(row, "pole") != 0)
            {
                Assert.True(FishingRules1458.TryGetPole(type, out var pole));
                Assert.Equal(Int(row, "pole"), pole.Power);
                Assert.Equal(Int(row, "shoot"), pole.ProjectileType);
                Assert.Equal(row.GetProperty("speed").GetSingle(), pole.ShootSpeed);
                Assert.Equal(Int(row, "time"), pole.UseTime);
                Assert.Equal(Int(row, "animation"), pole.UseAnimation);
            }
            metadata++;
        }
        foreach (JsonElement row in source.RootElement.GetProperty("power").EnumerateArray())
        {
            var context = new FishingPowerContext1458(Bool(row, "day"), row.GetProperty("time").GetDouble(),
                Int(row, "moon"), Bool(row, "rain"), row.GetProperty("cloud").GetSingle(), Bool(row, "blood"), 0);
            Assert.Equal(row.GetProperty("multiplier").GetSingle(), FishingRules1458.GetPowerMultiplier(context));
            power++;
        }
        foreach (JsonElement row in source.RootElement.GetProperty("payment").EnumerateArray())
        {
            var random = new SourceRandom(Int(row, "seed"));
            int caught = Int(row, "caught");
            bool consumed = FishingRules1458.ShouldConsumeBait(Int(row, "bait"), Math.Max(0, caught),
                Math.Max(0, -caught), caught == 2337, Bool(row, "tackle"), random);
            Assert.True(Bool(row, "accepted"));
            Assert.Equal(Int(row, "bait"), Int(row, "baitUsed"));
            Assert.Equal(Int(row, "remaining"), consumed ? 0 : 1);
            Assert.Equal(Int(row, "type"), consumed ? 0 : Int(row, "bait"));
            Assert.Equal(Int(row, "next"), random.Next());
            payment++;
        }
        foreach (JsonElement row in source.RootElement.GetProperty("drops").EnumerateArray())
        {
            int scene = Int(row, "scene");
            var random = new SourceRandom(Int(row, "seed"));
            FishingAttempt1458 attempt = MakeDatabaseScene(scene);
            Assert.Equal(Int(row, "item"), FishingRules1458.RollItem(attempt, 0, random));
            Assert.Equal(Int(row, "next"), random.Next());
            drops++;
        }
        foreach (JsonElement row in source.RootElement.GetProperty("timers").EnumerateArray())
        {
            var random = new SourceRandom(Int(row, "seed"));
            float timer = 0;
            for (int tick = 0; tick < Int(row, "ticks"); tick++)
                timer = FishingRules1458.AdvanceWait(timer, Int(row, "level"), random);
            Assert.Equal(row.GetProperty("timer").GetSingle(), timer);
            Assert.Equal(Int(row, "next"), random.Next());
        }
        Assert.Equal(57, metadata); Assert.Equal(56, power); Assert.Equal(240, payment); Assert.Equal(240, drops);

        var inventory = new FishingInventorySlot1458[58];
        inventory[0] = new(2676, 99); inventory[54] = new(1994, 1);
        Assert.Equal(54, FishingRules1458.SelectBait(inventory));
        inventory[54] = new(2674, 1);
        Assert.Equal(0, FishingRules1458.SelectBait(inventory, 2676));
        inventory[0] = default;
        Assert.Equal(-1, FishingRules1458.SelectBait(inventory, 2676));
        inventory[54] = default; inventory[0] = new(2294, 1); inventory[1] = new(2289, 1); inventory[55] = new(2676, 1);
        var conditions = FishingRules1458.GetConditions(inventory, 1, new(true, 10000, 2, false, 0, false, 0));
        Assert.Equal(5, conditions.PolePower); // Held pole wins over a stronger inventory pole.
        Assert.Equal(55, conditions.FinalFishingLevel);
        Assert.Equal(50, FishingRules1458.GetConditions(inventory, 2, new(true, 10000, 2, false, 0, false, 0)).PolePower);
    }

    private static FishingAttempt1458 MakeDatabaseScene(int scene)
    {
        int mask = scene switch { 0 => 0, 1 => 1, 2 => 2, 3 => 4, 4 => 8, 5 => 16, 6 => 32,
            7 => 64, 8 => 128, 9 => 256, 10 => 3, 11 => 12, 12 => 320, _ => scene * 73 % 512 };
        int quest = scene switch { 3 => 2452, 4 => 2467, 8 => 2475, 9 => 4393, 24 => 2461, _ => -1 };
        var biomes = (FishingBiomes1458)mask;
        return new(100, 800, 50, scene % 5, 300,
            new(1200, scene is 13 or 14, scene is 15 or 16, 0), default,
            new(4200, 1200, 300, 600, (scene & 1) != 0, scene >= 24, false, scene % 4 == 0,
                true, true), biomes, biomes, scene % 2 == 0, scene == 29, scene == 17, true,
            quest, new(true, true, scene % 3 == 0, scene % 4 == 0, scene % 5 == 0, scene is >= 18 and < 23));
    }

    private static int Int(JsonElement row, string name) => row.GetProperty(name).GetInt32();
    private static bool Bool(JsonElement row, string name) => row.GetProperty(name).GetBoolean();
    private sealed class SourceRandom(int seed) : Random
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public override int Next() => source.Next();
        public override int Next(int maxValue) => source.Next(maxValue);
        public override int Next(int minValue, int maxValue) => source.Next(minValue, maxValue);
        public override double NextDouble() => source.NextDouble();
    }
}
