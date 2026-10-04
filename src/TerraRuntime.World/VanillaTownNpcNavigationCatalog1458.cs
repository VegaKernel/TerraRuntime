using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>TerrariaServer 1.4.5.8 Collision.StepUp specialChecksMode=1 terrain identities.</summary>
public static class VanillaTownNpcNavigationCatalog1458
{
    public const ushort PlanterBoxType = 380;
    private static ReadOnlySpan<ushort> IgnoredStepUpTypes => [14, 469, 18, 16, 134];
    private static ReadOnlySpan<ushort> AvoidedTypes => [21, 467, 55, 85, 395, 88, 463, 334, 29, 97, 99, 356, 663, 425, 440, 209, 441, 468, 471, 491, 510, 511, 520, 573, 698];
    private static ReadOnlySpan<ushort> InteractableTypes => [17, 77, 133, 12, 665, 639, 26, 695, 35, 36, 55, 395, 471, 698, 21, 467, 29, 97, 88, 99, 463, 491, 33, 372, 174, 49, 646, 100, 173, 78, 79, 94, 96, 101, 50, 707, 103, 282, 106, 114, 125, 171, 172, 207, 215, 220, 219, 244, 228, 237, 247, 128, 269, 354, 355, 377, 287, 378, 390, 302, 405, 406, 411, 425, 209, 441, 468, 452, 454, 455, 457, 462, 470, 475, 494, 499, 505, 511, 510, 520, 543, 565, 573, 597, 598, 617, 621, 464, 642, 699];
    public static bool IsInteractable(TileTypeId type) => InteractableTypes.Contains(checked((ushort)type.Value));
    public static bool IsAvoided(TileTypeId type) => AvoidedTypes.Contains(checked((ushort)type.Value));
    public static bool IsIgnoredByStepUp(TileTypeId type) => IgnoredStepUpTypes.Contains(checked((ushort)type.Value));
}
