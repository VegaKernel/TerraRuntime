using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;
// Part of the existing immutable ItemPhase capture, not an independent receipt registry.
internal readonly record struct RuntimePlayerMagicPayment1458(PlayerHandle Player,
    ulong Attempt,
    byte Slot,
    RuntimePlayerInventoryItem Item,
    int BaseManaMaximum,
    int PaidMana,
    bool SlowMagicUse,
    bool EligibleBirth,
    bool BirthConsumed,
    bool AwaitingPhase)
{
    internal bool GemStaff { get; init; }
    internal TerraRuntime.Gameplay.Players.PlayerSelectedConsumableState1458 GemClocks { get; init; }
    internal TerraRuntime.Contracts.Gameplay.ItemTypeId EffectiveBody { get; init; }
    internal TerraRuntime.Gameplay.Projectiles.GemStaffFeatures1458 GemFeatures { get; init; }
    internal bool GemLastUseSuccess { get; init; }
    internal bool MidpointEligible { get; init; }
    internal bool MidpointConsumed { get; init; }
    internal RuntimePlayerMagicRefill1458? Refill { get; init; }
    internal bool IsValid => Attempt > 0 &&
        Slot <= 58 &&
        Item.IsCanonical &&
        Item.Stack > 0 &&
        BaseManaMaximum > 0 &&
        PaidMana is >= 0 and <= 400 &&
        (GemStaff ? TerraRuntime.Gameplay.Items.VanillaGemStaff1458.TryGetWeapon(Item.ItemType,
        out _) &&
        TerraRuntime.Gameplay.Items.VanillaGemStaff1458.TryPackFeatures(Item.ItemType,
        EffectiveBody,
        out var expected) &&
        expected == GemFeatures &&
        GemClocks.Animation is >= 0 and <= short.MaxValue &&
        GemClocks.AnimationMax is >= 0 and <= short.MaxValue &&
        GemClocks.ItemTime is >= 0 and <= short.MaxValue &&
        GemClocks.ItemTimeMax is >= 0 and <= short.MaxValue : TerraRuntime.Gameplay.Players.VanillaRemoteChannelledMagic1458.IsSupported(Item.ItemType,
        Item.Prefix)) &&
        (!AwaitingPhase ||
        BirthConsumed &&
        !EligibleBirth) &&
        (Refill is not { } refill ||
        refill.IsValid &&
        refill.Connection.Player == Player &&
        refill.Attempt == Attempt);

    internal bool Matches(PlayerHandle player,
        byte slot,
        RuntimePlayerInventoryItem item,
        int baseManaMaximum) => Player == player &&
        Slot == slot &&
        Item == item &&
        BaseManaMaximum == baseManaMaximum;
}

// Kept inside the paid attempt, not a separate report/receipt registry. Transfer clears it.
internal readonly record struct RuntimePlayerMagicRefill1458(ConnectionHandle Connection,
    ulong Attempt,
    short Slot,
    RuntimePlayerInventoryItem Before,
    RuntimePlayerInventoryItem After)
{
    internal bool IsValid => Connection.IsAssigned &&
        Attempt > 0 &&
        Slot is >= 0 and < 58 &&
        Before.IsCanonical &&
        Before.ItemType.Value is (110 or 189 or 500 or 2209) &&
        Before.Stack > 0 &&
        Before.Prefix.Value == 0 &&
        After.IsCanonical &&
        After == (Before.Stack == 1 ? default : Before with
    {
        Stack = (short)(Before.Stack - 1)
    }

    );
}
