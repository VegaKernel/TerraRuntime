using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class ServerOwnedPlayerControlIngressTests
{
    [Fact]
    public void Connection_cannot_post_control_state_for_a_server_owned_slot()
    {
        var slots = new PlayerSlotPool(2);
        Assert.True(slots.TryAcquireServerOwned(out PlayerSlotPool.PlayerSlotLease? serverPlayer));
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? connectionPlayer));
        Assert.NotNull(serverPlayer);
        Assert.NotNull(connectionPlayer);

        var queue = new CountingIngress();
        var connection = new ConnectionHandle(
            GameCommandSourceId.FromConnection(7),
            connectionPlayer.Handle);
        PlayerSlotId forgedSlot = serverPlayer.Slot;
        PlayerMovementCommitRequest movement = VanillaPlayerMovementNormalizerTests.Request() with
        {
            PlayerSlot = forgedSlot
        };
        PlayerAppearanceCommitRequest appearance = VanillaPlayerAppearanceNormalizerTests.Request("Forged") with
        {
            PlayerSlot = forgedSlot
        };
        var equipment = new PlayerEquipmentCommitRequest(forgedSlot, 0, 1, 0, 1, 0);
        var health = new PlayerHealthCommitRequest(forgedSlot, 100, 100);
        var mana = new PlayerManaCommitRequest(forgedSlot, 20, 20);

        Assert.False(new RuntimePlayerMovementIngress(queue).TryPost(connection, in movement));
        Assert.False(new RuntimePlayerAppearanceIngress(queue).TryPost(connection, in appearance));
        Assert.False(new RuntimePlayerEquipmentIngress(queue).TryPost(connection, in equipment));
        Assert.False(new RuntimePlayerHealthIngress(queue).TryPost(connection, in health));
        Assert.False(new RuntimePlayerManaIngress(queue).TryPost(connection, in mana));
        Assert.Equal(0, queue.Posted);
    }

    private sealed class CountingIngress : IGameCommandIngress<RuntimeCommand>
    {
        public int Posted { get; private set; }

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Posted++;
            return true;
        }
    }
}
