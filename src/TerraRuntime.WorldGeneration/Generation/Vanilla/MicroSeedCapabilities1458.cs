using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;
internal static class MicroSeedCapabilities1458
{
    internal static bool Supports(VanillaWorldSeedProfile1458 profile)
    {
        if(profile.Secret==VanillaSecretWorldSeed1458.None)
            return profile.Special is VanillaSpecialWorldSeed1458.None or VanillaSpecialWorldSeed1458.DrunkWorld or VanillaSpecialWorldSeed1458.ForTheWorthy or VanillaSpecialWorldSeed1458.CelebrationMk10 or VanillaSpecialWorldSeed1458.TheConstant or VanillaSpecialWorldSeed1458.NotTheBees or VanillaSpecialWorldSeed1458.Remix or VanillaSpecialWorldSeed1458.NoTraps or (VanillaSpecialWorldSeed1458)255;
        return profile.Special==VanillaSpecialWorldSeed1458.None&&profile.Secret is VanillaSecretWorldSeed1458.MolePeople or VanillaSecretWorldSeed1458.IAmError or VanillaSecretWorldSeed1458.MoreTrapsPlease or VanillaSecretWorldSeed1458.RainbowRoad or (VanillaSecretWorldSeed1458.MolePeople|VanillaSecretWorldSeed1458.IAmError) or (VanillaSecretWorldSeed1458.MoreTrapsPlease|VanillaSecretWorldSeed1458.MolePeople|VanillaSecretWorldSeed1458.RainbowRoad|VanillaSecretWorldSeed1458.NegativeInfinity);
    }
}
