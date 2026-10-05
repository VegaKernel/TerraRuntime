using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>Source 1.4.5.8 cobweb cosmetic frames, captured from TileFrameCosmetic for every attachment mask/bank.</summary>
public static class VanillaCobwebFrames1458
{
    private const int FrameStepPixels = 18;
    private const int TileIdentityCount = 754;
    private const int FrameCoordinateMask = 15;
    private const int FrameCoordinateShift = 4;
    // NW,N,NE,W,E,SW,S,SE. Three source frameNumber banks per mask; coordinates use 18-pixel units.
    private static readonly byte[] Frames = Convert.FromHexString(
        "393A3B393A3B363738363738393A3B393A3B3637383637380C1C2C0C1C2C4143454143450C1C2C0C1C2C41434541434509192909192940424440424409192909" +
        "1929404244404244464748464748212223212223464748464748212223212223393A3B393A3B363738363738393A3B393A3B3637383637380C1C2C0C1C2C4143" +
        "454143450C1C2C0C1C2C414345414345091929091929404244404244091929091929404244404244464748464748212223212223464748464748212223212223" +
        "06070806070805152505152506070806070805152505152531333531333504142404142431333531333504142404142430323430323400102000102030323430" +
        "32340010200010200102030102031617182627280102030102032627282627280607080607080515250515250607080607080515250515253133353133350414" +
        "240414243133353133350414240414243032343032340010200010203032343032340010200010200102030102031617180B1B2B010203010203111213111213" +
        "393A3B393A3B363738363738393A3B393A3B3637383637380C1C2C0C1C2C4143454143450C1C2C0C1C2C41434541434509192909192940424440424409192909" +
        "1929404244404244464748464748212223212223464748464748212223212223393A3B393A3B363738363738393A3B393A3B3637383637380C1C2C0C1C2C4143" +
        "454143450C1C2C0C1C2C414345414345091929091929404244404244091929091929404244404244464748464748212223212223464748464748212223212223" +
        "06070806070805152505152506070806070805152505152531333531333504142404142431333531333504142404142430323430323400102000102030323430" +
        "32340010200010200102030102031617181112130102030102030A1A2A1112130607080607080515250515250607080607080515250515253133353133350414" +
        "24041424313335313335041424041424303234303234001020001020303234303234001020001020010203010203161718111213010203010203111213111213");
    // Exact Main.tileNoAttach metadata, including identities outside the framed 3x3 square.
    private static readonly byte[] NoAttach = Convert.FromHexString("18E43F08000004000000C0FF6F4004004000000000000000000000000000000000000000000000000000000000000000580000000008F80200003800C0070200000000000000301710400E0080100000000000000000000009000000000000");
    private static readonly byte[] FrameImportant = Convert.FromHexString("38FC3FBD1E04862080E7FEFFFF470660F3EF21002078040F0082961F98FAFF4000E0F8EFFFFF7FF419C00E20DC1FF017FC0F607C983BF83FF0E3FF18F16F0AE6C0FF3EC4FBDFB13FF8FFFFFFFFFBE1A5FD7F97030000E0FDEF1F7B20008003");

    public static bool TryGetFrameImportant(ushort type, out bool important)
    {
        important = false;
        if (type >= TileIdentityCount) return false;
        important = (FrameImportant[type >> 3] & (1 << (type & 7))) != 0;
        return true;
    }

    public static bool TryGetFrame(byte mask, byte bank, out short frameX, out short frameY)
    {
        frameX = frameY = 0;
        if (bank > 2) return false;
        byte frame = Frames[mask * 3 + bank];
        frameX = (short)((frame & FrameCoordinateMask) * FrameStepPixels);
        frameY = (short)((frame >> FrameCoordinateShift) * FrameStepPixels);
        return true;
    }

    public static bool TryGetAttachmentMask(ReadOnlySpan<WorldTile> neighbors, out byte mask, bool invisibleBlock = false)
    {
        mask = 0;
        if (neighbors.Length != 8) return false;
        for (int bit = 0; bit < neighbors.Length; bit++)
        {
            var cell = neighbors[bit];
            if (!cell.IsActive) continue;
            // Shapes have independent slope/half-brick merge rules; do not reinterpret imported shapes.
            if (cell.Shape != 0 || cell.Type >= TileIdentityCount) return false;
            if ((NoAttach[cell.Type >> 3] & (1 << (cell.Type & 7))) == 0)
            {
                // Mixed echo coating depends on SceneMetrics/PerspectivePlayer visibility, which
                // is not a dedicated runtime-owned framing fact. Equal coating has no cull ambiguity.
                if (((cell.Flags & WorldTileFlags.InvisibleBlock) != 0) != invisibleBlock) return false;
                mask |= (byte)(1 << bit);
            }
        }
        return true;
    }
}
