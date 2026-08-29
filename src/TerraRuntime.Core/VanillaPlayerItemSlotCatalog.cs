namespace TerraRuntime.Core;

/// <summary>
/// TerrariaServer 1.4.5.8 <c>Terraria.ID.PlayerItemSlotID</c> layout.
/// The server accepts the complete bounded slot space but only broadcasts slots marked CanRelay.
/// Packet 5's low slot span is source-verified as 0..58 and Terraria.Player owns a 59-element inventory array.
/// </summary>
public static class VanillaPlayerItemSlotCatalog
{
    public const short InventoryStart = 0;
    public const short InventoryCount = 59;
    public const short InventoryEndExclusive = InventoryStart + InventoryCount;
    public const short InventoryAndEquipmentEndExclusive = 99;
    public const short Bank4Start = 700;
    public const short Count = 990;
    public const int RelayableCount = InventoryAndEquipmentEndExclusive + (Count - Bank4Start);

    public static bool IsValid(short slot) => (ushort)slot < Count;

    public static bool IsInventorySlot(short slot) =>
        (ushort)(slot - InventoryStart) < InventoryCount;

    public static bool CanRelay(short slot) =>
        (ushort)slot < InventoryAndEquipmentEndExclusive ||
        slot >= Bank4Start && slot < Count;
}
