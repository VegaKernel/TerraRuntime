"""Regenerate GenerationKillTileDust1458's draw table from the killdust probe's output.

The probe (.cache/killdust-probe) breaks one tile of every identity inside the pinned official
TerrariaServer 1.4.5.8 with world generation in progress, and reports how many shared RNG draws
WorldGen.KillTile consumed. Usage:

    python tools/ci/generate_kill_tile_dust_table.py <killdust.tsv>

The TSV carries one row per (type, frameX, frameY) with the measured draw count, or a negative value
when the official method threw before it could be measured (-100 - draws).
"""

import collections
import io
import sys

UNKNOWN = 255

HEADER = '''using TerraRuntime.Contracts.Gameplay;

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
/// The table is measured, not derived: the probe breaks one tile of every identity in the pinned official
/// assembly and counts the draws. It measures the unfailed break, which is the only one generation makes:
/// <c>KillTile</c> raises <c>fail</c> only for the locked doors <c>CheckTileBreakability</c> answers with one,
/// and a failed break asks for three particles instead of ten. Identities whose measurement the official method
/// cut short by throwing are recorded as unknown, and this refuses them rather than guessing - a pass that
/// breaks one has to be measured before it can be trusted. Six identities answer differently for different
/// frames - the multi-cell objects whose origin the kill re-derives - and their frame-zero cost is what is
/// recorded, because generation does not break them at any other frame.
/// </para>
/// </remarks>
internal static class GenerationKillTileDust1458
{
    /// <summary>Draws per kill, indexed by tile identity. <c>255</c> means the cost was never measured.</summary>
    private static ReadOnlySpan<byte> DrawsPerKill =>
    [
__TABLE__
    ];

    /// <summary>
    /// Spends what the source spends on one tile's dust. The values are discarded: a <c>UnifiedRandom</c> draw
    /// consumes one sample whatever bound it is given, so only the count reaches the next caller.
    /// </summary>
    public static void Consume(IWorldGenerationVanillaRandom random, ushort type)
    {
        int draws = For(type);
        for (int i = 0; i < draws; i++)
            random.Next(2);
    }

    /// <summary>The measured draw count, or a refusal for an identity nobody has measured.</summary>
    public static int For(ushort type)
    {
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
'''


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2

    rows = collections.defaultdict(dict)
    with io.open(sys.argv[1], encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            type_id, frame_x, frame_y, draws = (int(part) for part in line.split("\t"))
            rows[type_id][(frame_x, frame_y)] = draws

    count = max(rows) + 1
    table = []
    for type_id in range(count):
        measured = rows.get(type_id, {})
        zero_frame = measured.get((0, 0))
        if zero_frame is None or zero_frame < 0:
            table.append(UNKNOWN)
        else:
            table.append(zero_frame)

    lines = []
    for start in range(0, count, 24):
        chunk = table[start:start + 24]
        lines.append("        " + ", ".join(str(value) for value in chunk) + ",")
    lines[-1] = lines[-1].rstrip(",")

    text = HEADER.replace("__TABLE__", "\n".join(lines))
    with io.open(
        "src/TerraRuntime.WorldGeneration/Generation/Vanilla/GenerationKillTileDust1458.cs",
        "w",
        encoding="utf-8",
        newline="",
    ) as handle:
        handle.write(text)

    unknown = sum(1 for value in table if value == UNKNOWN)
    print(f"{count} identities, {unknown} unmeasured, "
          f"{sum(1 for value in table if value not in (0, UNKNOWN))} with a cost")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
