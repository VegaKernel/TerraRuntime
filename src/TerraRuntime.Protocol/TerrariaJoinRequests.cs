namespace TerraRuntime.Protocol;

public readonly record struct TerrariaSectionBootstrapRequest(int TileX, int TileY, byte Team);

public readonly record struct TerrariaPlayerSpawnRequest(
    byte ClaimedPlayerId,
    short SpawnX,
    short SpawnY,
    int RespawnTimer,
    short DeathsPve,
    short DeathsPvp,
    byte Team,
    byte SpawnContext);

public enum TerrariaJoinDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Truncated = 3
}
