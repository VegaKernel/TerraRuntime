using System.Text.Json;

namespace TerraRuntime.Tests;

public sealed class BeeRawAi0051458Tests
{
    public static IEnumerable<object[]> Cases() => RawFlightFixture1458.Cases("BeeRawAi0051458");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Original_AI005_and_UpdateNPC_use_None_player_or_non_bee_NPC_targets(int index, string capturedJson)
    {
        _ = index;
        using var document = JsonDocument.Parse(capturedJson);
        var row = document.RootElement;
        var fixture = new RawFlightFixture1458(row, bee: true);
        var before = fixture.Before;
        if (row.GetProperty("full").GetBoolean())
        {
            fixture.Executor.Tick(fixture.Motion);
            var actual = fixture.Commits.Events.Last(commit => commit.State.Handle == before.Handle).State;
            RawFlightFixture1458.AssertState(row.GetProperty("after"), in actual);
            Assert.Equal(row.GetProperty("after").GetProperty("timeLeft").GetInt32(), actual.Simulation.TimeLeft);
            Assert.Equal(row.GetProperty("rotation").GetSingle(), actual.Simulation.Rotation);
            Assert.Equal(row.GetProperty("afterSprite").GetInt32(), actual.Simulation.SpriteDirection);
            Assert.Equal(row.GetProperty("after").GetProperty("active").GetBoolean(), fixture.Store.TryGet(before.Handle, out _));
            Assert.Equal(!row.GetProperty("noSpawnCycle").GetBoolean(), fixture.Cycle.TryBeginCycle());
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.SourceRandom.Next());
        }
        else
        {
            Assert.True(fixture.Targeting.TryStepState(in before, out var placeholder));
            Assert.True(fixture.Store.TryUpdateUnpublished(before.Handle, in placeholder, out var accepted));
            Assert.True(fixture.Targeting.TryGetBeePlan(in before, in accepted, out var planned));
            var actual = RawFlightFixture1458.Snapshot(in accepted, in planned);
            RawFlightFixture1458.AssertState(row.GetProperty("after"), in actual);
        }
    }
}
