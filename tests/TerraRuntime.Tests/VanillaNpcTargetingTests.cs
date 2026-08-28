using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcTargetingTests
{
    [Fact]
    public void Selects_lowest_aggro_adjusted_manhattan_distance()
    {
        VanillaNpcTargetCandidate[] candidates =
        [
            new(0, 100f, 0f, Aggro: 0, Active: true, Dead: false, Ghost: false, NoAggro: false),
            new(1, 120f, 0f, Aggro: 30, Active: true, Dead: false, Ghost: false, NoAggro: false)
        ];

        Assert.True(VanillaNpcTargeting.TrySelectClosestPlayerTarget(
            npcCenterX: 0f,
            npcCenterY: 0f,
            npcDirection: 1,
            candidates,
            out VanillaNpcTargetSelection selection));

        Assert.Equal((byte)1, selection.PlayerSlot);
        Assert.Equal(120f, selection.ManhattanDistance);
        Assert.Equal(90f, selection.AdjustedDistance);
    }

    [Fact]
    public void No_aggro_penalty_applies_only_after_npc_has_a_direction()
    {
        VanillaNpcTargetCandidate[] candidates =
        [
            new(0, 10f, 0f, Aggro: 0, Active: true, Dead: false, Ghost: false, NoAggro: true),
            new(1, 20f, 0f, Aggro: 0, Active: true, Dead: false, Ghost: false, NoAggro: false)
        ];

        Assert.True(VanillaNpcTargeting.TrySelectClosestPlayerTarget(
            0f, 0f, npcDirection: 1, candidates, out VanillaNpcTargetSelection directed));
        Assert.Equal((byte)1, directed.PlayerSlot);

        Assert.True(VanillaNpcTargeting.TrySelectClosestPlayerTarget(
            0f, 0f, npcDirection: 0, candidates, out VanillaNpcTargetSelection directionless));
        Assert.Equal((byte)0, directionless.PlayerSlot);
    }

    [Fact]
    public void Dead_inactive_and_ghost_players_are_ignored()
    {
        VanillaNpcTargetCandidate[] candidates =
        [
            new(0, 1f, 0f, 0, Active: false, Dead: false, Ghost: false, NoAggro: false),
            new(1, 2f, 0f, 0, Active: true, Dead: true, Ghost: false, NoAggro: false),
            new(2, 3f, 0f, 0, Active: true, Dead: false, Ghost: true, NoAggro: false),
            new(3, 4f, 0f, 0, Active: true, Dead: false, Ghost: false, NoAggro: false)
        ];

        Assert.True(VanillaNpcTargeting.TrySelectClosestPlayerTarget(
            0f, 0f, 1, candidates, out VanillaNpcTargetSelection selection));
        Assert.Equal((byte)3, selection.PlayerSlot);
    }

    [Fact]
    public void Equal_scores_keep_first_candidate_in_slot_order()
    {
        VanillaNpcTargetCandidate[] candidates =
        [
            new(4, 10f, 0f, 0, true, false, false, false),
            new(5, 10f, 0f, 0, true, false, false, false)
        ];

        Assert.True(VanillaNpcTargeting.TrySelectClosestPlayerTarget(
            0f, 0f, 1, candidates, out VanillaNpcTargetSelection selection));
        Assert.Equal((byte)4, selection.PlayerSlot);
    }

    [Fact]
    public void No_live_candidate_returns_false()
    {
        VanillaNpcTargetCandidate[] candidates =
        [
            new(0, 1f, 1f, 0, Active: false, Dead: false, Ghost: false, NoAggro: false)
        ];

        Assert.False(VanillaNpcTargeting.TrySelectClosestPlayerTarget(
            0f, 0f, 1, candidates, out _));
    }
}
