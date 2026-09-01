using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaZombieCheckActiveTests
{
    [Fact]
    public void Nearby_player_resets_active_time_before_decrement()
    {
        VanillaNpcTargetCandidate[] players =
        [
            Player(centerX: 120f, centerY: 120f)
        ];

        Assert.True(VanillaZombieCheckActive.TryStep(
            positionX: 100f,
            positionY: 100f,
            width: 18,
            height: 40,
            timeLeft: 10,
            players,
            out VanillaZombieCheckActiveResult result));

        Assert.True(result.PlayerInActiveRange);
        Assert.True(result.PlayerInResetRange);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft - 1, result.TimeLeft);
        Assert.False(result.ShouldDespawn);
    }

    [Fact]
    public void Moderately_distant_player_keeps_npc_active_while_lifetime_counts_down()
    {
        VanillaNpcTargetCandidate[] players =
        [
            Player(centerX: 2500f, centerY: 120f)
        ];

        Assert.True(VanillaZombieCheckActive.TryStep(
            positionX: 100f,
            positionY: 100f,
            width: 18,
            height: 40,
            timeLeft: 10,
            players,
            out VanillaZombieCheckActiveResult result));

        Assert.True(result.PlayerInActiveRange);
        Assert.False(result.PlayerInResetRange);
        Assert.Equal(9, result.TimeLeft);
        Assert.False(result.ShouldDespawn);
    }

    [Fact]
    public void Expiring_lifetime_clears_active_range_protection()
    {
        VanillaNpcTargetCandidate[] players =
        [
            Player(centerX: 2500f, centerY: 120f)
        ];

        Assert.True(VanillaZombieCheckActive.TryStep(
            positionX: 100f,
            positionY: 100f,
            width: 18,
            height: 40,
            timeLeft: 1,
            players,
            out VanillaZombieCheckActiveResult result));

        Assert.Equal(0, result.TimeLeft);
        Assert.True(result.ShouldDespawn);
    }

    [Fact]
    public void No_active_range_player_despawns_immediately_even_with_time_remaining()
    {
        VanillaNpcTargetCandidate[] players =
        [
            Player(centerX: 10000f, centerY: 10000f)
        ];

        Assert.True(VanillaZombieCheckActive.TryStep(
            positionX: 100f,
            positionY: 100f,
            width: 18,
            height: 40,
            timeLeft: 750,
            players,
            out VanillaZombieCheckActiveResult result));

        Assert.False(result.PlayerInActiveRange);
        Assert.Equal(749, result.TimeLeft);
        Assert.True(result.ShouldDespawn);
    }

    [Fact]
    public void Inactive_player_does_not_keep_zombie_alive()
    {
        VanillaNpcTargetCandidate inactive = Player(centerX: 120f, centerY: 120f) with { Active = false };

        Assert.True(VanillaZombieCheckActive.TryStep(
            positionX: 100f,
            positionY: 100f,
            width: 18,
            height: 40,
            timeLeft: 10,
            [inactive],
            out VanillaZombieCheckActiveResult result));

        Assert.False(result.PlayerInActiveRange);
        Assert.True(result.ShouldDespawn);
    }

    private static VanillaNpcTargetCandidate Player(float centerX, float centerY) =>
        new(
            Slot: 0,
            CenterX: centerX,
            CenterY: centerY,
            Aggro: 0,
            Active: true,
            Dead: false,
            Ghost: false,
            NoAggro: false);
}
