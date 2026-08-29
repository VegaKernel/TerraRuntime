namespace TerraRuntime.Core;

/// <summary>
/// TerrariaServer 1.4.5.8 <c>Terraria.ID.PlayerItemSlotID</c> layout.
/// The server accepts the complete bounded slot space but only broadcasts slots marked CanRelay.
/// Packet 5's source-verified low span is 58 ordinary inventory slots followed by one mouse-item slot;
/// <c>SlotReference</c> maps all 0..58 directly into the 59-element <c>Player.inventory</c> array.
/// </summary>
public static class VanillaPlayerItemSlotCatalog
{
    public const short InventoryStart = 0;
    public const short OrdinaryInventoryCount = 58;
    public const short InventoryMouseItem = InventoryStart + OrdinaryInventoryCount;
    public const short InventoryCount = OrdinaryInventoryCount + 1;
    public const short InventoryEndExclusive = InventoryStart + InventoryCount;
    public const short InventoryAndEquipmentEndExclusive = 99;
    public const short Bank4Start = 700;
    public const short Count = 990;
    public const int RelayableCount = InventoryAndEquipmentEndExclusive + (Count - Bank4Start);

    public static bool IsValid(short slot) => (ushort)slot < Count;

    public static bool IsInventorySlot(short slot) =>
        (ushort)(slot - InventoryStart) < InventoryCount;

    public static bool IsOrdinaryInventorySlot(short slot) =>
        (ushort)(slot - InventoryStart) < OrdinaryInventoryCount;

    public static bool CanRelay(short slot) =>
        (ushort)slot < InventoryAndEquipmentEndExclusive ||
        slot >= Bank4Start && slot < Count;
}
