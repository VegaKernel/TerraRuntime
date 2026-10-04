using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source AI_087 defaults and the pinned Jungle Mimic stuff-cannon material set.</summary>
public static class VanillaBigMimicNpcCatalog1458
{
    public const int DefinitionCount = 4;
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Create(VanillaNpcIds.BigMimicCorruption), Create(VanillaNpcIds.BigMimicCrimson),
        Create(VanillaNpcIds.BigMimicHallow), Create(VanillaNpcIds.BigMimicJungle)
    ];
    private static readonly int[] CannonItems =
    [
        2, 3, 61, 836, 409, 593, 664, 169, 370, 1246, 408, 3271, 3277, 3339, 3276, 3272,
        3274, 3275, 3338, 176, 172, 424, 1103, 3087, 3066, 9, 2503, 2504, 619, 911, 621,
        620, 1727, 276, 4564, 751, 1124, 1125, 824, 129, 131, 607, 594, 883, 414, 413,
        609, 4050, 192, 412
    ];
    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions => Definitions;
    public static ReadOnlySpan<int> StuffCannonItems => CannonItems;
    public static bool IsSupported(NpcTypeId type) => type.Value is >= 473 and <= 476;
    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (IsSupported(type))
        {
            definition = Definitions[type.Value - 473];
            return true;
        }
        definition = default;
        return false;
    }
    private static VanillaNpcDefinition Create(NpcTypeId type) => new(type, VanillaNpcAiStyles.BigMimic,
        VanillaNpcBehaviorFamily.BigMimic, VanillaNpcPhysicsFamily.BigMimic, NpcArchetypeRole.Ordinary,
        28, 44, 90, 34, 3500, .1f, 1f, false, false, VanillaNpcSyncAnchor.TopLeft);
}

/// <summary>Owned world collision facts sampled before a retained Big Mimic AI plan.</summary>
public interface IVanillaBigMimicEnvironment1458
{
    bool CanHit(float fromX, float fromY, float toX, float toY);
    bool SolidCollision(float x, float y, int width, int height);
}
