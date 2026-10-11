using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal readonly record struct RemotePhaseInventoryMutation(
    ConnectionHandle Connection, short Slot, RuntimePlayerInventoryItem Before, RuntimePlayerInventoryItem After)
{
    internal bool ConsumeFundedQuantity { get; init; }
    internal RuntimePlayerInventoryStore.CursorMovePreparation? CursorSourceDebit {get;init;}
}

internal readonly record struct OwnedInventoryQuantity1458(short Slot, ItemTypeId ItemType, short Maximum);

internal sealed partial class RuntimePlayerInventoryStore
{
    // Fixed admitted producer identities; client reports cannot retire a consumed quantity obligation.
    private static ReadOnlySpan<int> OwnedQuantityTypes => [109, 110, 189, 500, 2209];
    private readonly short[] ownedQuantityCaps = CreateQuantityCaps();
    private static short[] CreateQuantityCaps()
    {
        var caps = new short[PlayerSlotCount * InventorySlotCount * 5];
        Array.Fill(caps, (short)-1);
        return caps;
    }
    private static int QuantityLane(ItemTypeId type) => OwnedQuantityTypes.IndexOf(type.Value);
    private static int QuantityOffset(int player, int slot, int lane) => (player * InventorySlotCount + slot) * 5 + lane;

    internal bool CanAcceptOwnedQuantityReport(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
        if (request.SlotId is < 0 or >= InventorySlotCount || !request.TryGetCanonicalItemType(out var type)) return true;
        int lane = QuantityLane(type);
        if (lane < 0) return true;
        if (!connection.IsAssigned || !CanAccept(connection)) return false;
        short cap = ownedQuantityCaps[QuantityOffset(connection.Player.Slot.Value, request.SlotId, lane)];
        return cap < 0 || request.Stack <= cap;
    }

    internal bool HasOwnedManaPotionQuantity(ConnectionHandle connection)
    {
        if (!connection.IsAssigned || connections[connection.Player.Slot.Value] != connection) return false;
        for (int slot = 0; slot < InventorySlotCount; slot++)
        for (int lane = 1; lane < 5; lane++)
            if (ownedQuantityCaps[QuantityOffset(connection.Player.Slot.Value, slot, lane)] >= 0) return true;
        return false;
    }

    internal OwnedInventoryQuantity1458[] CaptureOwnedQuantities(ConnectionHandle connection)
    {
        var result = new List<OwnedInventoryQuantity1458>();
        if (!connection.IsAssigned || connections[connection.Player.Slot.Value] != connection) return [];
        for (short slot = 0; slot < InventorySlotCount; slot++)
        for (int lane = 0; lane < 5; lane++)
        {
            short cap = ownedQuantityCaps[QuantityOffset(connection.Player.Slot.Value, slot, lane)];
            if (cap >= 0) result.Add(new(slot, new(OwnedQuantityTypes[lane]), cap));
        }
        return result.ToArray();
    }

    private static bool IsValidOwnedQuantityMaximum(ItemTypeId type, int maximum) =>
        maximum >= 0 && (VanillaCoinFacts.TryGetValue(type,out _)
            ? maximum == 0 || VanillaCoinFacts.IsValidRetainedStack(type,maximum)
            : VanillaWorldItemAllocationCatalog1458.TryGet(type.Value,out var facts) && maximum <= facts.MaximumStack);

    internal static bool AreValidOwnedQuantities(ReadOnlySpan<OwnedInventoryQuantity1458> caps, ReadOnlySpan<RuntimePlayerInventoryItem> inventory)
    {
        if (inventory.Length != InventorySlotCount || caps.Length > InventorySlotCount * 5) return false;
        Span<bool> seen = stackalloc bool[InventorySlotCount * 5];
        seen.Clear();
        foreach (var cap in caps)
        {
            int lane = QuantityLane(cap.ItemType);
            if (cap.Slot is < 0 or >= InventorySlotCount || lane < 0 || cap.Maximum < 0 || !IsValidOwnedQuantityMaximum(cap.ItemType, cap.Maximum) || seen[cap.Slot * 5 + lane]) return false;
            if (inventory[cap.Slot].ItemType == cap.ItemType && inventory[cap.Slot].Stack > cap.Maximum) return false;
            seen[cap.Slot * 5 + lane] = true;
        }
        return true;
    }

    internal bool CanAttachOwnedQuantities(int count) => count == 0 || Serial < ulong.MaxValue - 3;

    internal void AdoptOwnedQuantities(ConnectionHandle connection, ReadOnlySpan<OwnedInventoryQuantity1458> caps)
    {
        foreach (var cap in caps)
            ownedQuantityCaps[QuantityOffset(connection.Player.Slot.Value, cap.Slot, QuantityLane(cap.ItemType))] = cap.Maximum;
        if (!caps.IsEmpty) Changed(connection.Player.Slot.Value);
    }

    private void ClearOwnedQuantities(int player) =>
        ownedQuantityCaps.AsSpan(player * InventorySlotCount * 5, InventorySlotCount * 5).Fill(-1);

