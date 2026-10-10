using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

// Part of the existing immutable ItemPhase capture, not an independent receipt registry.
internal readonly record struct RuntimePlayerMagicPayment1458(
    PlayerHandle Player, ulong Attempt, byte Slot, RuntimePlayerInventoryItem Item,
    int BaseManaMaximum, int PaidMana, bool SlowMagicUse, bool EligibleBirth,
    bool BirthConsumed, bool AwaitingPhase)
{
    internal bool IsValid => Attempt > 0 && Slot <= 58 && Item.IsCanonical && Item.Stack > 0 &&
        Item.Prefix.Value == 0 && BaseManaMaximum > 0 && PaidMana is >= 0 and <= 400 &&
        TerraRuntime.Gameplay.Players.VanillaRemoteChannelledMagic1458.IsSupported(Item.ItemType, Item.Prefix) &&
        (!AwaitingPhase || BirthConsumed && !EligibleBirth);
    internal bool Matches(PlayerHandle player, byte slot, RuntimePlayerInventoryItem item,
        int baseManaMaximum) => Player == player && Slot == slot && Item == item &&
        BaseManaMaximum == baseManaMaximum;
}
