namespace TerraRuntime.Protocol;

/// <summary>
/// Verified message identifiers consumed by TerraRuntime's protocol boundaries.
/// Extend this catalog only from confirmed Terraria 1.4.5.8 / protocol-326 evidence.
/// </summary>
public enum TerrariaMessageId : byte
{
    Hello = 1,
    Kick = 2,
    PlayerInfo = 3,
    SyncPlayer = 4,
    SyncEquipment = 5,
    RequestWorldData = 6,
    WorldData = 7,
    SpawnTileData = 8,
    StatusTextSize = 9,
    TileSection = 10,
    TileFrameSection = 11,
    PlayerSpawn = 12,
    PlayerControls = 13,
    PlayerActive = 14,
    PlayerHp = 16,
    WorldItemDrop = 21,
    WorldItemOwner = 22,
    ProjectileNew = 27,
    ProjectileDestroy = 29,
    PlayerMana = 42,
    PlayerSpawnSelf = 49,
    FinishedConnectingToServer = 129
}
