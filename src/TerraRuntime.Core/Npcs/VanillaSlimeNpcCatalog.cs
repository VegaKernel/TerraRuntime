using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>Type-specific AI_001 timer parameters from TerrariaServer 1.4.5.8.</summary>
public readonly record struct VanillaSlimeMotionProfile(float TimerBonus, float JumpTimerBand)
{
    public bool IsValid =>
        float.IsFinite(TimerBonus) &&
        TimerBonus >= 0f &&
        float.IsFinite(JumpTimerBand) &&
        JumpTimerBand < 0f;
}

/// <summary>
/// Hostile AI_001 definitions and movement parameters. Projectile, item-containment, split,
/// transformation and seed-specific branches remain separate capability work.
/// </summary>
public static class VanillaSlimeNpcCatalog
{
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Slime(VanillaNpcIds.MotherSlime, 36, 24, 20, 7, 90, 0.6f, 1.25f),
        Slime(VanillaNpcIds.LavaSlime, 24, 18, 15, 10, 50, 1f, 1.1f),
        Slime(VanillaNpcIds.DungeonSlime, 36, 24, 30, 7, 150, 0.6f, 1.25f),
        Slime(VanillaNpcIds.CorruptSlime, 40, 30, 55, 20, 170, 1f, 1.1f),
        Slime(VanillaNpcIds.IlluminantSlime, 24, 18, 70, 30, 180, 0.85f, 1.05f),
        Slime(VanillaNpcIds.ToxicSludge, 34, 28, 50, 18, 150, 0.8f, 1.1f),
        Slime(VanillaNpcIds.IceSlime, 24, 18, 8, 4, 30, 1f, 1f),
        Slime(VanillaNpcIds.Crimslime, 40, 30, 60, 26, 200, 1f, 1.1f),
        Slime(VanillaNpcIds.SpikedIceSlime, 24, 18, 12, 8, 60, 1f, 1.1f),
        Slime(VanillaNpcIds.SpikedJungleSlime, 24, 18, 28, 8, 65, 1f, 1.15f),
        Slime(VanillaNpcIds.UmbrellaSlime, 38, 26, 10, 5, 35, 0.75f, 1f),
        Slime(VanillaNpcIds.RainbowSlime, 60, 42, 85, 26, 400, 0.3f, 1f),
        Slime(VanillaNpcIds.SlimeMasked, 24, 18, 7, 2, 25, 1f, 1f),
        Slime(VanillaNpcIds.SlimeRibbonWhite, 24, 18, 7, 2, 25, 1f, 1f),
        Slime(VanillaNpcIds.SlimeRibbonYellow, 24, 18, 6, 2, 23, 1f, 0.9f),
        Slime(VanillaNpcIds.SlimeRibbonGreen, 24, 18, 8, 3, 29, 1f, 1.05f),
        Slime(VanillaNpcIds.SlimeRibbonRed, 24, 18, 5, 1, 22, 1f, 0.85f),
        Slime(VanillaNpcIds.SpikedSlime, 24, 18, 14, 5, 50, 1f, 1.1f),
        Slime(VanillaNpcIds.SandSlime, 30, 24, 15, 5, 50, 0.7f, 1f),
        Slime(VanillaNpcIds.QueenSlimeMinionBlue, 24, 18, 40, 35, 150, 1f, 1f),
        Slime(VanillaNpcIds.QueenSlimeMinionPink, 24, 18, 40, 35, 150, 1f, 1f),
        Slime(VanillaNpcIds.GoldenSlime, 24, 18, 5, 5, 300, 1f, 1f),
        Slime(VanillaNpcIds.ShimmerSlime, 24, 18, 20, 5, 80, 1f, 1f)
    ];

    public static int DefinitionCount => Definitions.Length;

    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions => Definitions;

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        foreach (VanillaNpcDefinition candidate in Definitions)
        {
            if (candidate.Type == type)
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }

    public static bool TryGetMotionProfile(NpcTypeId type, out VanillaSlimeMotionProfile profile)
    {
        if (type != VanillaNpcIds.BlueSlime && !TryGetDefinition(type, out _))
        {
            profile = default;
            return false;
        }

        float timerBonus = 0f;
        if (type == VanillaNpcIds.LavaSlime ||
            type == VanillaNpcIds.IlluminantSlime ||
            type == VanillaNpcIds.Crimslime)
        {
            timerBonus = 2f;
        }
        else if (type == VanillaNpcIds.DungeonSlime || type == VanillaNpcIds.GoldenSlime)
        {
            timerBonus = 3f;
        }
        else if (type == VanillaNpcIds.CorruptSlime)
        {
            timerBonus = 4f;
        }
        else if (type == VanillaNpcIds.QueenSlimeMinionBlue)
        {
            timerBonus = 5f;
        }
        else if (type == VanillaNpcIds.QueenSlimeMinionPink)
        {
            timerBonus = 3f;
        }

        float jumpTimerBand = type == VanillaNpcIds.QueenSlimeMinionPink
            ? -500f
            : type == VanillaNpcIds.GoldenSlime
                ? -400f
                : -1000f;
        profile = new VanillaSlimeMotionProfile(timerBonus, jumpTimerBand);
        return true;
    }

    private static VanillaNpcDefinition Slime(
        NpcTypeId type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist,
        float scale) =>
        new(
            type,
            VanillaNpcAiStyles.Slime,
            VanillaNpcBehaviorFamily.SlimeGround,
            VanillaNpcPhysicsFamily.SlimeGround,
            NpcArchetypeRole.Ordinary,
            width,
            height,
            damage,
            defense,
            lifeMax,
            knockBackResist,
            scale,
            NoGravityAtSpawn: false,
            NoTileCollideAtSpawn: false,
            VanillaNpcSyncAnchor.TopLeft);
}
