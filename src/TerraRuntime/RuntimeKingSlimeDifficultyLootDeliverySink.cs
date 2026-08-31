using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Production delivery boundary for the source-backed King Slime Expert/Master rules. Ordinary Master items enter the
/// shared world-item store and therefore publish packet 21. Expert Boss Bags remain unpublished server-side: the exact
/// item slot is held as a lease reservation, packet 90 is sent only to qualifying player slots, and packet 151 is
/// broadcast when the lease expires.
/// </summary>
internal sealed class RuntimeKingSlimeDifficultyLootDeliverySink : IKingSlimeDifficultyLootDeliverySink
{
    private readonly RuntimeWorldItemStore _worldItems;
    private readonly RuntimeWorldItemInstancedLeaseStore _leases;
    private readonly RuntimeWorldItemReplicationRegistry _replication;
    private readonly INpcLootWorldItemMaterializer _materializer;

    public RuntimeKingSlimeDifficultyLootDeliverySink(
        RuntimeWorldItemStore worldItems,
        RuntimeWorldItemInstancedLeaseStore leases,
        RuntimeWorldItemReplicationRegistry replication,
        INpcLootWorldItemMaterializer? materializer = null)
    {
        _worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _replication = replication ?? throw new ArgumentNullException(nameof(replication));
        _materializer = materializer ?? VanillaNpcLootWorldItemMaterializer.Instance;
    }

    public bool CanDeliverInstanced(ItemTypeId itemType) => _materializer.CanMaterialize(itemType);

    public bool CanDeliverWorldItem(ItemTypeId itemType) => _materializer.CanMaterialize(itemType);

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaKingSlimeLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!_materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized) ||
            !_leases.TryLease(in materialized, slotLeaseTicks, out WorldItemDropReservation reservation))
        {
            return false;
        }

        TerrariaWorldItemDropState wireState = RuntimeWorldItemReplicationRegistry.MapDrop(reservation.Slot, in materialized);
        if (TerrariaWorldItemFrameEncoder.TryEncodeInstancedDrop(in wireState, out ReadOnlyMemory<byte> frame) !=
            TerrariaWorldItemFrameEncodeResult.Encoded)
        {
            _leases.TryCancel(in reservation);
            return false;
        }

        for (int index = 0; index < recipients.Length; index++)
        {
            if (_replication.TrySendInstanced(recipients[index].Slot, frame))
                continue;

            // A failed addressed enqueue can be partially observable just like a socket failure in vanilla. Keep the
            // leased slot alive rather than making it reusable while a client may still own the item copy.
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
        return _materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized) &&
               _worldItems.TryAllocateDrop(in materialized, out _);
    }
}
