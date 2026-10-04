using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

/// <summary>One materialization and reservation owner shared by all admitted NPC reward families.</summary>
internal sealed class RuntimeNpcLootDelivery1458(
    RuntimeWorldItemStore items,
    RuntimeWorldItemInstancedLeaseStore? leases,
    RuntimeWorldItemReplicationRegistry? replication,
    INpcLootWorldItemMaterializer? materializer = null,
    IRuntimePlayerSlotSnapshotLookup? players = null) :
    IKingSlimeDifficultyLootDeliverySink,
    IEaterOfWorldsLootDeliverySink,
    IBrainOfCthulhuLootDeliverySink,
    ISkeletronLootDeliverySink,
    IQueenBeeLootDeliverySink,
    IDeerclopsLootDeliverySink,
    IQueenSlimeLootDeliverySink,
    IPlanteraLootDeliverySink,
    IGolemLootDeliverySink,
    IMoonLordLootDeliverySink,
    IMechanicalBossLootDeliverySink,
    IEyeOfCthulhuLootDeliverySink,
    IWallOfFleshLootDeliverySink,
    IBossRecoveryLootDeliverySink1458
{
    private readonly RuntimeWorldItemStore worldItems = items ?? throw new ArgumentNullException(nameof(items));
    private readonly INpcLootWorldItemMaterializer ownedMaterializer = materializer ?? VanillaNpcLootWorldItemMaterializer.Instance;

    internal RuntimeNpcDeathDropPlan1458? Preview { get; set; }
    internal NpcDeathDropPhase1458 Phase { get; set; }

    public bool CanDeliverInstanced(ItemTypeId itemType) =>
        leases is not null && replication is not null && ownedMaterializer.CanMaterialize(itemType);

    public bool CanDeliverWorldItem(ItemTypeId itemType) => ownedMaterializer.CanMaterialize(itemType);

    public bool TryDeliverWorldItem(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return TryWorld(in origin, in drop, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaKingSlimeLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaBrainOfCthulhuLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaSkeletronLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaQueenBeeLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaDeerclopsLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaQueenSlimeLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaPlanteraLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaGolemLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaMoonLordLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaMechanicalBossLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaEyeOfCthulhuLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaWallOfFleshLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (recipients.Length > byte.MaxValue)
            return false;

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[recipients.Length];
        for (int index = 0; index < recipients.Length; index++)
            slots[index] = recipients[index].Slot;

        return TryInstanced(in origin, in drop, slots, slotLeaseTicks, random);
    }

    internal bool TryWorld(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
    {
        if (!ownedMaterializer.TryMaterialize(in origin, in drop, random, out var state)) return false;
        return Preview is { } plan ? plan.TryStage(Phase, in state) : worldItems.TryAllocateDrop(in state, out _);
    }

    internal bool TryInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
        ReadOnlySpan<PlayerSlotId> recipients, int ticks, INpcLootRollSource random)
    {
        if (leases is null || replication is null || ticks <= 0 || recipients.Length > byte.MaxValue ||
            !ownedMaterializer.TryMaterialize(in origin, in drop, random, out var state)) return false;
        if (Preview is { } plan)
        {
            var projected = RuntimeWorldItemReplicationRegistry.MapDrop(0, in state);
            if (TerrariaWorldItemFrameEncoder.TryEncodeInstancedDrop(in projected, out _) != TerrariaWorldItemFrameEncodeResult.Encoded) return false;
            Span<PlayerHandle> handles = stackalloc PlayerHandle[recipients.Length];
            for (int i = 0; i < recipients.Length; i++)
            {
                if (players is null || !players.TryGetPlayer(recipients[i], out var player)) return false;
                handles[i] = player.Player;
            }
            return plan.TryStage(Phase, in state, handles, ticks);
        }
        if (!leases.TryLease(in state, ticks, out var reservation)) return false;
        return Send(in reservation, in state, recipients);
    }

    internal bool Adopt(WorldItemDropReservation reservation, WorldItemDropStateUpdate state,
        PlayerHandle[] recipients, int ticks)
    {
        if (leases is null || replication is null || !leases.TryAdoptReservedDrop(in reservation, ticks)) return false;
        Span<PlayerSlotId> current = stackalloc PlayerSlotId[recipients.Length];
        int count = 0;
        foreach (PlayerHandle handle in recipients)
            if (players is not null && players.TryGetPlayer(handle.Slot, out var player) && player.Player == handle)
                current[count++] = handle.Slot;
        // Transport enqueue failure does not roll back an accepted death or make an observed bag slot reusable.
        _ = Send(in reservation, in state, current[..count], cancelOnInvalidFrame: false);
        return true;
    }

    private bool Send(in WorldItemDropReservation reservation, in WorldItemDropStateUpdate state,
        ReadOnlySpan<PlayerSlotId> recipients, bool cancelOnInvalidFrame = true)
    {
        TerrariaWorldItemDropState wire = RuntimeWorldItemReplicationRegistry.MapDrop(reservation.Slot, in state);
        if (TerrariaWorldItemFrameEncoder.TryEncodeInstancedDrop(in wire, out ReadOnlyMemory<byte> frame) != TerrariaWorldItemFrameEncodeResult.Encoded)
        {
            if (cancelOnInvalidFrame) leases!.TryCancel(in reservation);
            return false;
        }
        foreach (PlayerSlotId recipient in recipients)
            if (!replication!.TrySendInstanced(recipient, frame)) return false;
        return true;
    }
}
