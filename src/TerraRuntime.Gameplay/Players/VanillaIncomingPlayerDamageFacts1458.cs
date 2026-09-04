using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Players;

/// <summary>
/// Source-backed incoming-player damage facts from TerrariaServer 1.4.5.8 Player.Update_NPCCollision,
/// Projectile.Damage_EVP and Player.Hurt. This deliberately excludes luck re-rolls, banner/cold/trap modifiers,
/// parry/thorns and debuffs until those states are authoritative runtime inputs.
/// </summary>
public enum VanillaPlayerImmunityChannel1458 : byte
{
    General = 0,
    BossNoCheese = 1
}

public static class VanillaIncomingPlayerDamageFacts1458
{
    public const int OrdinaryPveImmunityTicks = 40;
    public const int OneDamagePveImmunityTicks = 20;

    public static VanillaPlayerImmunityChannel1458 GetHostileProjectileImmunityChannel(ProjectileTypeId type) =>
        type.Value is 452 or 454 or 455 or 462 or 871 or 872 or 873 or 874 or 919 or 923 or 924
            ? VanillaPlayerImmunityChannel1458.BossNoCheese
            : VanillaPlayerImmunityChannel1458.General;

    public static bool TryGetNpcContactImmunityChannel(
        NpcTypeId type,
        in NpcAiState ai,
        out VanillaPlayerImmunityChannel1458 channel)
    {
        channel = VanillaPlayerImmunityChannel1458.General;
        if (!ai.IsFinite)
            return false;

        if (type == VanillaNpcIds.EmpressOfLight)
        {
            channel = VanillaPlayerImmunityChannel1458.BossNoCheese;
            // Player.Update_NPCCollision explicitly suppresses Empress body contact in states 0 and 10.
            return ai.Ai0 is not (0f or 10f);
        }

        if (type == VanillaNpcIds.MoonLordHead ||
            type == VanillaNpcIds.MoonLordHand ||
            type == VanillaNpcIds.MoonLordCore ||
            type == VanillaNpcIds.MoonLordFreeEye ||
            type.Value == 401)
        {
            channel = VanillaPlayerImmunityChannel1458.BossNoCheese;
        }

        return true;
    }

    /// <summary>Main.DamageVar baseline for one already-selected -15..+15 roll, with luck re-rolls excluded.</summary>
    public static int ApplyDamageVariation(float damage, int percent)
    {
        if (!float.IsFinite(damage) || damage < 0f || percent is < -15 or > 15)
            return 0;

        float varied = damage * (1f + percent * 0.01f);
        return Math.Max(0, (int)MathF.Round(varied, MidpointRounding.ToEven));
    }

    /// <summary>
    /// Projectile.Damage_EVP applies Main.DamageVar and then the vanilla x2 hostile-projectile factor. The caller
    /// supplies the difficulty-scaled projectile damage already owned by the runtime definition/AI slice.
    /// </summary>
    public static int ResolveHostileProjectileDamage(int projectileDamage, int variationPercent)
    {
        if (projectileDamage <= 0)
            return 0;
        int varied = ApplyDamageVariation(projectileDamage, variationPercent);
        return varied > int.MaxValue / 2 ? int.MaxValue : varied * 2;
    }

    public static int ResolveNpcContactDamage(int npcDamage, int variationPercent) =>
        npcDamage <= 0 ? 0 : ApplyDamageVariation(npcDamage, variationPercent);

    public static int ResolvePveImmunityTicks(int committedDamage) =>
        committedDamage == 1 ? OneDamagePveImmunityTicks : OrdinaryPveImmunityTicks;
}
