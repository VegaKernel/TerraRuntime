using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed partial class WiringDartPlayerHit1458Tests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void Flame_hurt_retains_existing_capability_and_stale_owner_boundaries(bool server, int sabotage)
    {
        var row = RemainingPlayerContacts().First(); using var json = JsonDocument.Parse((string)row[0]);
        RunSourceRow(json.RootElement, server, sabotage);
    }

    public static IEnumerable<object[]> RemainingPlayerContacts()
    {
        using var input = typeof(WiringDartPlayerHit1458Tests).Assembly.GetManifestResourceStream("WiringRemainingTrapPlayerHit1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            if (row.GetProperty("immunity").GetInt32() != 70)
                foreach (bool server in new[] { false, true }) yield return [row.GetRawText(), server];
    }
    [Theory, MemberData(nameof(RemainingPlayerContacts))]
    public void Remaining_trap_player_contacts_match_original_EVP(string observation, bool server)
    {
        using var json = JsonDocument.Parse(observation); RunSourceRow(json.RootElement, server, maximum: 500);
    }
    public static IEnumerable<object[]> HellfirePlayerDots()
    {
        using var input = typeof(WiringDartPlayerHit1458Tests).Assembly.GetManifestResourceStream("WiringTrapHellfirePlayerDot1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }
    [Theory, MemberData(nameof(HellfirePlayerDots))]
    public void Hellfire_player_counter_combinations_match_original_UpdateLifeRegen(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        int mask = row.GetProperty("mask").GetInt32(), maximum = row.GetProperty("maximum").GetInt32();
        var before = new PlayerNpcHealthState1458(maximum / 2, row.GetProperty("counter").GetInt32(), row.GetProperty("time").GetSingle(), true);
        var after = VanillaRemotePlayerHealth1458.Step(before, maximum, false, false, false, true,
            row.GetProperty("expert").GetBoolean(), row.GetProperty("moving").GetBoolean(), row.GetProperty("regenerationSlots").GetInt32(),
            (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, false, false, false, false, false, (mask & 8) != 0, onFire3: true);
        Assert.NotNull(after); Assert.Equal(row.GetProperty("life").GetInt32(), after.Value.Life);
        Assert.Equal(row.GetProperty("afterCounter").GetInt32(), after.Value.RegenCount);
        Assert.Equal(row.GetProperty("afterTime").GetSingle(), after.Value.RegenTime);
    }
    [Fact]
    public void Hellfire_final_effect_is_owned_and_cannot_be_granted_by_a_display_snapshot()
    {
        var buffs = new PlayerBuffState(); buffs.ReplaceNetworkSnapshot([VanillaBuffIds.OnFire3]);
        Assert.False(buffs.CaptureDebuffFlags().OnFire3);
        Assert.True(buffs.TryApplyNpcContactBuff(VanillaBuffIds.OnFire3, 1));
        buffs.TickNpcContactBuffs(); Assert.True(buffs.CaptureDebuffFlags().OnFire3); Assert.Equal(0, buffs.GetDuration(VanillaBuffIds.OnFire3));
        buffs.TickNpcContactBuffs(); Assert.False(buffs.CaptureDebuffFlags().OnFire3);
    }
}
