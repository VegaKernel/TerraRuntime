namespace TerraRuntime.Application;

/// <summary>
/// One connection's answer to vanilla's broadcast condition
/// <c>Netplay.Clients[i].TileSections[Netplay.GetSectionX(x), Netplay.GetSectionY(y)]</c>.
/// Terraria never relays a world-cell change to a client that has not been handed the containing network
/// section: <c>NetLiquidModule.ChunkChanges.BroadcastingCondition</c> and the legacy
/// <c>NetMessage.sendWater</c> both consult exactly this table. Reads happen on the authoritative world loop
/// while the owning connection thread hands out sections, which is why the contract is a single relaxed bit
/// per section rather than a snapshot.
/// </summary>
internal interface IPlayerSectionVisibility
{
    bool OwnsSectionAtTile(int tileX, int tileY);
}
