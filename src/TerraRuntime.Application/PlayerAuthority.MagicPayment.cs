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
        out VanillaPlayerCombatSnapshot combat)
    {
        payment = default; debit = 0; period = 0; combat = default;
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
                !VanillaRemoteChannelledMagic1458.TryPay(member.Mana, weapon.ManaCost, out int after, out bool slow))
                return false;
            debit = member.Mana - after;
            payment = new(connection.Player, (previous?.Attempt ?? 0) + 1, member.SelectedItem,
                selected, phase.BaseManaMaximum, after, slow, false, true, true);
        }
        period = payment.SlowMagicUse ? (int)(weapon.UseTimeTicks * 2.5f) : weapon.UseTimeTicks;
        return true;
    }
}
