using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

public readonly record struct GemStaffFinalize1458(int Penetration, int MaximumPenetration,
    int TimeLeft, int ArmorPenetration);

public readonly record struct GemStaffKill1458(bool HasAreaDamage, int RandomSamples,
    int Width, int Height, int Damage, float KnockBack, int RetainedLocalImmunityValue)
{
    public int FinalWidth => HasAreaDamage ? 22 : 10;
    public int FinalHeight => HasAreaDamage ? 22 : 10;
}

public static partial class VanillaGemStaffProjectile1458
{
    public const int LocalNpcHitCooldown = 60;

    public static float GetLaunchDamageMultiplier(GemStaffFeatures1458 features)
    {
        if ((features & (GemStaffFeatures1458.AllGems | GemStaffFeatures1458.AllGems2)) != 0) return 1f;
        float multiplier = 1f;
        if ((features & GemStaffFeatures1458.SwirlTwins) != 0) multiplier = Math.Min(multiplier, .6f);
        if ((features & GemStaffFeatures1458.CanBounce) != 0) multiplier = Math.Min(multiplier, .8f);
        if ((features & GemStaffFeatures1458.ArPenSpread) != 0) multiplier = Math.Min(multiplier, .5f);
        return multiplier;
    }

    public static bool TryFinalize(ProjectileTypeId type, GemStaffFeatures1458 features,
        in GemStaffFinalize1458 before, out GemStaffFinalize1458 after)
    {
        after = default;
        if (!IsGemStaffProjectile(type) || !TryDecodeFeatures(EncodeFeatures(features), out _, true) ||
            before.Penetration < 0 || before.MaximumPenetration < 0 || before.TimeLeft < 0 || before.ArmorPenetration < 0)
            return false;
        int extra = 0;
        bool fast = (features & GemStaffFeatures1458.FastThenSlow) != 0;
        if (fast || (features & GemStaffFeatures1458.BiggerHitbox) != 0) extra = 1;
        if ((features & GemStaffFeatures1458.CanBounce) != 0) extra = 2;
        int armor = (features & GemStaffFeatures1458.ArPenSpread) != 0 ? 4 : 0;
        bool all = (features & (GemStaffFeatures1458.AllGems | GemStaffFeatures1458.AllGems2)) != 0;
        if (all) { extra = 2; armor = 4; }
        long time = all ? 90 : (long)before.TimeLeft * (fast ? 2 : 1);
        if ((long)before.Penetration + extra > int.MaxValue || (long)before.MaximumPenetration + extra > int.MaxValue ||
            (long)before.ArmorPenetration + armor > int.MaxValue || time > int.MaxValue) return false;
        after = new(before.Penetration + extra, before.MaximumPenetration + extra, (int)time, before.ArmorPenetration + armor);
        return true;
    }

    public static bool TryPrepareKill(ProjectileTypeId type, GemStaffFeatures1458 features,
        int damage, float knockBack, bool windowsArithmetic, out GemStaffKill1458 kill)
    {
        kill = default;
        if (!IsGemStaffProjectile(type) || !TryDecodeFeatures(EncodeFeatures(features), out _, true) ||
            damage < 0 || !float.IsFinite(knockBack) || knockBack < 0f) return false;
        bool area = (features & GemStaffFeatures1458.AoeExplosion) != 0;
        if (!area) { kill = new(false, 15, 10, 10, damage, knockBack, LocalNpcHitCooldown); return true; }
        float multiplier = GetLaunchDamageMultiplier(features);
        // CLR4 x86 retains the quotient/product wider until the final integer conversion.
        double result = windowsArithmetic ? damage * ((double).35f / multiplier) : damage * (.35f / multiplier);
        if (result < 0d || result > int.MaxValue) return false;
        float nextKnockBack = knockBack * .35f;
        if (!float.IsFinite(nextKnockBack)) return false;
        kill = new(true, 105, 160, 160, (int)result, nextKnockBack, LocalNpcHitCooldown);
        return true;
    }
}
