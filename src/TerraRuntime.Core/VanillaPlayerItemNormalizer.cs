namespace TerraRuntime.Core;

/// <summary>
/// Reproduces the packet-5 normalization that can be applied without loading Terraria item data.
/// Values are pinned to TerrariaServer 1.4.5.8 Item.netDefaults and ItemID.Count.
/// </summary>
public static class VanillaPlayerItemNormalizer
{
    public const short ItemTypeCount = 6196;

    public static PlayerEquipmentCommitRequest Normalize(in PlayerEquipmentCommitRequest request)
    {
        short itemNetId = NormalizeNetId(request.ItemNetId);
        if (itemNetId == 0 || request.Stack <= 0)
        {
            return request with
            {
                Stack = 0,
                Prefix = 0,
                ItemNetId = 0,
                ItemFlags = 0
            };
        }

        return request with
        {
            ItemNetId = itemNetId,
            ItemFlags = (byte)(request.ItemFlags & 1)
        };
    }

    public static short NormalizeNetId(short itemNetId)
    {
        if ((ushort)itemNetId < ItemTypeCount)
            return itemNetId;
        if (itemNetId is >= -18 and <= -1)
            return checked((short)(3522 + itemNetId));
        if (itemNetId is >= -24 and <= -19)
            return checked((short)(3745 - itemNetId));
        if (itemNetId is >= -48 and <= -25)
            return checked((short)(3528 + itemNetId));

        return 0;
    }
}
