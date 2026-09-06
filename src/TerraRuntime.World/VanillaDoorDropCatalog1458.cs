using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// TerrariaServer 1.4.5.8 WorldGen.GetDoorItem and CheckTallGate object-drop identities.
/// </summary>
internal static class VanillaDoorDropCatalog1458
{
    public static ItemTypeId TallGateItem { get; } = new(3240);

    public static ItemTypeId GetDoorItem(int style) => new(style switch
    {
        0 => 25,
        1 => 650,
        2 => 651,
        3 => 652,
        >= 4 and <= 8 => 812 + style,
        9 => 837,
        10 => 912,
        12 => 1137,
        13 => 1138,
        14 => 1139,
        15 => 1140,
        16 => 1411,
        17 => 1412,
        18 => 1413,
        19 => 1458,
        >= 20 and <= 23 => 1709 + style - 20,
        24 => 1793,
        25 => 1815,
        26 => 1924,
        27 => 2044,
        28 => 2265,
        29 => 2528,
        30 => 2561,
        31 => 2576,
        32 => 2815,
        33 => 3129,
        34 => 3131,
        35 => 3130,
        36 => 3888,
        37 => 3941,
        38 => 3967,
        39 => 4155,
        40 => 4176,
        41 => 4197,
        42 => 4218,
        43 => 4307,
        44 => 4415,
        45 => 4576,
        46 => 5158,
        47 => 5179,
        48 => 5200,
        49 => 5558,
        50 => 5611,
        51 => 5699,
        52 => 5722,
        53 => 5747,
        54 => 5765,
        55 => 5786,
        56 => 5807,
        57 => 5828,
        58 => 5867,
        59 => 5907,
        60 => 5941,
        61 => 5964,
        62 => 5984,
        63 => 6007,
        64 => 6030,
        65 => 6053,
        66 => 6076,
        67 => 6098,
        68 => 6120,
        _ => 0
    });
}
