using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// TerrariaServer 1.4.5.8 WorldGen.KillTile(fail:true) pick-transform table. Zero means no transform; otherwise the
/// stored value is target TileTypeId + 1 so Dirt (type 0) remains representable without a second bitmap.
/// Raw IDs are isolated in this generated/source-pinned data file rather than spread through gameplay branches.
/// </summary>
internal static class VanillaTileFailedPickTransformData1458
{
    private static readonly ushort[] TargetPlusOne = Build();

    public static TileTypeId? GetTarget(TileTypeId source)
    {
        ushort encoded = TargetPlusOne[source.Value];
        return encoded == 0 ? null : new TileTypeId(encoded - 1);
    }

    private static ushort[] Build()
    {
        var targets = new ushort[VanillaTileIds.Count];

        // Grass families -> Dirt.
        Set(targets, 0, 2, 23, 109, 199, 477, 492);

        // Jungle/mushroom families -> Mud.
        Set(targets, 59, 60, 70, 661, 662);

        // Ash grass -> Ash.
        Set(targets, 57, 633);

        // Main.tileMoss -> Stone.
        Set(targets, 1, 179, 180, 181, 182, 183, 381, 534, 536, 539, 625, 627);

        // TileID.Sets.tileMossBrick -> Gray Brick.
        Set(targets, 38, 512, 513, 514, 515, 516, 517, 535, 537, 540, 626, 628);

        return targets;
    }

    private static void Set(ushort[] targets, ushort target, params int[] sources)
    {
        ushort encoded = checked((ushort)(target + 1));
        foreach (int source in sources)
            targets[source] = encoded;
    }
}
