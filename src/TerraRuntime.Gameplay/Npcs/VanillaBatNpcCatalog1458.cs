using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source-backed TerrariaServer 1.4.5.8 aiStyle 14 definitions admitted by the deterministic motion slice.</summary>
public static class VanillaBatNpcCatalog1458
{
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Bat(VanillaNpcIds.CaveBat, 22, 18, 13, 2, 16, 0.8f),
        Bat(VanillaNpcIds.JungleBat, 22, 18, 20, 4, 34, 0.8f),
        Bat(VanillaNpcIds.Hellbat, 22, 18, 35, 8, 46, 0.8f, 1.1f),
        Bat(VanillaNpcIds.GiantBat, 26, 20, 45, 16, 100, 0.75f),
        Bat(VanillaNpcIds.IlluminantBat, 26, 20, 75, 30, 200, 0.75f),
        Bat(VanillaNpcIds.IceBat, 22, 22, 18, 6, 30, 0.8f),
        Bat(VanillaNpcIds.LavaBat, 22, 22, 50, 16, 160, 0.6f, 1.15f),
        Bat(VanillaNpcIds.GiantFlyingFox, 38, 34, 80, 24, 220, 0.65f),
        Bat(VanillaNpcIds.SporeBat, 22, 18, 13, 2, 16, 0.8f),
        Bat(VanillaNpcIds.Slimer, 40, 30, 45, 20, 60, 0.8f, 1.1f)
    ];

    public static int DefinitionCount => Definitions.Length;

    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions => Definitions;

    public static bool IsSupportedMotionType(NpcTypeId type) =>
        type == VanillaNpcIds.QueenSlimeMinionPurple || TryGetDefinition(type, out _);

    public static bool UsesBatDoubleAcceleration(NpcTypeId type) =>
        type == VanillaNpcIds.CaveBat ||
        type == VanillaNpcIds.JungleBat ||
        type == VanillaNpcIds.Hellbat ||
        type == VanillaNpcIds.GiantBat ||
        type == VanillaNpcIds.IlluminantBat ||
        type == VanillaNpcIds.IceBat ||
        type == VanillaNpcIds.LavaBat ||
        type == VanillaNpcIds.GiantFlyingFox ||
        type == VanillaNpcIds.SporeBat;

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

    private static VanillaNpcDefinition Bat(
        NpcTypeId type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist,
        float scale = 1f) =>
        new(
            type,
            VanillaNpcAiStyles.Bat,
            VanillaNpcBehaviorFamily.Bat,
            VanillaNpcPhysicsFamily.BatFlight,
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
