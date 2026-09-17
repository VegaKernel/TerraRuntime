namespace TerraRuntime.World;

/// <summary>
/// The single random draw inside TerrariaServer 1.4.5.8 <c>Liquid.Update</c>. Its three-cell horizontal
/// averaging branch promotes a level of exactly 254 to a full 255 with probability one in thirty:
/// <c>if (num == 254f &amp;&amp; WorldGen.genRand.Next(30) == 0) num = 255f;</c>. The draw is not decoration.
/// A sealed pocket whose water averages to exactly 254 has no other route to 255, and the cell above it keeps
/// handing down a unit through the deliberate full-source exception in the downward-flow step, so without this
/// promotion the pocket oscillates between 254 and 255 forever - active, and re-broadcasting, for the lifetime
/// of the world.
/// </summary>
public interface IVanillaLiquidRandom1458
{
    /// <summary>Evaluates vanilla's <c>WorldGen.genRand.Next(30) == 0</c>.</summary>
    bool NextFullFromNearlyFullLevel();
}

/// <summary>
/// Process-shared default draw for <see cref="IVanillaLiquidRandom1458"/>. Vanilla reads
/// <c>WorldGen.genRand</c>, which is <c>Main.rand</c>; TerraRuntime keeps liquid off the world-generation
/// stream and takes the same one-in-thirty decision from the shared runtime source instead.
/// </summary>
public sealed class VanillaSharedLiquidRandom1458 : IVanillaLiquidRandom1458
{
    public static VanillaSharedLiquidRandom1458 Instance { get; } = new();

    public bool NextFullFromNearlyFullLevel() => Random.Shared.Next(30) == 0;
}
