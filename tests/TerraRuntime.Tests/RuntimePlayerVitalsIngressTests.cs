using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerVitalsIngressTests
{
    [Fact]
    public void Health_normalizes_vanilla_minimum_max_life_before_queueing()
    {
        var inner = new CapturingIngress();
        var ingress = new RuntimePlayerHealthIngress(inner);
        var slot = new PlayerSlotId(3);
        ConnectionHandle connection = Connection(slot);
        var request = new PlayerHealthCommitRequest(slot, Life: 1, MaxLife: 1);

        Assert.True(ingress.TryPost(connection, in request));

        PlayerHealthRuntimeCommand posted = Assert.IsType<PlayerHealthRuntimeCommand>(inner.Command);
        Assert.Equal((short)1, posted.Request.Life);
        Assert.Equal((short)20, posted.Request.MaxLife);
    }

    [Fact]
    public void Vitals_reject_player_slot_mismatch_before_queueing()
    {
        var inner = new CapturingIngress();
        var healthIngress = new RuntimePlayerHealthIngress(inner);
        var manaIngress = new RuntimePlayerManaIngress(inner);
        var assigned = new PlayerSlotId(3);
        var forged = new PlayerSlotId(4);
        ConnectionHandle connection = Connection(assigned);
        var health = new PlayerHealthCommitRequest(forged, Life: 100, MaxLife: 100);
        var mana = new PlayerManaCommitRequest(forged, Mana: 20, MaxMana: 20);

        Assert.False(healthIngress.TryPost(connection, in health));
        Assert.False(manaIngress.TryPost(connection, in mana));
        Assert.Equal(0, inner.Posted);
    }

    private static ConnectionHandle Connection(PlayerSlotId slot) =>
        new(
            GameCommandSourceId.FromConnection(7),
            new PlayerHandle(slot, new PlayerSessionGeneration(1)));

    private sealed class CapturingIngress : IGameCommandIngress<RuntimeCommand>
    {
        public int Posted { get; private set; }
        public RuntimeCommand? Command { get; private set; }

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Assert.False(source.IsSystem);
            Command = command;
            Posted++;
            return true;
        }
    }
}
