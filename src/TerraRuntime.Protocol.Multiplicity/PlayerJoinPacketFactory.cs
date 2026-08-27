using global::Multiplicity.Packets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Creates the server-owned protocol 326 packets that advance the initial Terraria join bootstrap.
/// </summary>
public static class PlayerJoinPacketFactory
{
    public static ContinueConnecting CreateContinueConnecting(
        PlayerSlotId slot,
        bool serverSpecialFlag2 = false) =>
        new()
        {
            PlayerId = slot.Value,
            ServerSpecialFlag2 = serverSpecialFlag2
        };

    public static WorldInfo CreateWorldInfo(
        WorldFileData world,
        WorldInfoTransientState transient = default) =>
        WorldInfoPacketMapper.Create(world, transient);
}
