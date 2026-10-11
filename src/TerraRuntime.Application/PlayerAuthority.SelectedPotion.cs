using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

// Paid local-use epoch retained by the existing item phase, including its
// provisional inventory report and exact delayed vitals and buff acknowledgements.
internal readonly record struct RuntimePlayerSelectedPotionUse1458(
    PlayerHandle Player, ulong Attempt, byte Slot, RuntimePlayerInventoryItem Before,
    RuntimePlayerInventoryItem After, int Life, int Mana, int? BaseLifeMaximum,
    int BaseManaMaximum, PlayerSelectedConsumableState1458 Clocks)
{
    internal bool Completed { get; init; }
    internal bool InventoryAcknowledged { get; init; }
    internal BuffTypeId[]? ExpectedBuffTypes { get; init; }
    internal BuffTypeId[]? AnticipatedBuffTypes { get; init; }
    internal int? AnticipatedLife { get; init; }
    internal int? AnticipatedMana { get; init; }
}

internal sealed partial class PlayerAuthority
{
    private bool selectedPotionRawFallback;
    private bool TryPlanOwnedSelectedPotion(RemoteItemPhasePlan1458 plan,
        RuntimePlayerInventoryItem selected, RuntimePlayerItemPhase1458 item,
        PlayerSelectedConsumableState1458 clocks, in PlayerSelectedConsumableFacts1458 facts,
        VanillaUnifiedRandom1458 random, out PlayerSelectedConsumableTransition1458 use,
        out RuntimePlayerItemPhase1458 nextItem)
    {
        nextItem = item;
        RuntimePlayerInventoryStore.CursorMovePreparation? sourceDebit = null;
        RemotePhaseInventoryMutation admittedDebit;
        if (item.SelectedPotionUse is { Completed: false } held && held.Player == plan.Member.Connection.Player &&
            held.Slot == plan.Member.SelectedItem && plan.Member.CursorMove?.Inventory is { } candidate &&
            candidate.TryCaptureSelectedPotionDebit(inventory, plan.Member.Connection, out var original, out _) && original == held.Before)
        {
            sourceDebit = candidate;
            inventory.TryGet(plan.Member.Connection, plan.Member.SelectedItem, out var physical);
            admittedDebit = new(plan.Member.Connection, plan.Member.SelectedItem, physical, physical)
            { ConsumeFundedQuantity = true, CursorSourceDebit = candidate };
        }
        else if (!inventory.TryPrepareSelectedPotionConsumption(plan.Member.Connection,
            plan.Member.SelectedItem, out admittedDebit))
        {
            // No payment means no local effect. Clock-only progression keeps this
            // actor and the rest of the owned census live with the supported idle RNG.
            // An already-paid animation keeps its genuine visual/clock/RNG tail.
            // The effective use prohibition prevents a new idle begin; any effect
            // at an imported active clock is stripped because no unit can pay it.
            var deniedFacts = facts with { Cursed = true };
            if (!VanillaSelectedConsumable1458.TryStepOwned(clocks, in deniedFacts,
                selected.Stack, random.Next, random.NextDouble, out use)) return false;
            use = use with
            {
                Life = facts.Life,
                Mana = facts.Mana,
                BeganUse = false,
                PotionSicknessOffer = 0,
                ManaSicknessOffer = 0,
                Consumed = false,
                RemainingStack = selected.Stack
            };
            return true;
        }
        if (!VanillaSelectedConsumable1458.TryStepOwned(clocks, in facts, selected.Stack,
            random.Next, random.NextDouble, out use)) return false;
        if (!use.Consumed) return true;
        if (item.SelectedPotionUse is { Attempt: ulong.MaxValue } ||
            (sourceDebit is null && admittedDebit.Before != selected) ||
            Math.Max(0, (int)admittedDebit.After.Stack) != use.RemainingStack) return false;
        var debit = admittedDebit;
        plan.InventoryMutations.Add(debit);
        plan.PublishHealth = use.Life != facts.Life;
        plan.PublishMana = use.Mana != facts.Mana;
        plan.PublishBuffs |= use.PotionSicknessOffer > 0 || use.ManaSicknessOffer > 0;
        var expectedBuffs = plan.Buffs.Clone();
        if (use.PotionSicknessOffer > 0) expectedBuffs.TryApplySelectedConsumable(new(21), use.PotionSicknessOffer, false);
        if (use.ManaSicknessOffer > 0) expectedBuffs.TryApplySelectedConsumable(new(94), use.ManaSicknessOffer, false);
        var pendingEpoch = item.SelectedPotionUse is { Completed: false } heldEpoch &&
            heldEpoch.Slot == plan.Member.SelectedItem && heldEpoch.Before == selected ? heldEpoch : (RuntimePlayerSelectedPotionUse1458?)null;
        nextItem = item with
        {
            SelectedPotionUse = new(plan.Member.Connection.Player,
            (item.SelectedPotionUse?.Attempt ?? 0) + 1, plan.Member.SelectedItem,
            selected, debit.After, use.Life, use.Mana, plan.Member.BaseLifeMax,
            item.BaseManaMaximum, use.State)
            {
                Completed = true,
                ExpectedBuffTypes = expectedBuffs.CaptureTypes(),
                AnticipatedBuffTypes = pendingEpoch?.AnticipatedBuffTypes,
                AnticipatedLife = pendingEpoch?.AnticipatedLife ?? use.Life,
                AnticipatedMana = pendingEpoch?.AnticipatedMana ?? use.Mana,
                InventoryAcknowledged = item.SelectedPotionUse is { Completed: false } pending && pending.Slot == plan.Member.SelectedItem && pending.Before == selected && pending.InventoryAcknowledged
            }
        };
        return true;
    }
    private bool TryAcknowledgeSelectedPotionInventory(PlayerEquipmentRuntimeCommand command)
    {
        if (selectedPotionRawFallback || !membership.TryGet(command.Connection, out var member) || member.ItemPhase is not { } phase ||
            command.Request.SlotId != member.SelectedItem) return false;
        bool canBeginReportedUse = inventory.TryPrepareSelectedPotionConsumption(command.Connection,
            member.SelectedItem, out var debit) &&
            command.Request == debit.After.ToCommitRequest(member.Slot, member.SelectedItem) &&
            !member.IsDead && member.HasHealth && member.HasMana &&
            phase.Selected.Animation == 0 && phase.Selected.ItemTime == 0 && phase.Selected.ReleaseUseItem;
        if (phase.SelectedPotionUse is { } held && held.Player == command.Connection.Player)
        {
            if (command.Request == held.After.ToCommitRequest(member.Slot, held.Slot) &&
                (!held.Completed || !canBeginReportedUse))
            {
                member.ItemPhase = phase with { SelectedPotionUse = held with { InventoryAcknowledged = true } };
                AppliedEquipmentUpdates++;
                return true;
            }
            // An edited item retires only an unfinished use alternative. The last
            // paid epoch still explains its exact delayed vitals/buff reports,
            // even after a raw quantity restoration that supplies no new funding.
            if (!held.Completed)
            {
                member.ItemPhase = phase with { SelectedPotionUse = null };
                return false;
            }

        }
        if (!canBeginReportedUse) return false;
        // Exact source debit is provisional. The physical before-image stays owned
        // until the actual item phase authorizes and pays this local use.
        VanillaSelectedConsumableCatalog1458.TryGet(debit.Before.ItemType, out var potion);
        var anticipatedBuffs = transferProfiles.CaptureBuffState(member.Connection);
        if (potion.HealLife > 0) anticipatedBuffs?.TryApplySelectedConsumable(new(21), 3600, false);
        if (potion.HealMana > 0) anticipatedBuffs?.TryApplySelectedConsumable(new(94), 300, false);
        member.ItemPhase = phase with
        {
            SelectedPotionUse = new(member.Connection.Player, phase.SelectedPotionUse?.Attempt ?? 0,
            member.SelectedItem, debit.Before, debit.After, member.NpcHealth?.Life ?? member.Life, member.Mana,
            member.BaseLifeMax, phase.BaseManaMaximum, phase.Selected)
            {
                InventoryAcknowledged = true,
                AnticipatedLife = Math.Min(member.DerivedLifeMax ?? member.MaxLife, (member.NpcHealth?.Life ?? member.Life) + potion.HealLife),
                AnticipatedMana = Math.Min(phase.Mana.Maximum, member.Mana + potion.HealMana),
                AnticipatedBuffTypes = anticipatedBuffs?.CaptureTypes()
            }
        };
        if (CanPrepareOrdinaryCursorAlternative(command.Connection, member, member.SelectedItem, debit.After)) return false;
        AppliedEquipmentUpdates++;
        return true;
    }

