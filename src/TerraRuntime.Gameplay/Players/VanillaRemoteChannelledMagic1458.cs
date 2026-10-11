using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerRemoteChannelledMagicFacts1458(ItemTypeId Weapon, PrefixId Prefix,
    int Mana, bool Channel, bool SlowMagicUse, bool ControlUseItem, bool LastUseSuccess,
    bool Cursed, bool CrowdControlled, bool SelectionBuffered, bool Prepaid = false,
    bool PrepaidSlowMagicUse = false, float ManaCostMultiplier = 1f);

public readonly record struct PlayerRemoteChannelledMagicTransition1458(
    PlayerSelectedConsumableState1458 State, int Mana, bool Channel, bool SlowMagicUse,
    bool PendingItemReuse, bool AttemptedUse, bool BeganActualUse, int ManaPaid,
    bool RefreshManaDelay);

/// <summary>Source-neutral remote clocks/payment for the three represented channelled weapons.</summary>
public static class VanillaRemoteChannelledMagic1458
{
    public static bool IsSupported(ItemTypeId weapon, PrefixId prefix) => prefix.Value == 0 &&
        VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeapon(weapon, out _);

    // CheckMana's source Single product is truncated before sufficient/shortage selection.
    public static bool TryResolveManaCost(int rawCost, float multiplier, out int cost)
    {
        cost = 0;
        if (rawCost <= 0 || !float.IsFinite(multiplier) || multiplier <= 0f) return false;
        float product = rawCost * multiplier;
        if (!float.IsFinite(product) || product < 1f || product >= int.MaxValue) return false;
        cost = (int)product;
        return true;
    }

    public static bool TryPay(int mana, int cost, out int after, out bool slow)
    {
        after = 0; slow = false;
        if (mana < 0 || cost <= 0) return false;
        after = Math.Max(0, mana - cost);
        slow = mana < cost;
        return true;
    }

    public static bool TryStep(PlayerSelectedConsumableState1458 previous,
        in PlayerRemoteChannelledMagicFacts1458 facts, Func<int, int, int> next,
        out PlayerRemoteChannelledMagicTransition1458 transition)
    {
        transition = default;
        if (!IsSupported(facts.Weapon, facts.Prefix) || facts.Mana < 0 || next is null ||
            previous.Animation is < 0 or > short.MaxValue || previous.AnimationMax is < 0 or > short.MaxValue ||
            previous.ItemTime is < 0 or > short.MaxValue || previous.ItemTimeMax is < 0 or > short.MaxValue ||
            previous.RevolverCritBonus < int.MinValue + 2 ||
            !VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeapon(facts.Weapon, out var weapon) ||
            !TryResolveManaCost(weapon.ManaCost, facts.ManaCostMultiplier, out int manaCost)) return false;
        if (facts.CrowdControlled)
        {
            transition = new(previous with { Animation = 0, AnimationMax = 0 }, facts.Mana, false,
                facts.SlowMagicUse, false, false, false, 0, false);
            return true;
        }
        int animation = previous.Animation;
        int maximum = animation == 0 ? 0 : previous.AnimationMax;
        int crit = previous.RevolverCritBonus;
        if (next(0, 3) == 0) crit -= 2;
        bool attempt = facts.ControlUseItem && previous.ReleaseUseItem && animation == 0 &&
            !facts.Cursed && !facts.SelectionBuffered;
        int mana = facts.Mana;
        bool slow = facts.SlowMagicUse;
        int paid = 0;
        if (attempt)
        {
            if (facts.Prepaid) slow = facts.PrepaidSlowMagicUse;
            else
            {
                if (!TryPay(mana, manaCost, out int after, out slow)) return false;
                paid = mana - after; mana = after;
            }
        }
        // Payment precedes the remote last-success mask in original TryStartUse.
        bool began = attempt && facts.LastUseSuccess;
        bool channel = facts.Channel;
        if (began)
        {
            animation = maximum = slow ? (int)(weapon.AnimationTicks * 2.5f) : weapon.AnimationTicks;
            channel = true;
        }
        if (!facts.ControlUseItem || facts.SelectionBuffered) channel = false;
        bool refreshDelay = animation > 0;
        if (animation > 0) animation--;
        bool pending = animation == 0 && refreshDelay && facts.ControlUseItem && previous.ReleaseUseItem;
        transition = new(new(Math.Max(0, previous.ItemTime - 1), previous.ItemTimeMax,
            animation, maximum, !facts.ControlUseItem, previous.PotionDelay, crit), mana,
            channel, slow, pending, attempt, began, paid, refreshDelay);
        return true;
    }
}
