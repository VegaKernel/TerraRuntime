using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>TerrariaServer 1.4.5.8 Collision.StepUp specialChecksMode=1 terrain identities.</summary>
public static class VanillaTownNpcNavigationCatalog1458
{
    public const ushort PlanterBoxType = 380;
    private static ReadOnlySpan<ushort> IgnoredStepUpTypes => [14, 469, 18, 16, 134];
    private static ReadOnlySpan<ushort> AvoidedTypes => [21, 467, 55, 85, 395, 88, 463, 334, 29, 97, 99, 356, 663, 425, 440, 209, 441, 468, 471, 491, 510, 511, 520, 573, 698];
    public static bool IsAvoided(TileTypeId type) => AvoidedTypes.Contains(checked((ushort)type.Value));
    public static bool IsIgnoredByStepUp(TileTypeId type) => IgnoredStepUpTypes.Contains(checked((ushort)type.Value));
}
