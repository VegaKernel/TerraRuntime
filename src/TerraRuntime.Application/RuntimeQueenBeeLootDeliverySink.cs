using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

/// <summary>Production world-item/instanced delivery for the TerrariaServer 1.4.5.8 Queen Bee death-loot slice.</summary>
internal sealed class RuntimeQueenBeeLootDeliverySink : IQueenBeeLootDeliverySink
{
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeWorldItemInstancedLeaseStore? leases;
    private readonly RuntimeWorldItemReplicationRegistry? replication;
    private readonly INpcLootWorldItemMaterializer materializer;

    public RuntimeQueenBeeLootDeliverySink(
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
        ReadOnlySpan<VanillaQueenBeeLootPlayer> recipients,
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
        if (TerrariaWorldItemFrameEncoder.TryEncodeInstancedDrop(in wireState, out ReadOnlyMemory<byte> frame) != TerrariaWorldItemFrameEncodeResult.Encoded)
        {
            leases.TryCancel(in reservation);
            return false;
        }

        foreach (VanillaQueenBeeLootPlayer recipient in recipients)
        {
            if (!replication.TrySendInstanced(recipient.Slot, frame))
                return false;
        }
        return true;
    }

    public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized) &&
               worldItems.TryAllocateDrop(in materialized, out _);
    }
}
