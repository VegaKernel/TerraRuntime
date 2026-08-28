namespace TerraRuntime.Protocol;

/// <summary>
/// Verified message identifiers needed by the initial connection pipeline.
/// Extend this catalog only from confirmed Terraria 1.4.5.8 protocol evidence.
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
    PlayerMana = 42,
    PlayerSpawnSelf = 49
}
