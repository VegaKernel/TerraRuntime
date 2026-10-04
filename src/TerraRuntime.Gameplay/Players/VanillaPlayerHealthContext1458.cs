namespace TerraRuntime.Gameplay.Players;

/// <summary>Source Player.Update/ResetEffects/UpdateBuffs ordering for the represented dedicated remote health context.</summary>
public static class VanillaPlayerHealthContext1458
{
    public const int InitialDerivedLifeMax = 100;
    public const byte GhostMovementFlag = 1 << 6;
    public const int RemoteTileMargin = 4;

    public static int? Resolve(int? previous, int? baseMaximum, int? lifeforceSlots,
        bool outOfRange, bool ghost, bool dead)
    {
        if (!outOfRange && ghost) return baseMaximum;
        if (!outOfRange && dead) return previous;
        if (lifeforceSlots is not { } slots || slots is < 0 or > 44) return null;
        int? start = outOfRange ? previous : baseMaximum;
        if (slots == 0) return start;
        if (start is not { } initial || baseMaximum is not { } maximum || maximum < 0) return null;
        // Each positive slot applies integer division twice before scaling; duplicates are intentional.
        long value = initial + (long)(maximum / 5 / 20 * 20) * slots;
        return value is >= 0 and <= int.MaxValue ? (int)value : null;
    }

    public static bool IsRemoteOutOfRange(float x, float y, int width, int height, int worldWidth, int worldHeight)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) return true;
        int tileX = (int)(x + width / 2) / 16;
        int tileY = (int)(y + height / 2) / 16;
        return tileX < RemoteTileMargin || tileY < RemoteTileMargin ||
            tileX >= worldWidth - RemoteTileMargin || tileY >= worldHeight - RemoteTileMargin;
    }
}
