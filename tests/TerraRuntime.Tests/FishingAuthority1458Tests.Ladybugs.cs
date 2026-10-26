using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using Xunit;

namespace TerraRuntime.Tests;

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Consumed_ladybug_bait_uses_source_rain_and_player_luck_owners_once()
    {
        using Stream compressed = typeof(FishingAuthority1458Tests).Assembly.GetManifestResourceStream("FishingRulesLinux1458")!;
        using var zip = new GZipStream(compressed, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(zip);
        int observations = 0;
        foreach (var row in source.RootElement.GetProperty("ladybugs").EnumerateArray())
        {
            var clock = new RuntimeWorldClock(10000, true, default, 0, 1, freezeWind: true, freezeRain: true);
            int initial = row.GetProperty("beforeRain").GetInt32();
            if (initial != 0)
            {
                for (int i = 0; i < 10; i++) clock.ConsumeLadyBugBait();
                for (int i = initial; i < 18000; i++) clock.Tick();
            }
            Assert.Equal(initial, clock.LadyBugRainBoost);
            clock.ConsumeLadyBugBait();
            Assert.Equal(row.GetProperty("afterRain").GetInt32(), clock.LadyBugRainBoost);
            observations++;
        }
        Assert.Equal(18, observations);
        foreach (bool gold in new[] { false, true })
        {
            using var f = new FishingFixture(1, gold ? (short)4362 : (short)4361);
            var oracle = source.RootElement.GetProperty("ladybugs").EnumerateArray().Single(row =>
                row.GetProperty("gold").GetBoolean() == gold && row.GetProperty("beforeLuck").GetInt32() == 0 &&
                row.GetProperty("beforeRain").GetInt32() == 0);
            bool consumed = false;
            for (int cast = 0; cast < 64 && !consumed; cast++)
            {
                f.Cast(); f.WaitForBite(); float luckBefore = f.Member.Luck;
                f.Pull();
                consumed = f.Item(f.BaitSlot).IsEmpty;
                if (consumed)
                {
                    int penalty = oracle.GetProperty("afterLuck").GetInt32();
                    Assert.Equal(penalty, f.Member.LuckComponents!.Value.LadyBugLuckTimeLeft);
                    Assert.Equal(luckBefore, f.Member.Luck);
                    // The world weather pass follows the player/projectile pass.
                    Assert.Equal(oracle.GetProperty("afterRain").GetInt32() - 1, f.Runtime.WorldClock!.LadyBugRainBoost);
                    f.Tick();
                    Assert.Equal(penalty + 1, f.Member.LuckComponents!.Value.LadyBugLuckTimeLeft);
                    Assert.True(f.Member.Luck < 0);
                    int rain = f.Runtime.WorldClock.LadyBugRainBoost;
                    f.Apply(new PlayerLuckFactorsRuntimeCommand(f.Connection, default));
                    Assert.Equal(penalty + 1, f.Member.LuckComponents!.Value.LadyBugLuckTimeLeft);
                    f.Control(true); f.Control(true); f.Tick();
                    Assert.Equal(rain - 1, f.Runtime.WorldClock.LadyBugRainBoost);
                }
                f.Return();
            }
            Assert.True(consumed);
            Assert.Equal(0, f.Funded(f.BaitSlot));
        }
    }
}
