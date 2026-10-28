namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Exact AI007 bunny identities sharing the admitted nonresident movement and outer phases.</summary>
public static class VanillaNonresidentBunnyCatalog1458
{
    public static ReadOnlySpan<int> Types => [46, 303, 337, 443, 540, 646, 647, 648, 649, 650, 651, 652];

    // Explosive Bunny and turning squirrels have additional source branches and are not admitted here.
    public static bool IsSupported(int type) => type is 46 or 303 or 337 or 443 or 540 or >= 646 and <= 652;

    // UpdateNPC_BloodMoonTransformations runs before AI007 and converts these five to evil bunnies.
    public static bool IsEvilConvertible(int type) => type is 46 or 303 or 337 or 443 or 540;
}
