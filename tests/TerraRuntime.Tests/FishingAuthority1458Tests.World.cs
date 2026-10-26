using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Actual_world_rain_clock_and_biome_scan_feed_fishing_without_a_spurious_cloud_bonus()
    {
        var clock = new RuntimeWorldClock(20000, true, (VanillaMoonPhase)2, 0, 1,
            maxRain: .5f, freezeWind: true, freezeRain: true, raining: true, rainTime: 1000);
        using var f = new FishingFixture(1, worldClock: clock);
        Assert.True(f.Runtime.Npcs.TryCaptureFishingWorld(f.Member, out var world, out var weather, out var beforeBiomes));
        Assert.True(weather.Raining); Assert.Equal(0f, weather.CloudAlpha);
        Assert.Equal(clock.Time, weather.Time); Assert.Equal(clock.DayTime, weather.DayTime);
        Assert.Equal((int)clock.MoonPhase, weather.MoonPhase);
        Assert.Equal(400, world.Width); Assert.Equal(600, world.Height);
        Assert.Equal(80d, world.Surface); Assert.Equal(180d, world.RockLayer);
        Assert.False(beforeBiomes.HasFlag(FishingBiomes1458.Snow));
        using Stream compressed = typeof(FishingAuthority1458Tests).Assembly.GetManifestResourceStream("FishingRulesLinux1458")!;
        using var zip = new GZipStream(compressed, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(zip);
        var oracle = source.RootElement.GetProperty("power").EnumerateArray().Single(row =>
            row.GetProperty("day").GetBoolean() && row.GetProperty("time").GetDouble() == 20000 &&
            row.GetProperty("moon").GetInt32() == 2 && row.GetProperty("rain").GetBoolean() &&
            !row.GetProperty("blood").GetBoolean() && row.GetProperty("cloud").GetSingle() == 0);
        float multiplier = oracle.GetProperty("multiplier").GetSingle();
        Assert.Equal(multiplier, FishingRules1458.GetPowerMultiplier(weather));
        Assert.True(f.Players.TryCaptureFishingPlayer(f.Member, weather, out var player));
        Assert.Equal((int)(65 * multiplier), player!.Conditions.FinalFishingLevel);
        // Change real cells inside the existing169x124 scene census, below the
        // pond and away from the player body. Packet36 contributes no biome.
        for (int x = 20; x < 70; x++)
            for (int y = 120; y < 150; y++)
                f.Tiles.Set(x, y, new WorldTile { Type = 147, Flags = WorldTileFlags.Active });
        Assert.True(f.Runtime.Npcs.TryCaptureFishingWorld(f.Member, out var afterWorld, out var afterWeather, out var afterBiomes));
        Assert.True(afterBiomes.HasFlag(FishingBiomes1458.Snow));
        Assert.Equal(world, afterWorld); Assert.Equal(weather, afterWeather);
        clock.Tick();
        Assert.True(f.Runtime.Npcs.TryCaptureFishingWorld(f.Member, out _, out var advancedWeather, out _));
        Assert.Equal(weather.Time + 1, advancedWeather.Time);
        Assert.Equal(multiplier, FishingRules1458.GetPowerMultiplier(advancedWeather));
    }
}
