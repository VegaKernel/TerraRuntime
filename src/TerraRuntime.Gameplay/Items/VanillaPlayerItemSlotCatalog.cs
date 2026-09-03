namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// TerrariaServer 1.4.5.8 <c>Terraria.ID.PlayerItemSlotID</c> layout.
/// The server accepts the complete bounded slot space but only broadcasts slots marked CanRelay.
/// Packet 5's source-verified low span is 58 ordinary inventory slots followed by one mouse-item slot;
/// <c>SlotReference</c> maps all 0..58 directly into the 59-element <c>Player.inventory</c> array.
/// </summary>
public static class VanillaPlayerItemSlotCatalog
{
    public const short InventoryStart = 0;
    public const short MainInventoryStart = InventoryStart;
    public const short MainInventoryCount = 50;
    public const short MainInventoryEndExclusive = MainInventoryStart + MainInventoryCount;
    public const short CoinSlotStart = MainInventoryEndExclusive;
    public const short CoinSlotCount = 4;
    public const short CoinSlotEndExclusive = CoinSlotStart + CoinSlotCount;
    public const short AmmoSlotStart = CoinSlotEndExclusive;
    public const short AmmoSlotCount = 4;
    public const short AmmoSlotEndExclusive = AmmoSlotStart + AmmoSlotCount;
    public const short OrdinaryInventoryCount = 58;
    public const short OrdinaryInventoryEndExclusive = InventoryStart + OrdinaryInventoryCount;
    public const short InventoryMouseItem = InventoryStart + OrdinaryInventoryCount;
    public const short InventoryCount = OrdinaryInventoryCount + 1;
    public const short InventoryEndExclusive = InventoryStart + InventoryCount;
    public const short ArmorStart = InventoryEndExclusive;
    public const short ArmorCount = 20;
    public const short FunctionalArmorCount = 10;
    public const short BaselineFunctionalArmorCount = 8; // head/body/legs + five ordinary accessory slots
    public const short FunctionalArmorEndExclusive = ArmorStart + FunctionalArmorCount;
    public const short BaselineFunctionalArmorEndExclusive = ArmorStart + BaselineFunctionalArmorCount;
    public const short VanityArmorStart = FunctionalArmorEndExclusive;
    public const short VanityArmorEndExclusive = ArmorStart + ArmorCount;
    public const short DyeStart = VanityArmorEndExclusive;
    public const short DyeCount = 10;
    public const short MiscStart = DyeStart + DyeCount;
    public const short MiscCount = 5;
    public const short MiscDyeStart = MiscStart + MiscCount;
    public const short MiscDyeCount = 5;
    public const short InventoryAndEquipmentEndExclusive = 99;
    public const short Bank4Start = 700;
    public const short Count = 990;
    public const int RelayableCount = InventoryAndEquipmentEndExclusive + (Count - Bank4Start);

    public static bool IsValid(short slot) => (ushort)slot < Count;

    public static bool IsInventorySlot(short slot) =>
        (ushort)(slot - InventoryStart) < InventoryCount;

    public static bool IsOrdinaryInventorySlot(short slot) =>
        (ushort)(slot - InventoryStart) < OrdinaryInventoryCount;

    public static bool IsMainInventorySlot(short slot) =>
        (ushort)(slot - MainInventoryStart) < MainInventoryCount;

    public static bool IsCoinSlot(short slot) =>
        (ushort)(slot - CoinSlotStart) < CoinSlotCount;

    public static bool IsAmmoSlot(short slot) =>
        (ushort)(slot - AmmoSlotStart) < AmmoSlotCount;

    public static bool IsMouseItemSlot(short slot) => slot == InventoryMouseItem;

    public static bool IsFunctionalArmorSlot(short slot) =>
        (ushort)(slot - ArmorStart) < FunctionalArmorCount;

    public static bool IsBaselineFunctionalArmorSlot(short slot) =>
        (ushort)(slot - ArmorStart) < BaselineFunctionalArmorCount;

    public static bool IsVanityArmorSlot(short slot) =>
        (ushort)(slot - VanityArmorStart) < FunctionalArmorCount;

    public static bool CanRelay(short slot) =>
        (ushort)slot < InventoryAndEquipmentEndExclusive ||
        slot >= Bank4Start && slot < Count;
}
