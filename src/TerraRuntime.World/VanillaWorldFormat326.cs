namespace TerraRuntime.World;

/// <summary>
/// Terraria 1.4.5.8 world-format constants verified from the official dedicated server decompile.
/// </summary>
public static class VanillaWorldFormat326
{
    public const int SectionCount = 11;
    public const int TileTypeCount = 754;
    public const int WallTypeCount = 367;
    public const int NpcTypeCount = 697;
    public const int TileEntityTypeCount = 11;
    public const int MaximumChestSlots = 8_000;
    public const int MaximumSignSlots = 32_000;
    public const ushort TimersTileType = 144;

    public static bool AllowsSaveCompressionBatching(ushort tileType) =>
        tileType is not 423 and not 520 and not 723 and not 724;
}
