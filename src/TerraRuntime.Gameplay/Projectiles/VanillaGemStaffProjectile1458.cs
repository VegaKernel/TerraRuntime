using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

[Flags]
public enum GemStaffFeatures1458 : ushort
{
    None = 0,
    Homing = 1 << 0,
    FastThenSlow = 1 << 1,
    CanBounce = 1 << 2,
    AoeExplosion = 1 << 3,
    SwirlTwins = 1 << 4,
    ArPenSpread = 1 << 5,
    BiggerHitbox = 1 << 6,
    AllGems = 1 << 7,
    RepeatsGem = 1 << 8,
    AllGems2 = 1 << 9
}

/// <summary>Original FloatIntUnion representation. Decoding alone does not certify a source birth.</summary>
public static partial class VanillaGemStaffProjectile1458
{
    public static bool IsGemStaffProjectile(ProjectileTypeId type) =>
        type.Value is 121 or 122 or 123 or 124 or 125 or 126 or 597;

    public static float EncodeFeatures(GemStaffFeatures1458 features) =>
        BitConverter.Int32BitsToSingle((int)features);

    public static bool TryDecodeFeatures(float ai0, out GemStaffFeatures1458 features,
        bool allowDormantSourceBranches = false)
    {
        int bits = BitConverter.SingleToInt32Bits(ai0);
        const int represented = (1 << 10) - 1;
        const int dormant = (int)(GemStaffFeatures1458.AllGems | GemStaffFeatures1458.AllGems2);
        features = default;
        if ((bits & ~represented) != 0 || (!allowDormantSourceBranches && (bits & dormant) != 0))
            return false;
        features = (GemStaffFeatures1458)bits;
        return true;
    }
}
