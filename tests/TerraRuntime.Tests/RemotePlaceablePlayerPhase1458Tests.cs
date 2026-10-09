using System.Text.Json;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class RemotePlaceablePlayerPhase1458Tests
{
    [Fact]
    public void Additional_placeable_items_match_original_fresh_carried_and_manual_remote_phases()
    {
        using var source = RemotePassivePlayerPhase1458Tests.Read("RemoteFurniturePlayerPhase1458");
        int profiles = 0;
        int updates = 0;
        foreach (string platform in new[] { "linux", "windows", "carried" })
        {
            foreach (var row in source.RootElement.GetProperty(platform).EnumerateArray())
            {
                int item = row.GetProperty("spec").GetProperty("weapon").GetInt32();
                if (item != 0)
                {
                    Assert.True(VanillaRemotePassiveItemCheck1458.IsSupported(new(item)));
                    Assert.False(VanillaRemotePassiveItemCheck1458.IsSupported(new(item), new(1)));
                }
                updates += Compare(row, platform == "windows");
                profiles++;
            }
        }
        Assert.Equal(2803, source.RootElement.GetProperty("candidateIds").GetProperty("candidates").GetArrayLength());
        Assert.Equal(8557, profiles);
        Assert.Equal(38204, updates);
    }

    [Fact]
    public void Source_census_switches_early_returns_and_failed_starts_keep_shared_cursor_and_inventory()
    {
        using var source = RemotePassivePlayerPhase1458Tests.Read("RemoteFurniturePlayerPhase1458");
        int profiles = 0;
        int mixed = 0;
        int updates = 0;
        foreach (string platform in new[] { "orthogonal", "windowsOrthogonal" })
        {
            foreach (var row in source.RootElement.GetProperty(platform).EnumerateArray())
            {
                string mode = row.GetProperty("spec").GetProperty("mode").GetString()!;
                updates += Compare(row, platform == "windowsOrthogonal");
                if (mode.StartsWith("mixed", StringComparison.Ordinal)) mixed++;
                profiles++;
            }
        }
        Assert.Equal(180, profiles);
        Assert.Equal(24, mixed);
        Assert.Equal(6240, updates);
    }

    [Fact]
    public void Late_inventory_changes_and_unowned_selected_items_refuse_the_entire_placeable_census()
    {
        using var source = RemotePassivePlayerPhase1458Tests.Read("RemoteFurniturePlayerPhase1458");
        JsonElement row = source.RootElement.GetProperty("orthogonal").EnumerateArray().First(candidate =>
            candidate.GetProperty("spec").GetProperty("mode").GetString() == "mixed-0-2" &&
            candidate.GetProperty("spec").GetProperty("branch").GetString() == "living");
        foreach (short unsupported in new short[] { 5334, 155 })
        {
            using var f = new RemotePassivePlayerPhase1458Tests.Fixture(row, false);
            f.Prepare(row.GetProperty("steps")[0]);
            f.State.Tick();
            Assert.All(f.Members, member => Assert.NotNull(member.ItemPhase));
            var cursor = f.Random.Clone();
            f.Item(2, 0, unsupported, 1);
            Assert.True(f.Players.TryGetInventoryItem(f.Connections[2], 0, out var item));
            Assert.Equal(unsupported, item.ItemType.Value);
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.True(f.Random.HasSameState(cursor));
            f.State.Tick();
            Assert.All(f.Members, member => Assert.Null(member.ItemPhase));
            short original = checked((short)row.GetProperty("spec").GetProperty("weapon").GetInt32());
            f.Item(2, 0, original, 1);
            f.State.Tick();
            Assert.All(f.Members, member => Assert.Null(member.ItemPhase));
            Assert.True(f.Random.HasSameState(cursor));
        }

        using var late = new RemotePassivePlayerPhase1458Tests.Fixture(row, false);
        late.Prepare(row.GetProperty("steps")[0]);
        late.State.Tick();
        var phases = late.Members.Select(member => member.ItemPhase).ToArray();
        var physics = late.Members.Select(member => member.PhysicsPhase).ToArray();
        var health = late.Members.Select(member => member.NpcHealth).ToArray();
        var beforeRandom = late.Random.Clone();
        int callbacks = 0;
        late.Players.SetNpcHealthWorldFacts(() =>
        {
            callbacks++;
            late.Item(0, 0, 3, 1);
            return new(false, false);
        });
        Assert.False(late.Players.TryTickRemotePlayerPhase());
        Assert.True(callbacks > 0);
        Assert.Equal(phases, late.Members.Select(member => member.ItemPhase));
        Assert.Equal(physics, late.Members.Select(member => member.PhysicsPhase));
        Assert.Equal(health, late.Members.Select(member => member.NpcHealth));
        Assert.True(late.Random.HasSameState(beforeRandom));
    }

    private static int Compare(JsonElement row, bool windows)
    {
        try { return RemotePassivePlayerPhase1458Tests.Compare(row, windows); }
        catch (Exception error)
        {
            throw new Xunit.Sdk.XunitException($"Source profile {row.GetProperty("spec").GetProperty("name").GetString()}, Windows={windows}: {error}");
        }
    }
}
