using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerAppearanceIngressTests
{
    [Fact]
    public void Rejects_invalid_name_and_normalizes_before_the_authoritative_queue()
    {
        var inner = new CapturingIngress();
        var ingress = new RuntimePlayerAppearanceIngress(inner);
        var slot = new PlayerSlotId(3);
        var connection = new ConnectionHandle(
            GameCommandSourceId.FromConnection(4),
            new PlayerHandle(slot, new PlayerSessionGeneration(1)));
        PlayerAppearanceCommitRequest invalid = VanillaPlayerAppearanceNormalizerTests.Request("   ");
        PlayerAppearanceCommitRequest valid = VanillaPlayerAppearanceNormalizerTests.Request("  Player  ") with
        {
            Hair = byte.MaxValue
        };

        Assert.False(ingress.TryPost(connection, in invalid));
        Assert.True(ingress.TryPost(connection, in valid));

        PlayerAppearanceRuntimeCommand posted = Assert.IsType<PlayerAppearanceRuntimeCommand>(inner.Command);
        Assert.Equal("Player", posted.Request.Name);
        Assert.Equal((byte)0, posted.Request.Hair);
    }

    private sealed class CapturingIngress : IGameCommandIngress<RuntimeCommand>
    {
        public RuntimeCommand? Command { get; private set; }

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Command = command;
            return true;
        }
    }
}
