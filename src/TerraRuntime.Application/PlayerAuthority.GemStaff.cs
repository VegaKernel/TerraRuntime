using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;
internal sealed partial class PlayerAuthority
{
    internal bool TryPrepareGemStaffPayment(ConnectionHandle connection,
        RuntimePlayerInventoryItem selected,
        out RuntimePlayerMagicPayment1458 payment,
        out int debit,
        out VanillaPlayerCombatSnapshot combat,
        out RuntimePlayerMagicUsePreparation? magicUse)
    {
        payment = default;
        debit = 0;
        combat = default;
        magicUse = null;
        if (!playerUpdateRandomOwned ||
            playerUpdateWorld is not { } world ||
            !membership.TryGet(connection,
            out var member) ||
            member.IsDead ||
            member.ItemPhase is not { DerivedCombat: { } retained } phase ||
            !IsValidDerivedCombat(in retained) ||
            phase.BaseManaMaximum <= 0 ||
            !member.HasMana ||
            member.Mana < 0 ||
            selected.Stack <= 0 ||
            (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0 ||
            VanillaPlayerHealthContext1458.IsRemoteOutOfRange(member.PositionX,
            member.PositionY,
            20,
            42,
            world.MaxTilesX,
            world.MaxTilesY) ||
            !transferProfiles.TryCapture(connection,
            out var appearance,
            out var equipment,
            out _) ||
            !VanillaGemStaff1458.TryResolveWeapon(selected.ItemType,
            selected.Prefix,
            world.WindowsItemPrefixArithmetic,
            out var weapon,
            out _))
            return false;
        bool? extra = appearance is { } owned ? (owned.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag) != 0 : null;
        var context = new VanillaPlayerCombatEquipmentContext(extra, combatEquipmentExpertMode, combatEquipmentMasterMode)
        {
            LocalVanityArmor = combatEquipmentLocalVanityArmor
        };
        if (!VanillaPlayerCombatEquipmentCatalog.TryCaptureEffectiveGemStaffBody(equipment,
            in context,
            out var body) ||
            !VanillaGemStaff1458.TryPackFeatures(selected.ItemType,
            body,
            out var features))
            return false;
        combat = retained;
        var previous = phase.MagicPayment;
        bool same = previous is { GemStaff: true } old &&
            old.IsValid &&
            old.Matches(connection.Player,
            member.SelectedItem,
            selected,
            phase.BaseManaMaximum) &&
            old.EffectiveBody == body &&
            old.GemFeatures == features;
        if (same && previous is { AwaitingPhase: false } paid)
        {
            if (paid.EligibleBirth && !paid.BirthConsumed)
            {
                payment = paid with
                {
                    BirthConsumed = true
                };
                return true;
            }

            if (paid.MidpointEligible && !paid.MidpointConsumed)
            {
                payment = paid with
                {
                    MidpointConsumed = true
                };
                return true;
            }
        }

        if (same && previous is { AwaitingPhase: true })
            return false;
        // A completed receipt is history, not another cast certificate. Only an
        // actual fresh local start boundary may prepare a packet-first payment.
        var clocks = same ? previous!.Value.GemClocks : phase.Selected;
        bool localStart = clocks.Animation == 0 && clocks.ReleaseUseItem || weapon.AutoReuse && clocks.Animation == 1;
        // NoItems blocks a fresh payment, not a source-owned midpoint already paid.
        if (transferProfiles.CountActiveBuffs(connection, new(23)) is not 0 ||
            (member.ControlFlags & 32) == 0 ||
            (member.MiscFlags2 & 64) == 0 ||
            !localStart ||
            previous is { Attempt: ulong.MaxValue } ||
            previous is { } malformed &&
            !malformed.IsValid)
            return false;
        int available = member.Mana;
        int cost = (int)(weapon.ManaCost * retained.ManaCostMultiplier);
        if (cost < 0)
            return false;
        ulong attempt = (previous?.Attempt ?? 0) + 1;
        RuntimePlayerMagicRefill1458? refill = null;
        PlayerBuffState? beforeBuffs = null, afterBuffs = null;
        if (retained.ManaFlower && available < cost)
        {
            beforeBuffs = transferProfiles.CaptureBuffState(connection);
            var inventoryBefore = new RuntimePlayerInventoryItem[59];
            if (beforeBuffs is null || !inventory.TryCopyInventory(connection, inventoryBefore))
                return false;
            afterBuffs = beforeBuffs.Clone();
            if (!TryStageAutoQuickMana(connection, attempt, available, phase.Mana.Maximum, phase.ManaPotionDelay, inventoryBefore, afterBuffs, out available, out refill))
                return false;
        }

        int after = available - Math.Min(available, cost);
        bool slow = available < cost;
        debit = Math.Min(available, cost);
        payment = new(connection.Player, attempt, member.SelectedItem, selected, phase.BaseManaMaximum, after, slow, false, true, true)
        {
            GemStaff = true,
            GemClocks = clocks,
            GemLastUseSuccess = true,
            EffectiveBody = body,
            GemFeatures = features,
            Refill = refill
        };
        if (refill is { } restored)
        {
            var mutations = new[]
            {
                new RemotePhaseInventoryMutation(connection, restored.Slot, restored.Before, restored.After)
                {
                    ConsumeFundedQuantity = true
                }
            };
            if (!inventory.TryPrepareRemotePhase(mutations, inventory.Serial, out var prepared) || prepared is null)
                return false;
            if (!inventory.TryCaptureShopJournal(connection, out var beforeInventory) || beforeInventory is null)
                return false;
            magicUse = new(payment, beforeBuffs!, afterBuffs!, prepared)
            {
                GemInventoryBefore = beforeInventory
            };
        }

        return true;
    }

    internal bool TryRefreshPendingGemUseCapture(RuntimePlayerProjectileUseCapture capture,
        RuntimePlayerMagicPayment1458 proposed,
        RuntimePlayerMagicUsePreparation? prepared,
        out RuntimePlayerProjectileUseCapture current,
        out RuntimePlayerMagicPayment1458 payment)
    {
        current = capture;
        payment = default;
        if (!membership.TryGet(capture.Connection, out var member) || !member.OwnsManaMaximum)
            return false;
        var adjusted = capture with
        {
            OwnsManaMaximum = true
        };
        if (prepared is not null)
        {
            if (!proposed.AwaitingPhase ||
                prepared.Payment != proposed ||
                proposed.Refill is not { } refill ||
                prepared.GemInventoryBefore is not { } before ||
                member.ItemPhase?.MagicPayment is not { } committed ||
                committed.Refill != refill ||
                !transferProfiles.IsCurrentBuffState(capture.Connection,
                prepared.AfterBuffs) ||
                !inventory.TryCaptureShopJournal(capture.Connection,
                out var after) ||
                after is null)
                return false;
            var expectedItems = before.Before.ToArray();
            var expectedQuantity = before.Quantity;
            var expectedCaps = before.Caps;
            int lane = refill.Before.ItemType.Value switch
            {
                110 => 1,
                189 => 2,
                500 => 3,
                2209 => 4,
                _ => -1
            };
            if (lane < 0 || expectedItems[refill.Slot] != refill.Before || expectedQuantity[refill.Slot] <= 0)
                return false;
            expectedItems[refill.Slot] = refill.After;
            expectedQuantity[refill.Slot]--;
            int capIndex = refill.Slot * 5 + lane;
            expectedCaps[capIndex] = expectedCaps[capIndex] < 0 ? refill.After.Stack : Math.Min(expectedCaps[capIndex], refill.After.Stack);
            if (!expectedItems.AsSpan().SequenceEqual(after.Before) ||
                !expectedQuantity.AsSpan().SequenceEqual(after.Quantity) ||
                !expectedCaps.AsSpan().SequenceEqual(after.Caps) ||
                !before.Copper.AsSpan().SequenceEqual(after.Copper))
                return false;
            // The phase has jointly adopted exactly this paid refill. Refresh its
            // serial/profile proof, then consume the existing payment with zero cost.
            adjusted = adjusted with
            {
                InventorySerial = inventory.Serial,
                Buffs = prepared.AfterBuffs.CaptureTypes()
            };
        }

        if (!TryRefreshPendingBulletUseCapture(adjusted, out current))
            return false;
        return TryRebasePendingGemPayment(capture, current, proposed, out payment);
    }

    internal bool TryRebasePendingGemPayment(RuntimePlayerProjectileUseCapture original,
        RuntimePlayerProjectileUseCapture current,
        RuntimePlayerMagicPayment1458 proposed,
        out RuntimePlayerMagicPayment1458 payment)
    {
        payment = default;
        if (!proposed.GemStaff ||
            !proposed.IsValid ||
            original.ItemPhase is not { } before ||
            current.ItemPhase is not { } phase ||
            phase.MagicPayment is not { GemStaff: true, AwaitingPhase: false } owned ||
            !owned.IsValid ||
            owned.Player != proposed.Player ||
            owned.Attempt != proposed.Attempt ||
            owned.Item != proposed.Item ||
            owned.Slot != proposed.Slot ||
            owned.BaseManaMaximum != proposed.BaseManaMaximum ||
            owned.EffectiveBody != proposed.EffectiveBody ||
            owned.GemFeatures != proposed.GemFeatures ||
            owned.SlowMagicUse != proposed.SlowMagicUse)
            return false;
        // Only a phase-owned payment for this exact source attempt can supersede the
        // speculative packet-first payment. No completed/other epoch is transferable.
        if (proposed.AwaitingPhase)
        {
            if (!owned.EligibleBirth || owned.BirthConsumed || owned.Refill != proposed.Refill)
                return false;
            payment = owned with
            {
                BirthConsumed = true
            };
            return true;
        }

        if (before.MagicPayment is not { GemStaff: true } captured ||
            captured.Attempt != owned.Attempt ||
            captured.PaidMana != owned.PaidMana ||
            captured.SlowMagicUse != owned.SlowMagicUse ||
            captured.Refill != owned.Refill)
            return false;
        if (proposed.BirthConsumed && !captured.BirthConsumed)
        {
            if (!owned.EligibleBirth || owned.BirthConsumed)
                return false;
            payment = owned with
            {
                BirthConsumed = true
            };
            return true;
        }

        if (proposed.MidpointConsumed && !captured.MidpointConsumed)
        {
            if (!owned.MidpointEligible || owned.MidpointConsumed)
                return false;
            payment = owned with
            {
                MidpointConsumed = true
            };
            return true;
        }

        return false;
    }
}
