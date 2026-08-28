namespace TerraRuntime.Core;

/// <summary>
/// TerrariaServer 1.4.5.8 <c>Terraria.ID.PlayerItemSlotID</c> layout.
/// The server accepts the complete bounded slot space but only broadcasts slots marked CanRelay.
/// </summary>
public static class VanillaPlayerItemSlotCatalog
{
    public const short InventoryAndEquipmentEndExclusive = 99;
    public const short Bank4Start = 700;
    public const short Count = 990;
    public const int RelayableCount = InventoryAndEquipmentEndExclusive + (Count - Bank4Start);

    public static bool IsValid(short slot) => (ushort)slot < Count;

    public static bool CanRelay(short slot) =>
        (ushort)slot < InventoryAndEquipmentEndExclusive ||
        slot >= Bank4Start && slot < Count;
}
