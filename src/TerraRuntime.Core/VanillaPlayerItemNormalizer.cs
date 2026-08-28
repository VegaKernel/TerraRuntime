using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Reproduces the packet-5 normalization that can be applied without loading Terraria item data.
/// Values are pinned to TerrariaServer 1.4.5.8 Item.netDefaults and ItemID.Count.
/// </summary>
public static class VanillaPlayerItemNormalizer
{
    public const short ItemTypeCount = VanillaItemIds.Count;

    // Exact signed Item.netDefaults compatibility bands from TerrariaServer 1.4.5.8. These are wire/file
    // canonicalization rules, so their arithmetic stays here rather than leaking into inventory gameplay.
    private const short LegacyNetIdBand1Min = -18;
    private const short LegacyNetIdBand1Max = -1;
    private const int LegacyNetIdBand1Offset = 3522;
    private const short LegacyNetIdBand2Min = -24;
    private const short LegacyNetIdBand2Max = -19;
    private const int LegacyNetIdBand2Base = 3745;
    private const short LegacyNetIdBand3Min = -48;
    private const short LegacyNetIdBand3Max = -25;
    private const int LegacyNetIdBand3Offset = 3528;

    public static PlayerEquipmentCommitRequest Normalize(in PlayerEquipmentCommitRequest request)
    {
        if (!TryNormalizeNetId(request.ItemNetId, out ItemTypeId itemType) ||
            itemType.IsNone ||
            request.Stack <= 0)
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
            ItemNetId = checked((short)itemType.Value),
            ItemFlags = (byte)(request.ItemFlags & 1)
        };
    }

    /// <summary>Compatibility wrapper for callers that still require the canonical protocol primitive.</summary>
    public static short NormalizeNetId(short itemNetId) =>
        TryNormalizeNetId(itemNetId, out ItemTypeId itemType)
            ? checked((short)itemType.Value)
            : (short)0;

    /// <summary>
    /// Crosses the signed packet-5 net-id representation into validated Terraria 1.4.5.8 item identity.
    /// </summary>
    public static bool TryNormalizeNetId(short itemNetId, out ItemTypeId itemType)
    {
        int normalizedType;
        if ((ushort)itemNetId < ItemTypeCount)
        {
            normalizedType = itemNetId;
        }
        else if (itemNetId is >= LegacyNetIdBand1Min and <= LegacyNetIdBand1Max)
        {
            normalizedType = LegacyNetIdBand1Offset + itemNetId;
        }
        else if (itemNetId is >= LegacyNetIdBand2Min and <= LegacyNetIdBand2Max)
        {
            normalizedType = LegacyNetIdBand2Base - itemNetId;
        }
        else if (itemNetId is >= LegacyNetIdBand3Min and <= LegacyNetIdBand3Max)
        {
            normalizedType = LegacyNetIdBand3Offset + itemNetId;
        }
        else
        {
            itemType = default;
            return false;
        }

        return VanillaItemIds.TryCreate(normalizedType, out itemType);
    }
}
