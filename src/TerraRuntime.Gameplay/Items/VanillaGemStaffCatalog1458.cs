using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct GemStaffWeaponDefinition1458(ItemTypeId Type, ProjectileTypeId Projectile,
    int BaseDamage, float KnockBack, float ShootSpeed, int ManaCost, int UseTime, int Animation, bool AutoReuse);

public readonly record struct GemStaffRobeDefinition1458(ItemTypeId Type, int Defense,
    int ManaMaximumBonus, float ManaCostReduction);

public readonly record struct GemStaffLaunch1458(ProjectileTypeId Projectile, float CenterX, float CenterY,
    float VelocityX, float VelocityY, int Damage, float KnockBack, float Ai0, float Ai1);

/// <summary>Seven retained source staffs and their existing functional robe effects. This helper does not certify custody.</summary>
public static class VanillaGemStaff1458
{
    public const int MaximumVolleySize = 2;

    public static bool TryGetWeapon(ItemTypeId item, out GemStaffWeaponDefinition1458 weapon)
    {
        weapon = item.Value switch
        {
            739 => new(item, new(121), 15, 3.25f, 6f, 7, 37, 37, false),
            740 => new(item, new(122), 16, 3.5f, 6.5f, 7, 36, 36, false),
            741 => new(item, new(123), 18, 4f, 7.5f, 8, 34, 34, true),
            742 => new(item, new(124), 19, 4.25f, 8f, 8, 32, 32, true),
            743 => new(item, new(125), 18, 4.75f, 9f, 9, 28, 28, true),
            744 => new(item, new(126), 23, 5.5f, 9.5f, 9, 26, 26, true),
            3377 => new(item, new(597), 21, 4.75f, 9f, 9, 28, 28, true),
            _ => default
        };
        return weapon.Type == item && !item.IsNone;
    }

    public static bool TryResolveWeapon(ItemTypeId item, PrefixId prefix, bool windowsItemPrefixArithmetic,
        out GemStaffWeaponDefinition1458 weapon, out int itemCrit)
    {
        itemCrit = 0;
        if (!TryGetWeapon(item, out weapon) ||
            !VanillaItemPrefixTable1458.TryGetStatModifiers(prefix, out var modifiers) ||
            !VanillaItemPrefixTable1458.TryGetManaMultiplier(prefix, out float mana)) return false;
        // Positive Item.Prefix requests use the original modifier table, not the natural-roll family.
        weapon = weapon with
        {
            BaseDamage = Round(weapon.BaseDamage, modifiers.DamageMultiplier, windowsItemPrefixArithmetic),
            KnockBack = weapon.KnockBack * modifiers.KnockBackMultiplier,
            ShootSpeed = weapon.ShootSpeed * modifiers.ShootSpeedMultiplier,
            ManaCost = Round(weapon.ManaCost, mana, windowsItemPrefixArithmetic),
            UseTime = Round(weapon.UseTime, modifiers.SpeedMultiplier, windowsItemPrefixArithmetic),
            Animation = Round(weapon.Animation, modifiers.SpeedMultiplier, windowsItemPrefixArithmetic)
        };
        itemCrit = modifiers.CritBonus;
        return true;
    }

    private static int Round(int value, float multiplier, bool windows) =>
        (int)Math.Round(windows ? value * (double)multiplier : value * multiplier);

    public static bool TryGetRobe(ItemTypeId item, out GemStaffRobeDefinition1458 robe)
    {
        robe = item.Value switch
        {
            1282 => new(item, 0, 20, 0.05f),
            1283 => new(item, 1, 40, 0.07f),
            1284 => new(item, 1, 40, 0.09f),
            1285 => new(item, 2, 60, 0.11f),
            1286 => new(item, 2, 60, 0.13f),
            1287 => new(item, 3, 80, 0.15f),
            4256 => new(item, 3, 60, 0.13f),
            _ => default
        };
        return robe.Type == item && !item.IsNone;
    }

    public static bool TryPackFeatures(ItemTypeId weapon, ItemTypeId effectiveBody,
        out GemStaffFeatures1458 features)
    {
        features = default;
        if (!TryGetWeapon(weapon, out var definition)) return false;
        ReadOnlySpan<int> projectiles = [121, 124, 597, 122, 123, 125, 126];
        ReadOnlySpan<int> robes = [1282, 1285, 4256, 1283, 1284, 1286, 1287];
        for (int i = 0; i < projectiles.Length; i++)
        {
            if (definition.Projectile.Value == projectiles[i] || effectiveBody.Value == robes[i])
                features |= (GemStaffFeatures1458)(1 << i);
            if (definition.Projectile.Value == projectiles[i] && effectiveBody.Value == robes[i])
                features |= GemStaffFeatures1458.RepeatsGem;
        }
        return true;
    }

    public static bool IsMidpointShot(in PlayerSelectedConsumableState1458 clocks,
        GemStaffFeatures1458 features) => (features & GemStaffFeatures1458.ArPenSpread) != 0 &&
        clocks.Animation > 0 && clocks.Animation == clocks.AnimationMax / 2;

    public static bool TryPlanVolley(ItemTypeId weapon, ItemTypeId effectiveBody,
        float centerX, float centerY, float velocityX, float velocityY, int damage, float knockBack,
        Func<double> nextDouble, Span<GemStaffLaunch1458> launches, out int count)
    {
        count = 0;
        if (!TryGetWeapon(weapon, out var definition) || !TryPackFeatures(weapon, effectiveBody, out var features) ||
            !float.IsFinite(centerX) || !float.IsFinite(centerY) || !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) || damage < 0 || !float.IsFinite(knockBack) || knockBack < 0f ||
            launches.Length < MaximumVolleySize || nextDouble is null) return false;
        if ((features & GemStaffFeatures1458.FastThenSlow) != 0) { velocityX *= 5f; velocityY *= 5f; }
        if ((features & GemStaffFeatures1458.CanBounce) != 0) { velocityX *= 1.25f; velocityY *= 1.25f; }
        if (!float.IsFinite(velocityX) || !float.IsFinite(velocityY)) return false;
        if ((features & GemStaffFeatures1458.ArPenSpread) != 0)
        {
            double sample = nextDouble();
            if (!double.IsFinite(sample) || sample < 0d || sample >= 1d) return false;
            float angle = ((float)Math.PI * 2f) * (float)sample;
            centerX += (float)Math.Cos(angle) * 12f;
            centerY += (float)Math.Sin(angle) * 12f;
        }
        float multiplier = (features & GemStaffFeatures1458.ArPenSpread) != 0 ? 0.5f :
            (features & GemStaffFeatures1458.SwirlTwins) != 0 ? 0.6f :
            (features & GemStaffFeatures1458.CanBounce) != 0 ? 0.8f : 1f;
        float scaledDamage = damage * multiplier;
        if (scaledDamage >= int.MaxValue) return false;
        damage = (int)scaledDamage;
        if ((features & GemStaffFeatures1458.RepeatsGem) != 0)
        {
            if (damage > int.MaxValue - 4 || !float.IsFinite(knockBack + 1f)) return false;
            damage += 4;
            knockBack += 1f;
        }
        float ai0 = VanillaGemStaffProjectile1458.EncodeFeatures(features);
        launches[0] = new(definition.Projectile, centerX, centerY, velocityX, velocityY, damage, knockBack, ai0, 0f);
        count = 1;
        if ((features & GemStaffFeatures1458.SwirlTwins) != 0)
        {
            launches[1] = launches[0] with { Ai1 = 1f };
            count = 2;
        }
        return true;
    }
}
