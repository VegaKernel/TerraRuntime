namespace TerraRuntime.Core;

/// <summary>
/// Validates protocol-326 packet-12 scalar ranges before authoritative spawn commit.
/// </summary>
public static class VanillaPlayerSpawnValidator
{
    public const byte TeamCount = 6;
    public const byte SpawnContextCount = 4;

    public static bool IsValid(in PlayerSpawnCommitRequest request) =>
        request.SpawnX >= -1 &&
        request.SpawnY >= -1 &&
        request.RespawnTimer >= 0 &&
        request.DeathsPve >= 0 &&
        request.DeathsPvp >= 0 &&
        request.Team < TeamCount &&
        request.SpawnContext < SpawnContextCount;
}
