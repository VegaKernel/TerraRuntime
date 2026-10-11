using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool? CaptureMagicChannel(PlayerHandle player)
    {
        if (!membership.TryGet(player, out var member)) return null;
        if (!HasRemotePlayerPhaseBinding) return (member.ControlFlags & 32) != 0;
        return playerUpdateRandomOwned && member.ItemPhase is { } phase ? phase.Channel : null;
    }
    internal bool TryPrepareMagicPayment(ConnectionHandle connection,
        RuntimePlayerInventoryItem selected, in VanillaChanneledMagicProjectileWeaponCombatDefinition weapon,
        out RuntimePlayerMagicPayment1458 payment, out int debit, out int period,
        out VanillaPlayerCombatSnapshot combat, out RuntimePlayerMagicUsePreparation? magicUse)
    {
        payment = default; debit = 0; period = 0; combat = default; magicUse = null;
        if (!playerUpdateRandomOwned || !membership.TryGet(connection, out var member) ||
            member.ItemPhase is not { DerivedCombat: { } derived } phase ||
            !IsValidDerivedCombat(in derived) || phase.BaseManaMaximum <= 0 ||
            (phase.MagicPayment is { } imported && !imported.IsValid) ||
            !member.HasMana || member.Mana < 0 ||
            (member.ControlFlags & 32) == 0 || (member.MiscFlags2 & 64) == 0 ||
            selected.Prefix.Value != 0 || selected.Stack <= 0) return false;
        // These are source pre-ItemCheck branch inputs, not a client success claim.
        // A packet-first producer must obey the same current no-use boundary.
        if (member.IsDead ||
            (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0 ||
            playerUpdateWorld is not { } world ||
            VanillaPlayerHealthContext1458.IsRemoteOutOfRange(member.PositionX, member.PositionY,
                20, 42, world.MaxTilesX, world.MaxTilesY) ||
            transferProfiles.CountActiveBuffs(connection, new(23)) is not 0) return false;
        combat = derived;
        var previous = phase.MagicPayment;
        bool same = previous is { } known && known.Matches(connection.Player, member.SelectedItem,
            selected, phase.BaseManaMaximum);
        if (same && previous is { EligibleBirth: true, BirthConsumed: false, AwaitingPhase: false } paid)
            payment = paid with { BirthConsumed = true };
        else
        {
            if (same && (!phase.Selected.ReleaseUseItem || phase.Selected.Animation > 0)) return false;
            if (previous is { Attempt: ulong.MaxValue } ||
                !VanillaRemoteChannelledMagic1458.TryResolveManaCost(weapon.ManaCost, derived.ManaCostMultiplier, out int cost))
                return false;
            ulong attempt = (previous?.Attempt ?? 0) + 1;
            int available = member.Mana;
            RuntimePlayerMagicRefill1458? refill = null;
            PlayerBuffState? beforeBuffs = null;
            PlayerBuffState? afterBuffs = null;
            if (derived.ManaFlower && available < cost)
            {
                var items = new RuntimePlayerInventoryItem[59];
                beforeBuffs = transferProfiles.CaptureBuffState(connection);
                if (beforeBuffs is null || !inventory.TryCopyInventory(connection, items)) return false;
                afterBuffs = beforeBuffs.Clone();
                if (!TryStageAutoQuickMana(connection, attempt, available, phase.Mana.Maximum,
                    phase.ManaPotionDelay, items, afterBuffs, out available, out refill)) return false;
            }
            if (!VanillaRemoteChannelledMagic1458.TryPay(available, cost, out int after, out bool slow)) return false;
            debit = Math.Min(available, cost);
            payment = new(connection.Player, attempt, member.SelectedItem,
                selected, phase.BaseManaMaximum, after, slow, false, true, true) { Refill = refill };
            if (refill is { } restored)
            {
                var mutations = new[] { new RemotePhaseInventoryMutation(connection, restored.Slot, restored.Before, restored.After) };
                if (!inventory.TryPrepareRemotePhase(mutations, inventory.Serial, out var prepared) || prepared is null)
                    return false;
                magicUse = new(payment, beforeBuffs!, afterBuffs!, prepared);
            }
        }
        period = payment.SlowMagicUse ? (int)(weapon.UseTimeTicks * 2.5f) : weapon.UseTimeTicks;
        return true;
    }
}
