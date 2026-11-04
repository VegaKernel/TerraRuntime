using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// How many shared RNG draws TerrariaServer 1.4.5.8 <c>WorldGen.KillTile</c> spends on the dust a broken tile
/// makes, per tile identity, while a world is being generated.
/// </summary>
/// <remarks>
/// <para>
/// This is not a cosmetic detail that a dedicated server skips. <c>KillTile</c> asks
/// <c>KillTile_GetTileDustAmount</c> for a particle count - ten for almost everything - and calls
/// <c>KillTile_MakeTileDust</c> that many times; several dust identities are chosen with a draw, and the
/// corruption dust that every corrupt tile makes is one of them. Breaking a single corrupt plant therefore
/// moves the shared stream by ten, and a pass that breaks one and then plants over it reads a different world
/// from the one the source would have produced. Nothing about this is visible in the tiles the kill leaves
/// behind, which is why it went unnoticed until a flower patch was measured over a field of corrupt plants.
/// </para>
/// <para>
/// Frame-zero table values are measured, not derived: the probe breaks one tile of every identity in the pinned official
/// assembly and counts the draws, twice over - once through the whole of <c>KillTile</c> and once through its
/// dust helpers alone. The two agree for every identity where both can be read, which is what says the cost is
/// the dust and nothing else. It measures the unfailed break, which is the only one generation makes:
/// <c>KillTile</c> raises <c>fail</c> only for the locked doors <c>CheckTileBreakability</c> answers with one,
/// and a failed break asks for three particles instead of ten. Identities whose measurement the official method
/// cut short by throwing are recorded as unknown, and this refuses them rather than guessing - a pass that
/// breaks one has to be measured before it can be trusted. Six identities answer differently for different
/// frames - the multi-cell objects whose origin the kill re-derives - and their frame-zero cost is what is
/// recorded here. Contextual dust needs its own source-backed dispatch. Frame-dependent reward argument
/// evaluation belongs to its object-framing caller and is separate from this dust baseline.
/// Types <c>178</c> and <c>352</c> are later corrections: the initial headless probe had null elements in its
/// dust pool. Corrected original fixtures with a complete dust pool measure zero draws for178 and ten
/// for352. The latter agrees across three seeds/four frames in both whole kills and isolated dust helpers,
/// including the complete physical RNG state.
/// </para>
/// The remaining24 formerly unknown identities were remeasured with actual initialized Main entities and
/// Netplay.23 consume no shared samples; type314 evaluates three particle arguments for each of ten dust
/// particles. Whole kills and isolated dust helpers agree across four seeds, twelve frames and two headers.
/// </remarks>
internal static class GenerationKillTileDust1458
{
    /// <summary>Draws per kill, indexed by tile identity. <c>255</c> means the cost was never measured.</summary>
    private static ReadOnlySpan<byte> DrawsPerKill =>
    [
        0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10,
        10, 10, 10, 10, 0, 0, 0, 10, 10, 0, 10, 10, 0, 10, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 10, 0,
        0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 10, 10, 10, 10, 10, 10, 10, 0, 0, 10, 10, 0,
        10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 10, 0,
        0, 0, 30, 0, 10, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 10, 10,
        10, 10, 10, 10, 10, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 10, 0, 0, 0, 0, 0, 10, 10, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 10, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 10, 10, 10, 10, 10, 10,
        0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 10, 0, 0, 0, 10, 10, 10, 0, 0, 0, 0, 0, 10, 10,
        0, 10, 10, 10, 10, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 10, 10, 10, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 10, 0, 10, 10, 10, 10, 10, 10, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 10, 10,
        10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 0, 0, 0, 0, 0, 0, 10, 10, 0, 0, 0,
        0, 0, 0, 0, 0, 10, 0, 0, 10, 10, 20, 0, 0, 0, 0, 0, 10, 10, 0, 10, 10, 10, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10,
        10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0
    ];

    /// <summary>
    /// Spends what the source spends on one tile's dust. The values are discarded: a <c>UnifiedRandom</c> draw
    /// consumes one sample whatever bound it is given, so only the count reaches the next caller.
    /// </summary>
    public static void Consume(IWorldGenerationVanillaRandom random, ushort type, short frameX = 0)
    {
        int draws = For(type, frameX);
        for (int i = 0; i < draws; i++)
            random.Next(2);
    }

    /// <summary>The measured draw count, or a refusal for an identity nobody has measured.</summary>
    public static int For(ushort type, short frameX = 0)
    {
        // GetTileDust samples the corrupt bands, but uses fixed dust5 for crimson bands.
        if (type is 26 or 695 && frameX >= 54 || type is 31 or 696 && frameX >= 36)
            return 0;

        if (type >= DrawsPerKill.Length)
            return 0;

        byte draws = DrawsPerKill[type];
        if (draws == UnknownCost)
        {
            throw new NotSupportedException(
                $"WorldGen.KillTile's dust cost for tile {type} was never measured against the official " +
                "build, so breaking one during generation would move the shared RNG by an unknown amount. " +
                "Measure it with the killdust probe and regenerate GenerationKillTileDust1458.");
        }

        return draws;
    }

    private const byte UnknownCost = 255;
}
