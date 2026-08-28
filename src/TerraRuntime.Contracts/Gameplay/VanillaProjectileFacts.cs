namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 projectile facts used by server authority checks.
/// <para>
/// <see cref="IsHostile"/> mirrors the startup <c>Main.projHostile</c> lookup: vanilla constructs every
/// projectile type with <c>Projectile.SetDefaults(type)</c> during process initialization and records the
/// resulting <c>Projectile.hostile</c> value. This is deliberately not a claim that the hostile field can
/// never change later under world-specific behavior.
/// </para>
/// </summary>
public static class VanillaProjectileFacts
{
    // Extracted from official TerrariaServer 1.4.5.8 Projectile.SetDefaults control flow. The source
    // assembly used by the repository reference probe has SHA-256
    // d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e.
    private static readonly ushort[] HostileTypes =
    [
        31, 38, 39, 40, 44, 55, 56, 67, 71, 75, 81, 82, 83, 84, 96, 98, 99, 100, 101, 102,
        108, 109, 110, 115, 128, 129, 164, 174, 176, 177, 179, 180, 184, 185, 186, 188,
        240, 241, 257, 258, 259, 264, 270, 275, 276, 277, 288, 290, 291, 292, 293, 299,
        300, 302, 303, 325, 326, 327, 328, 329, 345, 346, 347, 348, 349, 350, 351, 352,
        384, 385, 386, 435, 436, 437, 438, 447, 448, 449, 450, 452, 454, 455, 456, 462,
        464, 465, 466, 467, 468, 471, 472, 490, 498, 501, 508, 537, 538, 539, 540, 572,
        573, 574, 575, 576, 577, 578, 579, 580, 581, 592, 593, 596, 605, 629, 654, 655,
        657, 658, 662, 670, 671, 672, 673, 674, 675, 676, 681, 682, 683, 685, 686, 687,
        713, 719, 727, 763, 811, 812, 813, 814, 836, 871, 872, 873, 874, 909, 919, 920,
        921, 922, 923, 926, 961, 962, 965, 980, 1001, 1002, 1005, 1007, 1013, 1014,
        1021, 1048, 1049, 1053, 1054, 1055, 1057, 1073, 1078, 1091, 1092
    ];

    public const int HostileTypeCount = 173;

    public static bool IsHostile(ProjectileTypeId type)
    {
        if (!VanillaProjectileIds.IsLiveWireType(type))
            return false;

        return Array.BinarySearch(HostileTypes, checked((ushort)type.Value)) >= 0;
    }
}
