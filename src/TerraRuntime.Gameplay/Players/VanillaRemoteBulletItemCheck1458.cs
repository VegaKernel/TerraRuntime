using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerRemoteBulletItemFacts1458(ItemTypeId Weapon, bool? HasAmmo,
    bool ControlUseItem, bool LastUseSuccess, bool Cursed, bool CrowdControlled, bool SelectionBuffered,
    PrefixId Prefix = default, bool WindowsItemPrefixArithmetic = false);

public readonly record struct PlayerRemoteBulletItemTransition1458(
    PlayerSelectedConsumableState1458 State, bool PendingItemReuse, bool BeganActualUse = false);

/// <summary>Neutral dedicated remote ItemCheck clocks. Its owner-only Shoot/PickAmmo branch is absent.</summary>
public static class VanillaRemoteBulletItemCheck1458
{
    public static bool IsSupported(ItemTypeId weapon, PrefixId prefix = default,
        bool windowsItemPrefixArithmetic = false) =>
        TryGet(weapon, prefix, out _, out _, windowsItemPrefixArithmetic);

    public static bool TryStep(PlayerSelectedConsumableState1458? previous,
        in PlayerRemoteBulletItemFacts1458 facts, Func<int, int, int> nextInteger,
        out PlayerRemoteBulletItemTransition1458 transition)
    {
        transition = default;
        if (previous is not { } state ||
            !TryGet(facts.Weapon, facts.Prefix, out int useAnimation, out bool autoReuse, facts.WindowsItemPrefixArithmetic) ||
            state.ItemTime is < 0 or > short.MaxValue || state.ItemTimeMax is < 0 or > short.MaxValue ||
            state.Animation is < 0 or > short.MaxValue || state.AnimationMax is < 0 or > short.MaxValue ||
            state.PotionDelay is < 0 or > 3_600 || state.RevolverCritBonus < int.MinValue + 2 ||
            nextInteger is null) return false;
        if (facts.CrowdControlled)
        {
            transition = new(state with { Animation = 0, AnimationMax = 0 }, false);
            return true;
        }

        int animation = state.Animation;
        int animationMax = state.AnimationMax;
        bool release = state.ReleaseUseItem;
        if (autoReuse && !facts.Cursed && !facts.SelectionBuffered)
        {
            release = true;
            if (animation == 1)
            {
                if (facts.ControlUseItem && facts.LastUseSuccess)
                    animation = animationMax = useAnimation + 1;
                else
                    animation = 0;
            }
        }
        if (animation == 0) animationMax = 0;
        bool attempt = facts.ControlUseItem && release && animation == 0 &&
            !facts.SelectionBuffered && !facts.Cursed;
        // A false remote success report does not erase the source CheckCanUse/HasAmmo query.
        if (attempt && facts.HasAmmo is null) return false;

        int crit = state.RevolverCritBonus;
        if (nextInteger(0, 3) == 0) crit -= 2;
        bool beganActualUse = attempt && facts.HasAmmo == true && facts.LastUseSuccess;
        if (beganActualUse)
            animation = animationMax = useAnimation + (autoReuse ? 1 : 0);
        bool pending = false;
        if (animation > 0)
        {
            animation--;
            pending = animation == 0 && facts.ControlUseItem && release;
        }
        transition = new(new(Math.Max(0, state.ItemTime - 1), state.ItemTimeMax,
            animation, animationMax, !facts.ControlUseItem, state.PotionDelay, crit), pending, beganActualUse);
        return true;
    }

    private static bool TryGet(ItemTypeId weapon, PrefixId prefix, out int useAnimation, out bool autoReuse,
        bool windowsItemPrefixArithmetic = false)
    {
        useAnimation = 0;
        autoReuse = weapon.Value is 98 or 533 or 1929 or 679;
        if (weapon.Value is not (98 or 219 or 533 or 1929 or 964 or 534 or 679 or 4703) ||
            !VanillaProjectileWeaponCombatCatalog.TryGetWeapon(weapon, out var definition) ||
            !VanillaItemPrefixTable1458.TryResolveSpeedMultiplier(weapon, prefix,
                windowsItemPrefixArithmetic, out float speedMultiplier)) return false;
        // Reuse the verified SetDefaults animation; only the remote autoReuse eligibility
        // supplements the launch catalog. These eight have style5, zero mana/reuse delay.
        // Linux/CoreCLR rounds the single-precision product. Official Windows CLR4/x86
        // retains its wider product into Math.Round (Quad55*.9 =>49, 55*1.1 =>61).
        // Invalid requested prefixes remain unknown; their source rerolled replacement is not inferred.
        useAnimation = windowsItemPrefixArithmetic
            ? (int)Math.Round(definition.AnimationTicks * (double)speedMultiplier)
            : (int)Math.Round(definition.AnimationTicks * speedMultiplier);
        return true;
    }
}
