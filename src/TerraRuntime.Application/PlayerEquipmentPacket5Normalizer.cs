using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

/// <summary>
/// Canonicalizes raw packet-5 equipment fields at the network/application ingress boundary.
/// Signed legacy net-id bands are pinned to TerrariaServer 1.4.5.8 Item.netDefaults; gameplay and Core only see
/// canonical positive item identities after this boundary.
/// </summary>
internal static class PlayerEquipmentPacket5Normalizer
{
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
            !VanillaDefinitionCatalog.IsValidKnownStack(itemType, request.Stack))
        {
            return request with
            {
                Stack = 0,
                Prefix = VanillaPrefixIds.NoneValue,
                ItemNetId = 0,
                ItemFlags = 0
            };
        }

        return request with
        {
            Prefix = VanillaPrefixIds.TryCreate(request.Prefix, out _)
                ? request.Prefix
                : VanillaPrefixIds.NoneValue,
            ItemNetId = checked((short)itemType.Value),
            ItemFlags = (byte)(request.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag)
        };
    }

    /// <summary>
    /// Crosses the signed packet-5 net-id representation into validated Terraria 1.4.5.8 item identity.
    /// </summary>
    public static bool TryNormalizeNetId(short itemNetId, out ItemTypeId itemType)
    {
        int normalizedType;
        if ((ushort)itemNetId < VanillaItemIds.Count)
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
