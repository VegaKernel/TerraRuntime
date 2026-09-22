using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source-pinned AI_003 armed-melee and GetMeleeCollisionData facts.</summary>
public static class VanillaArmedZombieCombatFacts1458
{
    public const float AttackDamageMultiplier = 1.5f;
    public const float ExtendedMeleeDamageMultiplier = 1.25f;
    public const int ExtendedMeleeReach = 34;

    public static bool IsArmedZombie(NpcTypeId type) => type.Value is >= 430 and <= 436 or 591;

    public static bool IsCrawdad(NpcTypeId type) => type.Value is 494 or 495;

    public static bool HasExtendedMeleeReach(NpcTypeId type, NpcAiState ai) =>
        (IsArmedZombie(type) || IsCrawdad(type)) && ai.Ai2 > 5f;

    public static int GetExtendedMeleeReach(NpcTypeId type) => IsCrawdad(type) ? 18 : ExtendedMeleeReach;

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
            left -= GetExtendedMeleeReach(type);
        else
            right += GetExtendedMeleeReach(type);
    }
}
