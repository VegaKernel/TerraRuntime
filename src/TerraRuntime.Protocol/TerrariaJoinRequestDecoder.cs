using System.Buffers;

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

/// <summary>
/// Exact protocol-326 decoders for the three client messages that advance vanilla's 1 -> 2 -> 3 -> 10 join states.
/// Gameplay validation is deliberately separate from byte decoding.
/// </summary>
public static class TerrariaJoinRequestDecoder
{
    public const int WorldRequestPayloadLength = 0;
    public const int SectionRequestPayloadLength = 9;
    public const int PlayerSpawnPayloadLength = 15;

    public static TerrariaJoinDecodeResult TryDecodeWorldRequest(in TerrariaFrame frame)
    {
        if (frame.MessageId != (byte)TerrariaMessageId.RequestWorldData)
        {
            return TerrariaJoinDecodeResult.WrongMessageId;
        }

        return frame.Payload.Length == WorldRequestPayloadLength
            ? TerrariaJoinDecodeResult.Decoded
            : TerrariaJoinDecodeResult.InvalidPayloadLength;
    }

    public static TerrariaJoinDecodeResult TryDecodeSectionRequest(
        in TerrariaFrame frame,
        out TerrariaSectionBootstrapRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SpawnTileData)
        {
            return TerrariaJoinDecodeResult.WrongMessageId;
        }
        if (frame.Payload.Length != SectionRequestPayloadLength)
        {
            return TerrariaJoinDecodeResult.InvalidPayloadLength;
        }

        var reader = new SequenceReader<byte>(frame.Payload);
        if (!reader.TryReadLittleEndian(out int tileX) ||
            !reader.TryReadLittleEndian(out int tileY) ||
            !reader.TryRead(out byte team))
        {
            return TerrariaJoinDecodeResult.Truncated;
        }

        request = new TerrariaSectionBootstrapRequest(tileX, tileY, team);
        return TerrariaJoinDecodeResult.Decoded;
    }

    public static TerrariaJoinDecodeResult TryDecodePlayerSpawn(
        in TerrariaFrame frame,
        out TerrariaPlayerSpawnRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerSpawn)
        {
            return TerrariaJoinDecodeResult.WrongMessageId;
        }
        if (frame.Payload.Length != PlayerSpawnPayloadLength)
        {
            return TerrariaJoinDecodeResult.InvalidPayloadLength;
        }

        var reader = new SequenceReader<byte>(frame.Payload);
        if (!reader.TryRead(out byte claimedPlayerId) ||
            !reader.TryReadLittleEndian(out short spawnX) ||
            !reader.TryReadLittleEndian(out short spawnY) ||
            !reader.TryReadLittleEndian(out int respawnTimer) ||
            !reader.TryReadLittleEndian(out short deathsPve) ||
            !reader.TryReadLittleEndian(out short deathsPvp) ||
            !reader.TryRead(out byte team) ||
            !reader.TryRead(out byte spawnContext))
        {
            return TerrariaJoinDecodeResult.Truncated;
        }

        request = new TerrariaPlayerSpawnRequest(
            claimedPlayerId,
            spawnX,
            spawnY,
            respawnTimer,
            deathsPve,
            deathsPvp,
            team,
            spawnContext);
        return TerrariaJoinDecodeResult.Decoded;
    }
}
