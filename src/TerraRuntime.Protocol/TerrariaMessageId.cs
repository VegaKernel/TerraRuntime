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
    TileManipulation = 17,
    DoorToggle = 19,
    TileSquare = 20,
    WorldItemDrop = 21,
    WorldItemOwner = 22,
    NpcUpdate = 23,
    ChatMessage = 25,
    ProjectileNew = 27,
    NpcDamage = 28,
    ProjectileDestroy = 29,
    TogglePvp = 30,
    RequestChestOpen = 31,
    SyncChestItem = 32,
    SyncPlayerChest = 33,
    ReleaseWorldItem = 39,
    PlayerMana = 42,
    PlayerTeam = 45,
    RequestSign = 46,
    SignNew = 47,
    LiquidSet = 48,
    PlayerSpawnSelf = 49,
    PlayerBuffs = 50,
    LockAndUnlock = 52,
    SetNpcTalk = 40,
    AddPlayerBuffPvp = 55,
    UniqueTownNpcInfoSyncRequest = 56,
    UpdateNpcHome = 60,
    SpawnBoss = 61,
    TeleportEntity = 65,
    ChestName = 69,
    CatchNpc = 70,
    TeleportRequest = 73,
    ReportInvasionProgress = 78,
    PlaceObject = 79,
    SyncPlayerChestIndex = 80,
    LoadNetModule = 82,
    PlayerHurt = 117,
    PlayerDeathV2 = 118,
    FinishedConnectingToServer = 129,
    WorldItemRemove = 151,

    /// <summary>
    /// The client's latency probe. TerrariaServer 1.4.5.8 echoes it straight back
    /// (<c>NetMessage.TrySendData(154, whoAmI)</c>) and the client turns the round trip into the number it
    /// displays. A server that never replies does not look fast - the client's own
    /// <c>Terraria.Net.Ping.Update</c> keeps raising its reading while it waits, and never sends a second
    /// probe, so the displayed latency climbs forever off one unanswered packet.
    /// </summary>
    Ping = 154,
    SyncChestSize = 155,
    NpcDamageAck = 162
}
