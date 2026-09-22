using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source-pinned AI_003 and GetMeleeCollisionData facts for armed zombie types 430 through 436.</summary>
public static class VanillaArmedZombieCombatFacts1458
{
    public const float AttackDamageMultiplier = 1.5f;
    public const float ExtendedMeleeDamageMultiplier = 1.25f;
    public const int ExtendedMeleeReach = 34;

    public static bool IsArmedZombie(NpcTypeId type) => type.Value is >= 430 and <= 436 or 591;

    public static bool HasExtendedMeleeReach(NpcTypeId type, NpcAiState ai) =>
        IsArmedZombie(type) && ai.Ai2 > 5f;

    public static int ResolveAttackDamage(int baseDamage) =>
        checked((int)(baseDamage * AttackDamageMultiplier));

    public static int ResolveMeleeDamage(int rawDamage, NpcTypeId type, NpcAiState ai) =>
        HasExtendedMeleeReach(type, ai)
            ? checked((int)(rawDamage * ExtendedMeleeDamageMultiplier))
            : rawDamage;

    public static void ExpandMeleeHitbox(
        NpcTypeId type,
        NpcAiState ai,
        int spriteDirection,
        ref float left,
        ref float right)
    {
        if (!HasExtendedMeleeReach(type, ai))
            return;

        if (spriteDirection < 0)
            left -= ExtendedMeleeReach;
        else
            right += ExtendedMeleeReach;
    }
}
