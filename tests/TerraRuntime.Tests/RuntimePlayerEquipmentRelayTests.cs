using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerEquipmentRelayTests
{
    [Fact]
    public void Pre_spawn_equipment_is_cached_and_exchanged_on_spawn()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(2);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        var first = new PlayerSlotId(1);
        var second = new PlayerSlotId(2);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));

        PlayerEquipmentCommitRequest firstSlot0 = CreateEquipment(first, 0, 10);
        PlayerEquipmentCommitRequest firstSlot1 = CreateEquipment(first, 1, 11);
        PlayerEquipmentCommitRequest secondSlot0 = CreateEquipment(second, 0, 20);
        registry.PlayerEquipmentUpdated(firstSource, in firstSlot0);
        registry.PlayerEquipmentUpdated(firstSource, in firstSlot1);
        registry.PlayerEquipmentUpdated(secondSource, in secondSlot0);
        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(0, secondOutbound.QueuedFrames);

        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second);
        registry.PlayerSpawned(firstSource, in firstSpawn);
        registry.PlayerSpawned(secondSource, in secondSpawn);

        Assert.Equal(1, firstOutbound.QueuedFrames);
        Assert.Equal(2, secondOutbound.QueuedFrames);
        Assert.Equal(3, registry.EquipmentBaselineFrames);
        Assert.Equal(0, registry.DroppedEquipmentSnapshotUpdates);
    }

    [Fact]
    public void Playing_equipment_update_relays_even_when_snapshot_store_is_bounded()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(10);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(20);
        var firstOutbound = CreateLargeOutbound();
        var secondOutbound = CreateLargeOutbound();
        var first = new PlayerSlotId(10);
        var second = new PlayerSlotId(20);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second);
        registry.PlayerSpawned(firstSource, in firstSpawn);
        registry.PlayerSpawned(secondSource, in secondSpawn);

        for (int i = 0; i < 513; i++)
        {
            PlayerEquipmentCommitRequest update = CreateEquipment(first, checked((short)i), checked((short)(100 + i)));
            registry.PlayerEquipmentUpdated(firstSource, in update);
        }

        Assert.Equal(513, secondOutbound.QueuedFrames);
        Assert.Equal(513, registry.RelayedEquipmentFrames);
        Assert.Equal(1, registry.DroppedEquipmentSnapshotUpdates);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static TerrariaConnectionOutboundQueue CreateLargeOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 1_024, maxQueuedBytes: 1_048_576, maxFrameBytes: 1_024));

    private static PlayerSpawnCommitRequest CreateSpawn(PlayerSlotId slot) =>
        new(
            slot,
            SpawnX: 100,
            SpawnY: 200,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 0,
            SpawnContext: 0);

    private static PlayerEquipmentCommitRequest CreateEquipment(PlayerSlotId slot, short equipmentSlot, short itemNetId) =>
        new(
            slot,
            SlotId: equipmentSlot,
            Stack: 1,
            Prefix: 0,
            ItemNetId: itemNetId,
            ItemFlags: 0);
}
