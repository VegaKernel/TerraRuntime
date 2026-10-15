using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private RuntimeNpcDeathPrelude1458? torchFavorPrelude;
    internal void SetTorchFavorPrelude(RuntimeNpcDeathPrelude1458 prelude) => torchFavorPrelude = prelude;

    private void CaptureTorchFavorIntent(RuntimePlayerMember member, in PlayerMovementCommitRequest request,
        ref RuntimePlayerItemPhase1458 phase)
    {
        if (((request.ControlFlags & 32) == 0 || request.SelectedItem != member.SelectedItem) &&
            phase.SelectedPotionUse is { TorchFavor: true, Completed: false } stale)
            phase = phase with { SelectedPotionUse = stale with { Completed = true } };
        if ((request.ControlFlags & 32) == 0 || (request.MiscFlags2 & 64) == 0 ||
            request.SelectedItem != member.SelectedItem || member.IsDead || !member.HasHealth ||
            !playerUpdateRandomOwned || !HasOwnedPickupContext ||
            phase.Selected.Animation != 0 || phase.Selected.ItemTime != 0 || !phase.Selected.ReleaseUseItem ||
            !inventory.TryGet(member.Connection, request.SelectedItem, out var selected) ||
            selected.IsEmpty || selected.ItemType.Value != 5043 || selected.Prefix.Value != 0 ||
            !transferProfiles.TryCapture(member.Connection, out var appearance, out _, out _) || appearance is not { } before)
            return;
        member.OwnsTorchFavor = true;
        if ((before.TorchAndCartFlags & 4) != 0) return;
        var after = selected.Stack == 1 ? default : selected with { Stack = (short)(selected.Stack - 1) };
        phase = phase with { SelectedPotionUse = new(member.Connection.Player, phase.SelectedPotionUse?.Attempt ?? 0,
            request.SelectedItem, selected, after, member.NpcHealth?.Life ?? member.Life, member.Mana,
            member.BaseLifeMax, phase.BaseManaMaximum, phase.Selected)
        { TorchFavor = true, TorchFlagsBefore = before.TorchAndCartFlags, PermanentLocalInputObserved = true,
          Completed = inventory.CaptureFundedQuantities(member.Connection)[request.SelectedItem] == 0 } };
    }

    private bool TryPrepareTorchFavorAppearance(RuntimePlayerMember member, in PlayerAppearanceCommitRequest incoming,
        out PlayerAppearanceCommitRequest accepted)
    {
        accepted = incoming;
        if (!transferProfiles.TryCapture(member.Connection, out var stored, out _, out _) || stored is not { } before)
            return !member.OwnsTorchFavor;
        bool unlocked = (before.TorchAndCartFlags & 4) != 0;
        bool reported = (incoming.TorchAndCartFlags & 4) != 0;
        if (!unlocked && reported && HasOwnedPickupContext && !member.IsDead && playerUpdateRandomOwned &&
            member.ItemPhase is { } phase && phase.Selected.Animation == 0 && phase.Selected.ItemTime == 0 &&
            inventory.TryGet(member.Connection, member.SelectedItem, out var selected) &&
            selected.ItemType.Value == 5043 && selected.Stack > 0 && selected.Prefix.Value == 0)
        {
            member.OwnsTorchFavor = true;
            if (phase.SelectedPotionUse is not { TorchFavor: true, Completed: false } held || held.Before != selected ||
                held.Player != member.Connection.Player || held.Slot != member.SelectedItem)
            {
                var after = selected.Stack == 1 ? default : selected with { Stack = (short)(selected.Stack - 1) };
                member.ItemPhase = phase with { SelectedPotionUse = new(member.Connection.Player,
                    phase.SelectedPotionUse?.Attempt ?? 0, member.SelectedItem, selected, after,
                    member.NpcHealth?.Life ?? member.Life, member.Mana, member.BaseLifeMax, phase.BaseManaMaximum, phase.Selected)
                { TorchFavor = true, TorchFlagsBefore = before.TorchAndCartFlags,
                  PermanentLocalInputObserved = (member.ControlFlags & 32) != 0 && (member.MiscFlags2 & 64) != 0 &&
                    phase.Selected.ReleaseUseItem } };
            }
        }
        if (!member.OwnsTorchFavor || unlocked == reported) return true;
        if (unlocked || !reported || member.ItemPhase is not { SelectedPotionUse: { TorchFavor: true } use } current ||
            use.Player != member.Connection.Player || (incoming.TorchAndCartFlags & 5) != 5 ||
            (use.TorchFlagsBefore & 4) != 0) return false;
        // Source4 precedes its selected debit. Preserve independent torch/cart
        // presentation bits while the unlock and automatic enable await payment.
        accepted = incoming with { TorchAndCartFlags = (byte)((incoming.TorchAndCartFlags & ~5) | (before.TorchAndCartFlags & 5)) };
        member.ItemPhase = current with { SelectedPotionUse = use with { PermanentAppearanceAcknowledged = true } };
        return true;
    }

    private bool TryPlanOwnedTorchFavor(RemoteItemPhasePlan1458 plan, RuntimePlayerInventoryItem selected,
        RuntimePlayerItemPhase1458 item, PlayerSelectedConsumableState1458 clocks,
        in PlayerSelectedConsumableFacts1458 facts, VanillaUnifiedRandom1458 random,
        out PlayerSelectedConsumableTransition1458 use, out RuntimePlayerItemPhase1458 nextItem)
    {
        use = default; nextItem = item;
        if (plan.Appearance is not { } appearance) return false;
        bool local = item.SelectedPotionUse is { TorchFavor: true, Completed: false, PermanentLocalInputObserved: true } held &&
            held.Player == plan.Member.Connection.Player && held.Slot == plan.Member.SelectedItem && held.Before == selected &&
            (appearance.TorchAndCartFlags & 4) == 0;
        bool paid = local && inventory.TryPrepareSelectedPotionConsumption(plan.Member.Connection, plan.Member.SelectedItem, out _);
        if (!VanillaSelectedConsumable1458.TryStepTorchGodFavor(clocks, in facts, selected.Stack,
            (appearance.TorchAndCartFlags & 4) != 0, (appearance.TorchAndCartFlags & 1) != 0,
            paid, plan.Buffs.CountActive(new(23)) > 0, random.Next, random.NextDouble, out var result)) return false;
        use = result.Use;
        if (!result.Applied)
        {
            if (local && !paid)
                nextItem = item with { SelectedPotionUse = item.SelectedPotionUse!.Value with { Completed = true } };
            return !use.Consumed;
        }
        if (!paid || !inventory.TryPrepareSelectedPotionConsumption(plan.Member.Connection, plan.Member.SelectedItem,
                out var mutation) || mutation.Before != selected || !use.Consumed || mutation.After.Stack != use.RemainingStack)
            return false;
        plan.InventoryMutations.Add(mutation);
        plan.AcceptedPermanentAppearance = appearance with { TorchAndCartFlags = (byte)(appearance.TorchAndCartFlags | 5) };
        plan.CreditTorchFavor = true;
        nextItem = item with { SelectedPotionUse = item.SelectedPotionUse!.Value with
            { Completed = true, After = mutation.After, Clocks = use.State, Life = use.Life, Mana = use.Mana } };
        return true;
    }
}
