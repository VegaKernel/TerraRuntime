using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Shared target-mitigation stage for the currently source-backed PvP slice. TerrariaServer 1.4.5.8
/// Player.Hurt(pvp:true) mutates HP through Main.CalculateDamagePlayersTake, so defense effectiveness is
/// difficulty-aware: 0.5 Classic, 0.75 Expert and 1.0 Master. The later CalculateDamagePlayersTakeInPVP call
/// only changes Hurt's return value. Endurance applies to the pre-crit HP value; PvP crit remains hit semantics
/// but does not double this HP delta. Flat attacker armor penetration is deliberately not subtracted here because
/// the pinned ordinary PvP Hurt path does not feed it into player defense calculation.
/// </summary>
public static class VanillaCombatDamagePipeline
{
    public const float ClassicDefenseEffectiveness = 0.5f;
    public const float ExpertDefenseEffectiveness = 0.75f;
    public const float MasterDefenseEffectiveness = 1f;

    public static bool TryResolvePvp(
        in AuthoritativeAttackDamage attack,
        in VanillaPlayerCombatSnapshot target,
        bool immune,
        out FinalDamageToHp final,
        bool expertMode = false,
        bool masterMode = false)
    {
        if (!attack.IsValid || (masterMode && !expertMode) ||
            !float.IsFinite(target.Endurance) || target.Endurance is < 0f or > 1f)
        {
            final = default;
            return false;
        }

        int effectiveDefense = Math.Max(target.Defense, 0);
        var mitigation = new TargetMitigation(
            target.Defense,
            effectiveDefense,
            target.Endurance,
            immune,
            Dodged: false,
            target.NoKnockback);

        if (immune)
        {
            final = new FinalDamageToHp(0, mitigation);
            return true;
        }

        float defenseEffectiveness = masterMode
            ? MasterDefenseEffectiveness
            : expertMode
                ? ExpertDefenseEffectiveness
                : ClassicDefenseEffectiveness;
        double afterDefense = Math.Max(attack.Damage - effectiveDefense * defenseEffectiveness, 1d);
        int damage = Math.Max((int)((1f - target.Endurance) * afterDefense), 1);
        final = new FinalDamageToHp(damage, mitigation);
        return true;
    }
}