    private bool TryAcknowledgeSelectedPotionHealth(PlayerHealthRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member) ||
            member.ItemPhase?.SelectedPotionUse is not { } use || use.Player != command.Connection.Player ||
            command.Request.PlayerSlot != member.Slot) return false;
        if (!VanillaSelectedConsumableCatalog1458.TryGet(use.Before.ItemType, out var potion)) return false;
        int expected = use.Completed ? use.Life : use.AnticipatedLife ?? Math.Min(member.DerivedLifeMax ?? member.MaxLife, use.Life + potion.HealLife);
        if ((command.Request.Life != expected && command.Request.Life != use.AnticipatedLife) || command.Request.MaxLife != use.BaseLifeMaximum) return false;
        AppliedHealthUpdates++;
        return true;
    }

    private bool TryAcknowledgeSelectedPotionMana(PlayerManaRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member) ||
            member.ItemPhase?.SelectedPotionUse is not { } use || use.Player != command.Connection.Player ||
            command.Request.PlayerSlot != member.Slot ||
            !VanillaSelectedConsumableCatalog1458.TryGet(use.Before.ItemType, out var potion)) return false;
        int expected = use.Completed ? use.Mana : use.AnticipatedMana ?? Math.Min(member.MaxMana, use.Mana + potion.HealMana);
        if ((command.Request.Mana != expected && command.Request.Mana != use.AnticipatedMana) || command.Request.MaxMana != use.BaseManaMaximum) return false;
        AppliedManaUpdates++;
        return true;
    }

    private bool IsExplainedSelectedPotionBuffAcknowledgement(RuntimePlayerMember member,
        ReadOnlySpan<BuffTypeId> incoming)
    {
        if (member.ItemPhase?.SelectedPotionUse is not { } use ||
            use.Player != member.Connection.Player) return false;
        if (use.AnticipatedBuffTypes is { } anticipated && incoming.SequenceEqual(anticipated)) return true;
        if (use.Completed && use.ExpectedBuffTypes is { } types) return incoming.SequenceEqual(types);
        var buffs = transferProfiles.CaptureBuffState(member.Connection);
        if (buffs is null) return false;
        if (!use.Completed)
        {
            if (!VanillaSelectedConsumableCatalog1458.TryGet(use.Before.ItemType, out var potion)) return false;
            if (potion.HealLife > 0) buffs.TryApplySelectedConsumable(new(21), 3600, false);
            if (potion.HealMana > 0) buffs.TryApplySelectedConsumable(new(94), 300, false);
        }
        return incoming.SequenceEqual(buffs.CaptureTypes());
    }

    private void CompleteUnpaidSelectedPotionReport(ConnectionHandle connection)
    {
        if (!membership.TryGet(connection, out var member) || member.ItemPhase is not { SelectedPotionUse: { Completed: false } use } phase)
            return;
        if (member.SelectedItem == use.Slot && (member.ControlFlags & 32) != 0 && !member.IsDead &&
            phase.Selected.ReleaseUseItem && phase.Selected.Animation == 0 && phase.Selected.ItemTime == 0 &&
            inventory.TryGet(connection, use.Slot, out var current) && (current == use.Before || member.CursorMove?.Inventory is { } source && source.TryCaptureSelectedPotionDebit(inventory, connection, out var original, out _) && original == use.Before) &&
            transferProfiles.CaptureBuffState(connection) is { } buffs && buffs.CountActive(new(23)) == 0 &&
            VanillaSelectedConsumableCatalog1458.TryGet(use.Before.ItemType, out var potion) &&
            (!potion.HealingDelay || buffs.CountActive(new(21)) == 0 && phase.Selected.PotionDelay == 0)) return;
        member.ItemPhase = phase with { SelectedPotionUse = null };
        var command = new PlayerEquipmentRuntimeCommand(connection,
            use.After.ToCommitRequest(member.Slot, use.Slot));
        selectedPotionRawFallback = true;
        try { ApplyPlayerEquipment(command); }
        finally { selectedPotionRawFallback = false; }
    }

}
