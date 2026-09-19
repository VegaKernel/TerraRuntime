"""Regenerate VanillaLoadingObjectFootprint1458 from the watercheck probe's sweep.

The probe (.cache/watercheck-probe) stands one object of every multi-cell identity on stone inside the pinned
official TerrariaServer 1.4.5.8 and runs WorldGen.WaterCheck - the pass a world runs as it loads - twice: once
dry, to prove the object survives on its own, and once flooded. Usage:

    python tools/ci/generate_loading_object_footprints.py <watercheck.tsv>

Each row is `type, liquid, width, height, strideX, strideY, dry, wet`, where the two observations are
`threw:objectIntact:objectCleared:outsideChanged`. An identity earns a place in the table only when the dry run
leaves it whole and touches nothing, and the flooded run clears exactly its own cells and changes no other
cell's identity. Identities that carry persistent metadata are excluded whatever the sweep says: removing one
means removing a chest, a sign or a tile entity with it, and that is not this table's business.
"""

import io
import sys

# Chest, sign and tile-entity identities. The loader refuses these outright.
METADATA = {21, 55, 85, 88, 378, 395, 425, 467, 470, 471, 475, 520, 573, 597, 616}

# Identities whose footprint depends on the style, so no single rectangle can stand for them.
VARIED = {14, 165, 185, 233}

HEADER = '''using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// The rectangle TerrariaServer 1.4.5.8 <c>WorldGen.WaterCheck</c> removes when a liquid destroys a multi-cell
/// object while a world is loading, and the frame strides its styles are laid out with, per tile identity.
/// </summary>
/// <remarks>
/// <para>
/// A world that generates cleanly can still hold an object standing in lava, because generation places objects
/// and floods caves in separate passes. The source resolves that as it loads: <c>WaterCheck</c> calls a plain
/// <c>KillTile</c> on the one cell it found, and the object's own validator removes the rest through the
/// <c>SquareTileFrame</c> that follows. The cells that disappear are exactly the object's own.
/// </para>
/// <para>
/// That last sentence is measured, not assumed. The probe stands one object of every identity that has a
/// rectangular <c>TileObjectData</c> footprint, runs the real pass dry and then flooded, and compares the whole
/// world: every death case clears the object's own cells and leaves every other cell's identity untouched, and
/// none of them throws. Identities that carry a chest, a sign or a tile entity are excluded regardless, because
/// removing one means removing its metadata too; identities whose footprint changes with the style are excluded
/// because no single rectangle describes them, and the loader names those cases itself.
/// </para>
/// <para>
/// The strides are why this is not simply a width and a height. A style does not always advance the frame by
/// the object's own size: a chair is one cell wide and two tall but its styles step 40 pixels down, and a
/// workbench is two cells wide and one tall but its styles step 20. Deriving a cell's position inside its
/// object means taking the frame modulo the stride, not modulo the size.
/// </para>
/// </remarks>
internal static class VanillaLoadingObjectFootprint1458
{
    // Four bytes per identity: width, height, frame stride across, frame stride down. A zero width means the
    // identity has no measured rectangle and the loader must refuse it.
    private static ReadOnlySpan<byte> Records =>
    [
__TABLE__
    ];

    /// <summary>The measured rectangle and style strides, or false for an identity the sweep never cleared.</summary>
    public static bool TryGet(TileTypeId type, out int width, out int height, out int strideX, out int strideY)
    {
        width = height = strideX = strideY = 0;
        int value = type.Value;
        if ((uint)value >= (uint)(Records.Length / 4))
            return false;

        int offset = value * 4;
        if (Records[offset] == 0)
            return false;

        width = Records[offset];
        height = Records[offset + 1];
        strideX = Records[offset + 2];
        strideY = Records[offset + 3];
        return true;
    }
}
'''


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2

    footprints = {}
    with io.open(sys.argv[1], encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            parts = line.split("\t")
            type_id = int(parts[0])
            width, height, stride_x, stride_y = (int(value) for value in parts[2:6])
            dry_threw, dry_intact, _dry_cleared, dry_outside = parts[6].split(":")
            wet_threw, wet_intact, wet_cleared, wet_outside = parts[7].split(":")
            if dry_threw != "0" or dry_intact != "1" or dry_outside != "0":
                continue
            if wet_threw != "0" or wet_intact != "0" or wet_cleared != "1" or wet_outside != "0":
                continue
            if type_id in METADATA or type_id in VARIED:
                continue
            if not 1 <= width <= 255 or not 1 <= height <= 255:
                continue
            if not 1 <= stride_x <= 255 or not 1 <= stride_y <= 255:
                continue
            footprints[type_id] = (width, height, stride_x, stride_y)

    count = max(footprints) + 1
    table = [0] * (count * 4)
    for type_id, record in footprints.items():
        table[type_id * 4:type_id * 4 + 4] = record

    lines = []
    for start in range(0, count * 4, 24):
        chunk = table[start:start + 24]
        lines.append("        " + ", ".join(str(value) for value in chunk) + ",")
    lines[-1] = lines[-1].rstrip(",")

    text = HEADER.replace("__TABLE__", "\n".join(lines))
    with io.open(
        "src/TerraRuntime.World/VanillaLoadingObjectFootprint1458.cs",
        "w",
        encoding="utf-8",
        newline="",
    ) as handle:
        handle.write(text)

    print(f"{len(footprints)} identities with a measured loading footprint, table length {count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
