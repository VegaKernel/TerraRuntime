using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerMovementIngressTests
{
    [Fact]
    public void Rejects_invalid_state_before_the_authoritative_queue()
    {
        var inner = new CapturingIngress();
        var ingress = new RuntimePlayerMovementIngress(inner);
        var slot = new PlayerSlotId(3);
        var connection = new ConnectionHandle(
            GameCommandSourceId.FromConnection(4),
            new PlayerHandle(slot, new PlayerSessionGeneration(1)));
        PlayerMovementCommitRequest invalid = VanillaPlayerMovementNormalizerTests.Request() with
        {
            PositionX = float.NaN
        };
        PlayerMovementCommitRequest valid = VanillaPlayerMovementNormalizerTests.Request() with
        {
            HasVelocity = true,
            VelocityX = float.NaN
        };

        Assert.False(ingress.TryPost(connection, in invalid));
        Assert.True(ingress.TryPost(connection, in valid));

        PlayerMovementRuntimeCommand posted = Assert.IsType<PlayerMovementRuntimeCommand>(inner.Command);
        Assert.False(posted.Request.HasVelocity);
        Assert.Equal(0f, posted.Request.VelocityX);
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
