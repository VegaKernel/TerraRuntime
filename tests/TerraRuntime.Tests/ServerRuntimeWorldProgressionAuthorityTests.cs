using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeWorldProgressionAuthorityTests
{
    [Fact]
    public void Supplied_world_progression_journal_is_the_runtime_authority()
    {
        var progression = new RuntimeWorldProgressionMutations();

        var state = new ServerRuntimeState(worldProgression: progression);

        Assert.Same(progression, state.WorldProgression);
    }

    [Fact]
    public void Independent_runtime_states_do_not_share_implicit_progression()
    {
        var first = new ServerRuntimeState();
        var second = new ServerRuntimeState();

        Assert.NotSame(first.WorldProgression, second.WorldProgression);
    }
}