    // One journal for the complete UpdatePlayers census. All validation and allocation precede
    // the first owner write; one serial advance covers its bounded, distinct physical slots.
    internal bool TryPrepareRemotePhase(ReadOnlySpan<RemotePhaseInventoryMutation> mutations,
        ulong expectedSerial, out RemotePhasePreparation? preparation)
    {
        preparation = null;
        if (expectedSerial == ulong.MaxValue ||
            (mutations.Length != 0 && expectedSerial >= ulong.MaxValue - 1) || Serial != expectedSerial ||
            mutations.Length > (PlayerSlotCount - 1) * 58) return false;
        var seen = new HashSet<int>();
        foreach (var mutation in mutations)
        {
            if (mutation.Connection.Player.Slot.Value == byte.MaxValue || mutation.Slot is < 0 or >= 58 ||
                !IsOwnedConsumption(mutation) || !TryGet(mutation.Connection, mutation.Slot, out var before) ||
                before != mutation.Before || !CanConsumeOwnedQuantity(mutation) || !seen.Add(GetOffset(mutation.Connection.Player.Slot.Value, mutation.Slot)))
                return false;
        }
        var frozenSourceFunding=new short[mutations.Length];
        for(int index=0;index<mutations.Length;index++)
            if(mutations[index].CursorSourceDebit is { } source &&
                !source.TryCaptureSelectedPotionDebit(this,mutations[index].Connection,out _,out frozenSourceFunding[index])) return false;
        preparation = new(this, expectedSerial, mutations.ToArray(),frozenSourceFunding);
        return true;
    }

    private static bool IsOwnedConsumption(in RemotePhaseInventoryMutation mutation)
    {
        var before = mutation.Before;
        if (mutation.CursorSourceDebit is not null) return mutation.ConsumeFundedQuantity && mutation.After == before;
        if (!before.IsCanonical || before.IsEmpty || before.Prefix.Value != 0 ||
            (QuantityLane(before.ItemType) < 0 && !mutation.ConsumeFundedQuantity))
            return false;
        RuntimePlayerInventoryItem expected = before.Stack == 1 ? default : before with { Stack = (short)(before.Stack - 1) };
        return mutation.After == expected;
    }

    private bool CanConsumeOwnedQuantity(in RemotePhaseInventoryMutation mutation)
    {
        if (mutation.CursorSourceDebit is { } source)
            return source.Slot == mutation.Slot && source.TryCaptureSelectedPotionDebit(this,mutation.Connection,out _,out _);
        if (mutation.ConsumeFundedQuantity &&
            (!VanillaSelectedConsumableCatalog1458.TryGet(mutation.Before.ItemType, out var potion) ||
             potion.HealLife <= 0 && potion.HealMana <= 0 && potion.BuffType <= 0 ||
             fundedQuantities[GetOffset(mutation.Connection.Player.Slot.Value, mutation.Slot)] <= 0)) return false;
        int lane = QuantityLane(mutation.CursorSourceDebit is { } paidSource ? paidSource.BeforePotionType : mutation.Before.ItemType);
        if (lane < 0) return true;
        short cap = ownedQuantityCaps[QuantityOffset(mutation.Connection.Player.Slot.Value, mutation.Slot, lane)];
        return cap < 0 || mutation.Before.Stack <= cap;
    }

    internal sealed class RemotePhasePreparation(RuntimePlayerInventoryStore owner,
        ulong serial, RemotePhaseInventoryMutation[] mutations, short[] frozenSourceFunding)
    {
        private bool adopted;
        internal bool IsCurrent
        {
            get
            {
                if (adopted || owner.Serial != serial) return false;
                foreach (var mutation in mutations)
                    if (!owner.TryGet(mutation.Connection, mutation.Slot, out var item) || item != mutation.Before || !owner.CanConsumeOwnedQuantity(mutation))
                        return false;
                return true;
            }
        }

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            adopted = true;
            for(int index=0;index<mutations.Length;index++)
            {
                var mutation=mutations[index];
                if (mutation.CursorSourceDebit is { } source)
                {
                    owner.fundedQuantities[GetOffset(mutation.Connection.Player.Slot.Value,mutation.Slot)]=(short)(frozenSourceFunding[index]-1);
                    source.AdoptSelectedPotionDebit();
                }
                else if (mutation.ConsumeFundedQuantity)
                    owner.fundedQuantities[GetOffset(mutation.Connection.Player.Slot.Value, mutation.Slot)]--;
                owner.PreserveReportedFunding(mutation.Connection.Player.Slot.Value, mutation.Slot, mutation.After);
                owner.items[GetOffset(mutation.Connection.Player.Slot.Value, mutation.Slot)] = mutation.After;
                int lane = QuantityLane(mutation.CursorSourceDebit is { } paidSource ? paidSource.BeforePotionType : mutation.Before.ItemType);
                if (lane >= 0 && (mutation.CursorSourceDebit is not null || mutation.After.Stack < mutation.Before.Stack) &&
                    (mutation.After.IsEmpty || mutation.After.ItemType == mutation.Before.ItemType))
                {
                    int offset = QuantityOffset(mutation.Connection.Player.Slot.Value, mutation.Slot, lane);
                    short after = (short)Math.Max(0, (int)mutation.After.Stack);
                    short prior = owner.ownedQuantityCaps[offset];
                    owner.ownedQuantityCaps[offset] = prior < 0 ? after : Math.Min(prior, after);
                }
            }
            if (mutations.Length != 0)
            {
                owner.Changed();
                Span<bool> stamped = stackalloc bool[PlayerSlotCount];
                stamped.Clear();
                foreach (var mutation in mutations)
                {
                    int player = mutation.Connection.Player.Slot.Value;
                    if (stamped[player]) continue;
                    stamped[player] = true;
                    owner.playerMutationStamps[player] = owner.Serial;
                }
            }
            return true;
        }

        internal bool IsAcceptedItemCurrent(int index)
        {
            if (!adopted || (uint)index >= (uint)mutations.Length) return false;
            var mutation = mutations[index];
            return owner.TryGet(mutation.Connection, mutation.Slot, out var item) && item == mutation.After;
        }
    }
}
