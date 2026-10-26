using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private void CaptureManaCrystalIntent(RuntimePlayerMember member, in PlayerMovementCommitRequest request)
    {
        if (member.ItemPhase is not { } phase) return;
        CaptureTorchFavorIntent(member, in request, ref phase);
        CapturePermanentUpgradeIntent(member, in request, ref phase);
        // Releasing or changing selection retires only the unfinished local life
        // action. The permanent custody guard and exact delayed ACK explanation
        // survive; imported animation reports cannot revive this paid intent.
        if (((request.ControlFlags & 32) == 0 || request.SelectedItem != member.SelectedItem) &&
            phase.SelectedPotionUse is { Completed: false } interruptedLife &&
            interruptedLife.Before.ItemType.Value is 29 or 1291)
            phase = phase with
            {
                SelectedPotionUse = interruptedLife with { Completed = true, DeniedLifeUse = true }
            };
        inventory.TryGet(member.Connection, request.SelectedItem, out var lifeItem);
        if (lifeItem.IsEmpty && phase.SelectedPotionUse is { } beforeUse &&
            beforeUse.Player == member.Connection.Player && beforeUse.Slot == request.SelectedItem &&
            beforeUse.Before.ItemType.Value is 29 or 1291 && beforeUse.After == lifeItem)
            lifeItem = beforeUse.Before;
        // Capture the source-local selected action, not a maximum inferred from packet16.
        // Payment remains a separate journal preflight; q0 attempts also bind this guard.
        if ((request.ControlFlags & 32) != 0 && (request.MiscFlags2 & 64) != 0 &&
            request.SelectedItem == member.SelectedItem && !member.IsDead && member.HasHealth &&
            phase.Selected.ReleaseUseItem && phase.Selected.Animation == 0 && phase.Selected.ItemTime == 0 &&
            playerUpdateRandomOwned &&
            lifeItem.IsCanonical && !lifeItem.IsEmpty && lifeItem.Prefix.Value == 0 &&
            lifeItem.ItemType.Value is 29 or 1291)
        {
            member.OwnsLifeMaximum = true;
            // The permanent custody flag is not a use certificate. Retain this
            // exact fresh local input in the existing selected-use envelope.
            if (phase.SelectedPotionUse is not { Completed: false } retainedLife ||
                retainedLife.Player != member.Connection.Player || retainedLife.Slot != request.SelectedItem ||
                retainedLife.Before != lifeItem)
            {
                int increase = PermanentLifeIncrease(lifeItem.ItemType, member.BaseLifeMax);
                var afterLife = lifeItem.Stack == 1 ? default : lifeItem with { Stack = (short)(lifeItem.Stack - 1) };
                bool fundedLife = inventory.CaptureFundedQuantities(member.Connection)[request.SelectedItem] > 0;
                phase = phase with
                {
                    SelectedPotionUse = new(member.Connection.Player, phase.SelectedPotionUse?.Attempt ?? 0,
                        request.SelectedItem, lifeItem, afterLife, member.NpcHealth?.Life ?? member.Life,
                        member.Mana, member.BaseLifeMax, phase.BaseManaMaximum, phase.Selected)
                    {
                        Completed = !fundedLife, DeniedLifeUse = !fundedLife,
                        AnticipatedLife = (member.NpcHealth?.Life ?? member.Life) + increase,
                        AnticipatedBaseLifeMaximum = member.BaseLifeMax + increase,
                        AnticipatedBuffTypes = transferProfiles.CaptureBuffState(member.Connection)?.CaptureTypes()
                    }
                };
            }
        }

        if ((request.ControlFlags & 32) == 0 || request.SelectedItem != member.SelectedItem)
            phase = phase with { ManaCrystalIntent = null };
        if ((request.ControlFlags & 32) != 0 && (request.MiscFlags2 & 64) != 0 &&
            (request.MovementFlags & 64) == 0 && !member.IsDead && member.HasHealth && member.Life > 0 &&
            member.HasMana && phase.ManaCrystalIntent is null && phase.Selected.ReleaseUseItem &&
            phase.Selected.Animation == 0 && phase.Selected.ItemTime == 0 &&
            phase.BaseManaMaximum is > 0 and <= 400 && playerUpdateRandomOwned && playerUpdateWorld is { } world &&
            !VanillaPlayerHealthContext1458.IsRemoteOutOfRange(request.PositionX, request.PositionY, 20, 42,
                world.MaxTilesX, world.MaxTilesY) &&
            transferProfiles.CaptureBuffState(member.Connection) is { PreventsFreshItemUse: false } &&
            transferProfiles.TryCapture(member.Connection, out var appearance, out _, out var types) &&
            appearance is not null && types is not null &&
            inventory.TryGet(member.Connection, request.SelectedItem, out var selected) && selected.IsCanonical &&
            selected.Stack > 0 && VanillaManaCrystalUse1458.IsSupported(selected.ItemType, selected.Prefix))
        {
            member.OwnsManaMaximum = true;
            phase = phase with
            {
                ManaCrystalIntent = new(member.Connection.Player, request.SelectedItem, selected,
                    phase.BaseManaMaximum, member.Mana, phase.Mana.Maximum, phase.Selected, false, false, false)
            };
        }
        member.ItemPhase = phase;
    }

    private bool TryAcknowledgeManaCrystalInventory(PlayerEquipmentRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member) ||
            member.ItemPhase is not { ManaCrystalIntent: { } intent } phase ||
            intent.Player != command.Connection.Player || command.Request.SlotId != intent.Slot) return false;
        var request = command.Request;
        if (request == intent.After.ToCommitRequest(command.Connection.Player.Slot, intent.Slot))
        {
            member.ItemPhase = phase with { ManaCrystalIntent = intent with { InventoryAcknowledged = true } };
            AppliedEquipmentUpdates++;
            return true;
        }
        // An actual edited item takes the ordinary receive path and invalidates this use intent.
        member.ItemPhase = phase with { ManaCrystalIntent = null };
        return false;
    }

    private bool TryAcknowledgeManaCrystalMana(PlayerManaRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member) ||
            member.ItemPhase is not { ManaCrystalIntent: { } intent } phase ||
            intent.Player != command.Connection.Player) return false;
        int increase = intent.BaseMaximum < 200 ? 20 : 0;
        int acknowledgedMana = intent.Completed ? member.Mana : intent.Mana + increase;
        if (command.Request.PlayerSlot == member.Slot && command.Request.MaxMana == intent.BaseMaximum + increase &&
            command.Request.Mana == acknowledgedMana)
        {
            member.ItemPhase = phase with { ManaCrystalIntent = intent with { ManaAcknowledged = true } };
            AppliedManaUpdates++;
        }
        else RejectedManaUpdates++;
        return true;
    }

    private bool TryPlanManaCrystal(RemoteItemPhasePlan1458 plan, RuntimePlayerInventoryItem selected,
        RuntimePlayerItemPhase1458 item, PlayerSelectedConsumableState1458 clocks,
        in PlayerSelectedConsumableFacts1458 facts, VanillaUnifiedRandom1458 random,
        out PlayerSelectedConsumableTransition1458 use, out RuntimePlayerItemPhase1458 nextItem)
    {
        use = default;
        nextItem = item;
        if (!VanillaManaCrystalUse1458.TryStep(clocks, in facts, item.BaseManaMaximum, selected.Stack,
            random.Next, random.NextDouble, out var result)) return false;
        use = result.Use;
        if (!result.Consumed)
        {
            if (item.ManaCrystalIntent is { } capped && item.BaseManaMaximum >= 200)
                nextItem = item with { ManaCrystalIntent = capped with { Completed = true } };
            return true;
        }
        if (item.ManaCrystalIntent is not { Completed: false } captured ||
            captured.Player != plan.Member.Connection.Player || captured.Slot != plan.Member.SelectedItem ||
            captured.Item != selected || captured.BaseMaximum != item.BaseManaMaximum ||
            !plan.BeforeOwnsManaMaximum || plan.Buffs.HasNoItemsEffect) return false;
        var after = selected.Stack == 1 ? default : selected with { Stack = (short)(selected.Stack - 1) };
        plan.InventoryMutations.Add(new(plan.Member.Connection, plan.Member.SelectedItem, selected, after));
        plan.PublishMana = true;
        nextItem = item with
        {
            BaseManaMaximum = result.BaseMaximum,
            ManaCrystalIntent = item.ManaCrystalIntent is { } intent
                ? intent with { Completed = true } : new(plan.Member.Connection.Player, plan.Member.SelectedItem,
                    selected, item.BaseManaMaximum, facts.Mana, facts.ManaMaximum, clocks, true, false, false),
            Mana = item.Mana with { Maximum = result.DerivedMaximum },
            MagicPayment = item.MagicPayment is { } payment
                ? payment with { EligibleBirth = false, BirthConsumed = true, AwaitingPhase = false } : null
        };
        return true;
    }
}
