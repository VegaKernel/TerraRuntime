using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerGemStaffFacts1458(ItemTypeId Weapon, PrefixId Prefix, ItemTypeId EffectiveBody,
    int Mana, bool SlowMagicUse, bool ControlUseItem, bool LastUseSuccess, bool NoItems,
    bool CrowdControlled, bool SelectionBuffered, bool LocalPlayer)
{
    public bool Prepaid { get; init; }
    public bool PrepaidSlowMagicUse { get; init; }
    public float ManaCostMultiplier { get; init; } = 1f;
    public bool WindowsItemPrefixArithmetic { get; init; }
    public bool AllowedToHoldItems { get; init; } = true;
}

public readonly record struct PlayerGemStaffTransition1458(PlayerSelectedConsumableState1458 State,
    int Mana, bool SlowMagicUse, bool LastUseSuccess, bool PendingItemReuse,
    bool AttemptedUse, bool BeganActualUse, int ManaPaid, bool RefreshManaDelay,
    bool InitialShoot, bool MidpointShoot);

/// <summary>Source staff ItemCheck clocks and payment. Ordered Shoot offers still require caller-owned mana/actor custody.</summary>
public static class VanillaGemStaffUse1458
{
    public static bool TryStep(PlayerSelectedConsumableState1458 previous, in PlayerGemStaffFacts1458 facts,
        Func<int, int, int> next, out PlayerGemStaffTransition1458 transition)
    {
        transition = default;
        if (!VanillaGemStaff1458.TryResolveWeapon(facts.Weapon, facts.Prefix,
                facts.WindowsItemPrefixArithmetic, out var weapon, out _) ||
            !VanillaGemStaff1458.TryPackFeatures(facts.Weapon, facts.EffectiveBody, out var features) ||
            facts.Mana < 0 || !float.IsFinite(facts.ManaCostMultiplier) || facts.ManaCostMultiplier <= 0f ||
            previous.Animation is < 0 or > short.MaxValue || previous.AnimationMax is < 0 or > short.MaxValue ||
            previous.ItemTime is < 0 or > short.MaxValue || previous.ItemTimeMax is < 0 or > short.MaxValue ||
            previous.RevolverCritBonus < int.MinValue + 2 || next is null) return false;
        if (facts.CrowdControlled)
        {
            transition = new(previous with { Animation = 0, AnimationMax = 0 }, facts.Mana,
                facts.SlowMagicUse, facts.LastUseSuccess, false, false, false, 0, false, false, false);
            return true;
        }
        float rawCost = weapon.ManaCost * facts.ManaCostMultiplier;
        if (!float.IsFinite(rawCost) || rawCost >= int.MaxValue) return false;
        int cost = (int)rawCost;
        int animation = previous.Animation;
        int maximum = previous.AnimationMax;
        bool release = previous.ReleaseUseItem;
        bool slow = facts.SlowMagicUse;
        int Animation() => (slow ? Math.Max(1, (int)(weapon.Animation * 2.5f)) : weapon.Animation) +
            (weapon.AutoReuse ? 1 : 0);
        if (weapon.AutoReuse && !facts.NoItems && !(facts.SelectionBuffered && previous.ItemTime == 0))
        {
            release = true;
            if (animation == 1)
            {
                if (!facts.LocalPlayer && facts.ControlUseItem && facts.LastUseSuccess)
                    animation = maximum = Animation();
                else animation = 0;
            }
        }
        if (animation == 0) maximum = 0;
        int crit = previous.RevolverCritBonus;
        if (next(0, 3) == 0) crit -= 2;
        bool midpoint = facts.LocalPlayer && VanillaGemStaff1458.IsMidpointShot(
            previous with { Animation = animation, AnimationMax = maximum }, features);
        bool attempt = facts.ControlUseItem && release && animation == 0 && !facts.SelectionBuffered;
        bool canUse = attempt && !facts.NoItems;
        int mana = facts.Mana;
        int paid = 0;
        bool success = facts.LastUseSuccess;
        if (canUse)
        {
            if (facts.Prepaid) slow = facts.PrepaidSlowMagicUse;
            else
            {
                slow = mana < cost;
                int after = Math.Max(0, mana - cost);
                paid = mana - after;
                mana = after;
            }

        }
        if (facts.LocalPlayer && attempt) success = canUse;
        bool began = canUse && success;
        if (began) animation = maximum = Animation();
        bool refreshDelay = animation > 0;
        if (animation > 0) animation--;
        bool pending = refreshDelay && animation == 0 && facts.ControlUseItem && release;
        int time = Math.Max(0, previous.ItemTime - 1);
        int timeMax = previous.ItemTimeMax;
        bool shoot = facts.LocalPlayer && facts.AllowedToHoldItems && animation > 0 && time == 0;
        if (shoot) time = timeMax = slow ? Math.Max(1, (int)(weapon.UseTime * 2.5f)) : weapon.UseTime;
        transition = new(new(time, timeMax, animation, maximum, !facts.ControlUseItem,
            previous.PotionDelay, crit), mana, slow, success, pending, attempt, began, paid,
            refreshDelay, shoot, midpoint);
        return true;
    }
}
