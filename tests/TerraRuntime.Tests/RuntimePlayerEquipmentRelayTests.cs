using TerraRuntime.Gameplay.Items;
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
        ConnectionHandle firstConnection = Connection(firstSource, first);
        ConnectionHandle secondConnection = Connection(secondSource, second);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));

        PlayerEquipmentCommitRequest firstSlot0 = CreateEquipment(first, 0, 10);
        PlayerEquipmentCommitRequest firstSlot1 = CreateEquipment(first, 1, 11);
        PlayerEquipmentCommitRequest secondSlot0 = CreateEquipment(second, 0, 20);
        registry.PlayerEquipmentUpdated(firstConnection, in firstSlot0);
        registry.PlayerEquipmentUpdated(firstConnection, in firstSlot1);
        registry.PlayerEquipmentUpdated(secondConnection, in secondSlot0);
        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(0, secondOutbound.QueuedFrames);

        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second);
        registry.PlayerSpawned(firstConnection, in firstSpawn);
        registry.PlayerSpawned(secondConnection, in secondSpawn);

        Assert.Equal(2, firstOutbound.QueuedFrames);
        Assert.Equal(3, secondOutbound.QueuedFrames);
        Assert.Equal(2, registry.PlayerActiveBaselineFrames);
        Assert.Equal(3, registry.EquipmentBaselineFrames);
        Assert.Equal(0, registry.DroppedEquipmentSnapshotUpdates);
    }

    [Fact]
    public void Playing_equipment_relay_matches_the_complete_vanilla_can_relay_slot_set()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(10);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(20);
        var firstOutbound = CreateLargeOutbound();
        var secondOutbound = CreateLargeOutbound();
        var first = new PlayerSlotId(10);
        var second = new PlayerSlotId(20);
        ConnectionHandle firstConnection = Connection(firstSource, first);
        ConnectionHandle secondConnection = Connection(secondSource, second);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second);
        registry.PlayerSpawned(firstConnection, in firstSpawn);
        registry.PlayerSpawned(secondConnection, in secondSpawn);

        for (short slot = 0; slot < VanillaPlayerItemSlotCatalog.Count; slot++)
        {
            PlayerEquipmentCommitRequest update = CreateEquipment(first, slot, itemNetId: 1);
            registry.PlayerEquipmentUpdated(firstConnection, in update);
        }

        Assert.Equal(1 + VanillaPlayerItemSlotCatalog.RelayableCount, secondOutbound.QueuedFrames);
        Assert.Equal(VanillaPlayerItemSlotCatalog.RelayableCount, registry.RelayedEquipmentFrames);
        Assert.Equal(0, registry.DroppedEquipmentSnapshotUpdates);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(GameCommandSourceId source, PlayerSlotId slot) =>
        new(source, new PlayerHandle(slot, new PlayerSessionGeneration(1)));

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
