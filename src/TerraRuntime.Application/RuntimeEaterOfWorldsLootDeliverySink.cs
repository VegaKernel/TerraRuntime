using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

/// <summary>
/// Production Eater of Worlds loot delivery. Ordinary drops use the shared authoritative world-item store. Expert
/// Boss Bags use the same packet-90 addressed delivery and 54000-tick unpublished slot lease contract as King Slime,
/// while Master per-player items remain ordinary world items placed at each qualifying player's center.
/// </summary>
internal sealed class RuntimeEaterOfWorldsLootDeliverySink : IEaterOfWorldsLootDeliverySink
{
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeWorldItemInstancedLeaseStore? leases;
    private readonly RuntimeWorldItemReplicationRegistry? replication;
    private readonly INpcLootWorldItemMaterializer materializer;

    public RuntimeEaterOfWorldsLootDeliverySink(
        RuntimeWorldItemStore worldItems,
        RuntimeWorldItemInstancedLeaseStore? leases,
        RuntimeWorldItemReplicationRegistry? replication,
        INpcLootWorldItemMaterializer? materializer = null)
    {
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.leases = leases;
        this.replication = replication;
        this.materializer = materializer ?? VanillaNpcLootWorldItemMaterializer.Instance;
    }

    public bool CanDeliverInstanced(ItemTypeId itemType) =>
        leases is not null && replication is not null && materializer.CanMaterialize(itemType);

    public bool CanDeliverWorldItem(ItemTypeId itemType) => materializer.CanMaterialize(itemType);

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (leases is null || replication is null ||
            !materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized) ||
            !leases.TryLease(in materialized, slotLeaseTicks, out WorldItemDropReservation reservation))
        {
            return false;
        }

        TerrariaWorldItemDropState wireState = RuntimeWorldItemReplicationRegistry.MapDrop(reservation.Slot, in materialized);
        if (TerrariaWorldItemFrameEncoder.TryEncodeInstancedDrop(in wireState, out ReadOnlyMemory<byte> frame) !=
            TerrariaWorldItemFrameEncodeResult.Encoded)
        {
            leases.TryCancel(in reservation);
            return false;
        }

        for (int index = 0; index < recipients.Length; index++)
        {
            if (replication.TrySendInstanced(recipients[index].Slot, frame))
                continue;
            return false;
        }
        return true;
    }

    public bool TryDeliverWorldItem(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized) &&
               worldItems.TryAllocateDrop(in materialized, out _);
    }
}
