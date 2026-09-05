using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Encodes server-owned protocol-326 join/bootstrap frames while keeping Multiplicity packet models inside the
/// protocol adapter. Runtime composition consumes only validated frame bytes and never needs a Multiplicity type.
/// </summary>
public static class PlayerJoinFrameEncoder
{
    private const string ReceivingTileDataLocalizationKey = "LegacyInterface.44";

    public static byte[] EncodeContinueConnecting(
        PlayerSlotId slot,
        bool serverSpecialFlag2 = false)
    {
        var packet = new ContinueConnecting
        {
            PlayerId = slot.Value,
            ServerSpecialFlag2 = serverSpecialFlag2
        };

        return packet.ToArray();
    }

    public static byte[] EncodeWorldInfo(
        WorldFileData world,
        WorldInfoTransientState transient = default,
        WorldInfoRuntimeState? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        return (WorldInfoPacketMapper.Create(world, transient, runtime)).ToArray();
    }

    public static byte[] EncodeStatus(int sectionCount)
    {
        if (sectionCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sectionCount));

        var packet = new Status
        {
            StatusMax = sectionCount,
            StatusText = new NetworkText
            {
                TextMode = (byte)NetworkText.Mode.LocalizationKey,
                Text = ReceivingTileDataLocalizationKey
            },
            SpecialFlags = StatusSpecialFlags.None
        };

        return packet.ToArray();
    }
}
