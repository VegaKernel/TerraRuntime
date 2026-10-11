using TerraRuntime.Gameplay.Players;
namespace TerraRuntime.Tests;
public sealed class SelectedCoinRemotePhase1458Tests
{
    [Fact]
    public void Living_coin_selected_clocks_match_original_both_platforms()
    {
        Compare("living");
    }
    [Fact]
    public void Dead_ghost_and_outside_coin_phases_match_original_both_platforms()
    {
        Compare("dead", "ghost", "outside");
    }
    [Fact]
    public void Coin_prefix_and_late_inventory_changes_keep_existing_phase_custody()
    {
        using var source = RemotePassivePlayerPhase1458Tests.Read("SelectedCoinRemotePhase1458");
        var row = source.RootElement.GetProperty("linux")[0];
        foreach (int id in new[] { 71, 72, 73, 74 })
        {
            Assert.True(VanillaRemotePassiveItemCatalog1458.TryGet(new(id), default, out var definition));
            Assert.True(definition.PermitsUse);
            Assert.True(definition.AutoReuse);
            Assert.Equal(15, definition.FreshAnimationTicks);
            Assert.False(VanillaRemotePassiveItemCheck1458.IsSupported(new(id), new(1)));
        }
        using var f = new RemotePassivePlayerPhase1458Tests.Fixture(row, false);
        f.Prepare(row.GetProperty("steps")[0]);
        f.State.Tick();
        var before = f.Random.Clone();
        var phases = f.Members.Select(member => member.ItemPhase).ToArray();
        f.Players.SetNpcHealthWorldFacts(() => { f.Item(0, 1, 3, 1); return new(false, false); });
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.True(f.Random.HasSameState(before));
        Assert.Equal(phases, f.Members.Select(member => member.ItemPhase));
    }
    private static void Compare(params string[] branches)
    {
        using var source = RemotePassivePlayerPhase1458Tests.Read("SelectedCoinRemotePhase1458");
        int profiles = 0;
        foreach (string platform in new[] { "linux", "windowsClr4X86" })
        foreach (var row in source.RootElement.GetProperty(platform).EnumerateArray())
        {
            if (!branches.Contains(row.GetProperty("spec").GetProperty("branch").GetString())) continue;
            Assert.Equal(32, RemotePassivePlayerPhase1458Tests.Compare(row, platform == "windowsClr4X86"));
            profiles++;
        }
        Assert.Equal(branches.Length * 8, profiles);
    }
}
