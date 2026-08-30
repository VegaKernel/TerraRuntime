namespace TerraRuntime;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 chest storage limits used by runtime-owned world-chest creation.
/// Ordinary vanilla containers start with 40 item slots. Protocol 326 represents the per-chest slot count as a byte
/// count-plus-one space, so runtime snapshots and sync remain bounded to 256 slots even when custom storage grows.
/// </summary>
internal static class VanillaChestStorageFacts1458
{
    public const int DefaultItemSlots = 40;
    public const int MaximumProtocolItemSlots = byte.MaxValue + 1;
}
