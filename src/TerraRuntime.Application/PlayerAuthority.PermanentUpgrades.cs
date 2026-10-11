using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // The eighth bit represents difficulty byte bit 2; the other bits retain
    // the protocol's seven consumable-unlock positions. This is custody, not
    // another copy of the character's permanent flags.
    private static byte PermanentUpgradeBit(int item) => item switch
    {
        5337 => 1, 5338 => 2, 5339 => 4, 5340 => 8,
        5341 => 16, 5342 => 32, 5326 => 64, 3335 => 128,
        _ => 0
    };

    private static byte PermanentUpgradeFlags(in PlayerAppearanceCommitRequest appearance) =>
        (byte)((appearance.ConsumableUnlockFlags & 127) | ((appearance.DifficultyFlags & 4) != 0 ? 128 : 0));

    private static PlayerAppearanceCommitRequest WithPermanentUpgradeFlags(
        in PlayerAppearanceCommitRequest appearance, byte flags) => appearance with
    {
        ConsumableUnlockFlags = (byte)(flags & 127),
        DifficultyFlags = (byte)((appearance.DifficultyFlags & ~4) | ((flags & 128) != 0 ? 4 : 0))
    };

    private void CapturePermanentUpgradeIntent(RuntimePlayerMember member,
        in PlayerMovementCommitRequest request, ref RuntimePlayerItemPhase1458 phase)
    {
        if (((request.ControlFlags & 32) == 0 || request.SelectedItem != member.SelectedItem) &&
            phase.SelectedPotionUse is { Completed: false, PermanentUpgradeBit: > 0 } interrupted)
            phase = phase with { SelectedPotionUse = interrupted with { Completed = true } };

        if ((request.ControlFlags & 32) == 0 || (request.MiscFlags2 & 64) == 0 ||
            request.SelectedItem != member.SelectedItem || member.IsDead || !member.HasHealth ||
            !playerUpdateRandomOwned || !HasOwnedPickupContext ||
            phase.Selected.Animation != 0 || phase.Selected.ItemTime != 0 || !phase.Selected.ReleaseUseItem ||
            !inventory.TryGet(member.Connection, request.SelectedItem, out var selected) ||
            !selected.IsCanonical || selected.IsEmpty || selected.Prefix.Value != 0 ||
            !transferProfiles.TryCapture(member.Connection, out var appearance, out _, out _) ||
            appearance is not { } before) return;

        byte bit = PermanentUpgradeBit(selected.ItemType.Value);
        if (bit == 0 || (bit == 128 && !combatEquipmentExpertMode)) return;
        byte flags = PermanentUpgradeFlags(in before);
        member.OwnedPermanentUpgradeMask |= bit;
        if ((flags & bit) != 0) return;
        if (phase.SelectedPotionUse is { Completed: false } existing &&
            existing.Player == member.Connection.Player && existing.Before == selected &&
            existing.PermanentUpgradeBit == bit)
        {
            phase = phase with
            {
                SelectedPotionUse = existing with { PermanentLocalInputObserved = true }
            };
            return;
        }

        var after = selected.Stack == 1 ? default : selected with { Stack = (short)(selected.Stack - 1) };
        bool funded = inventory.CaptureFundedQuantities(member.Connection)[request.SelectedItem] > 0;
        phase = phase with
        {
            SelectedPotionUse = new(member.Connection.Player, phase.SelectedPotionUse?.Attempt ?? 0,
                request.SelectedItem, selected, after, member.NpcHealth?.Life ?? member.Life,
                member.Mana, member.BaseLifeMax, phase.BaseManaMaximum, phase.Selected)
            {
                Completed = !funded,
                PermanentUpgradeBit = bit,
                PermanentUpgradeBefore = flags,
                PermanentUpgradeAfter = (byte)(flags | bit),
                PermanentLocalInputObserved = true
            }
        };
    }

    private bool TryPreparePermanentAppearance(RuntimePlayerMember member,
        in PlayerAppearanceCommitRequest incoming, out PlayerAppearanceCommitRequest accepted)
    {
        accepted = incoming;
        if (!transferProfiles.TryCapture(member.Connection, out var stored, out _, out _) ||
            stored is not { } before) return member.OwnedPermanentUpgradeMask == 0;
        byte oldFlags = PermanentUpgradeFlags(in before);
        byte newFlags = PermanentUpgradeFlags(in incoming);
        CaptureReportedPermanentAppearance(member, oldFlags, newFlags);
        byte changedOwned = (byte)((oldFlags ^ newFlags) & member.OwnedPermanentUpgradeMask);
        if (changedOwned == 0) return true;

        if (member.ItemPhase is not { SelectedPotionUse: { PermanentUpgradeBit: > 0 } use } phase ||
            use.Player != member.Connection.Player ||
            newFlags != use.PermanentUpgradeAfter || oldFlags != use.PermanentUpgradeBefore ||
            changedOwned != use.PermanentUpgradeBit) return false;

        // Source sends packet4 before the later inventory synchronization. Its
        // anticipated flag is an acknowledgement candidate, never payment. Keep
        // ordinary cosmetics while the existing owned phase prepares the debit.
        accepted = WithPermanentUpgradeFlags(in incoming, oldFlags);
        member.ItemPhase = phase with
        {
            SelectedPotionUse = use with { PermanentAppearanceAcknowledged = true }
        };
        return true;
    }

    private void CaptureReportedPermanentAppearance(RuntimePlayerMember member, byte beforeFlags, byte afterFlags)
    {
        if (!HasOwnedPickupContext || member.IsDead || !member.HasHealth ||
            member.ItemPhase is not { } phase ||
            phase.Selected.Animation != 0 || phase.Selected.ItemTime != 0 ||
            !inventory.TryGet(member.Connection, member.SelectedItem, out var selected) ||
            !selected.IsCanonical || selected.IsEmpty || selected.Prefix.Value != 0) return;
        byte bit = PermanentUpgradeBit(selected.ItemType.Value);
        if (bit == 0 || (bit == 128 && !combatEquipmentExpertMode) ||
            (beforeFlags & bit) != 0 || afterFlags != (byte)(beforeFlags | bit)) return;
        member.OwnedPermanentUpgradeMask |= bit;
        if (phase.SelectedPotionUse is { Completed: false, PermanentUpgradeBit: > 0 } current &&
            current.Player == member.Connection.Player && current.Slot == member.SelectedItem &&
            current.Before == selected && current.PermanentUpgradeBit == bit &&
            current.PermanentUpgradeBefore == beforeFlags) return;
        var after = selected.Stack == 1 ? default : selected with { Stack = (short)(selected.Stack - 1) };
        // A flag report can precede both debit and movement synchronization. It
        // retains a candidate in the existing envelope without inventing a use
        // command or authorizing local effects. The paid resolver still needs
        // the captured source input and exact conserved inventory explanation.
        member.ItemPhase = phase with
        {
            SelectedPotionUse = new(member.Connection.Player, phase.SelectedPotionUse?.Attempt ?? 0,
                member.SelectedItem, selected, after, member.NpcHealth?.Life ?? member.Life,
                member.Mana, member.BaseLifeMax, phase.BaseManaMaximum, phase.Selected)
            {
                PermanentUpgradeBit = bit,
                PermanentUpgradeBefore = beforeFlags,
                PermanentUpgradeAfter = afterFlags,
                // An ordinary original batch need not repeat packet13: its
                // synchronized held controls already precede packet4. The
                // appearance delta alone never supplies those input facts.
                PermanentLocalInputObserved = playerUpdateRandomOwned &&
                    (member.ControlFlags & 32) != 0 && (member.MiscFlags2 & 64) != 0 &&
                    phase.Selected.ReleaseUseItem
            }
        };
    }

    private bool TryPlanOwnedPermanentUpgrade(RemoteItemPhasePlan1458 plan,
        RuntimePlayerInventoryItem selected, RuntimePlayerItemPhase1458 item,
        PlayerSelectedConsumableState1458 clocks, in PlayerSelectedConsumableFacts1458 facts,
        VanillaUnifiedRandom1458 random, out PlayerSelectedConsumableTransition1458 use,
        out RuntimePlayerItemPhase1458 nextItem)
    {
        nextItem = item;
        use = default;
        if (plan.Appearance is not { } appearance) return false;
        var unlocks = new PlayerPermanentUnlockState1458(PermanentUpgradeFlags(in appearance));
        byte family = PermanentUpgradeBit(selected.ItemType.Value);
        bool currentInput = item.SelectedPotionUse is { Completed: false, PermanentLocalInputObserved: true } held &&
            held.Player == plan.Member.Connection.Player && held.Slot == plan.Member.SelectedItem &&
            held.Before == selected && held.PermanentUpgradeBit == family &&
            held.PermanentUpgradeBefore == unlocks.Flags;
        bool paid = currentInput && inventory.TryPrepareSelectedPotionConsumption(plan.Member.Connection,
            plan.Member.SelectedItem, out _);
        if (!VanillaSelectedConsumable1458.TryStepPermanentUnlock(clocks, in facts, in unlocks,
            selected.Stack, paid, plan.Buffs.CountActive(new(23)) > 0, combatEquipmentExpertMode,
            random.Next, random.NextDouble, out var result)) return false;
        use = result.Use;
        if (!result.Applied) return !use.Consumed;
        if (!paid || !inventory.TryPrepareSelectedPotionConsumption(plan.Member.Connection,
            plan.Member.SelectedItem, out var mutation) || mutation.Before != selected ||
            !use.Consumed || mutation.After.Stack != use.RemainingStack) return false;
        plan.InventoryMutations.Add(mutation);
        plan.AcceptedPermanentAppearance = WithPermanentUpgradeFlags(in appearance, result.Unlocks.Flags);
        var previous = item.SelectedPotionUse!.Value;
        nextItem = item with
        {
            SelectedPotionUse = previous with
            {
                Completed = true,
                After = mutation.After,
                Clocks = use.State,
                Life = use.Life,
                Mana = use.Mana
            }
        };
        return true;
    }
}
