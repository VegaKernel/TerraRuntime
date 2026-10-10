using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>Retained after-Strike flags and optional owned hit count; null never becomes a guessed zero.</summary>
public readonly record struct VanillaArrowPostStrike1458(float Ai2, int? NumHits);

/// <summary>Bounded ordinary-arrow writes around an admitted original 1.4.5.8 NPC Strike.</summary>
public static class VanillaArrowPostHit1458
{
    private static bool Supports(ProjectileTypeId type) => type.Value is 1 or 2 or 4 or 5;

    /// <summary>Stored projectile damage changes after selecting this hit's damage, before Strike/publication.</summary>
    public static bool TryResolveStoredDamage(ProjectileTypeId type, int damage, out int nextDamage)
    {
        nextDamage = default;
        if (!Supports(type) || damage < 0)
            return false;
        nextDamage = type.Value switch
        {
            4 => (int)(damage * 0.95d),
            5 => (int)(damage * 0.9d),
            _ => damage,
        };
        return true;
    }

    /// <summary>
    /// Called only after successful source Strike. For these arrow/AI1 defaults, OwnedBySomeone means
    /// !npcProj and !trap, independently of the numeric owner slot. Flags use raw FloatIntUnion bits.
    /// Unknown NumHits stays unknown; count-dependent effects remain outside that caller's owned scope.
    /// </summary>
    public static bool TryResolveAfterStrike(ProjectileTypeId type, float ai2, int? numHits,
        bool ownedBySomeone, out VanillaArrowPostStrike1458 next)
    {
        next = default;
        if (!Supports(type) || !float.IsFinite(ai2) || numHits is < 0 or int.MaxValue)
            return false;
        float flags = ownedBySomeone
            ? BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(ai2) | 3)
            : ai2;
        next = new(flags, numHits.HasValue ? numHits.Value + 1 : null);
        return true;
    }
}
