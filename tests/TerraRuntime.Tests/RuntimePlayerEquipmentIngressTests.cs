using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerEquipmentIngressTests
{
    [Fact]
    public void Rejects_out_of_range_slots_before_the_authoritative_queue()
    {
        var inner = new CapturingIngress();
        var ingress = new RuntimePlayerEquipmentIngress(inner);
        var slot = new PlayerSlotId(3);
        var connection = new ConnectionHandle(
            GameCommandSourceId.FromConnection(4),
            new PlayerHandle(slot, new PlayerSessionGeneration(1)));

        PlayerEquipmentCommitRequest below = CreateRequest(slot, equipmentSlot: -1);
        PlayerEquipmentCommitRequest above = CreateRequest(slot, VanillaPlayerItemSlotCatalog.Count);
        PlayerEquipmentCommitRequest valid = CreateRequest(slot, equipmentSlot: 0);

        Assert.False(ingress.TryPost(connection, in below));
        Assert.False(ingress.TryPost(connection, in above));
        Assert.True(ingress.TryPost(connection, in valid));
        Assert.Equal(1, inner.Posted);
    }

    [Fact]
    public void Normalizes_item_state_before_the_authoritative_queue()
    {
        var inner = new CapturingIngress();
        var ingress = new RuntimePlayerEquipmentIngress(inner);
        var slot = new PlayerSlotId(3);
        var connection = new ConnectionHandle(
            GameCommandSourceId.FromConnection(4),
            new PlayerHandle(slot, new PlayerSessionGeneration(1)));
        var request = new PlayerEquipmentCommitRequest(slot, 0, 7, 3, -19, byte.MaxValue);

        Assert.False(request.TryGetCanonicalItemType(out _));
        Assert.True(ingress.TryPost(connection, in request));

        PlayerEquipmentRuntimeCommand posted = Assert.IsType<PlayerEquipmentRuntimeCommand>(inner.Command);
        Assert.Equal((short)3764, posted.Request.ItemNetId);
        Assert.Equal((byte)1, posted.Request.ItemFlags);
        Assert.True(posted.Request.TryGetCanonicalItemType(out ItemTypeId itemType));
        Assert.Equal(3764, itemType.Value);
        Assert.Equal(3, posted.Request.PrefixId.Value);
    }

    private static PlayerEquipmentCommitRequest CreateRequest(
        PlayerSlotId player,
        short equipmentSlot) =>
        new(player, equipmentSlot, 1, 0, 1, 0);

    private sealed class CapturingIngress : IGameCommandIngress<RuntimeCommand>
    {
        public int Posted { get; private set; }

        public RuntimeCommand? Command { get; private set; }

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Assert.False(source.IsSystem);
            Assert.IsType<PlayerEquipmentRuntimeCommand>(command);
            Command = command;
            Posted++;
            return true;
        }
    }
}
