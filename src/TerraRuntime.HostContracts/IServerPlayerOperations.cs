using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.HostContracts;

public enum ServerPlayerCreateStatus : byte
{
    Created = 0,
    InvalidId = 1,
    InvalidPosition = 2,
    AlreadyExists = 3,
    NoAvailableSlot = 4,
    QueueRejected = 5
}

public readonly record struct ServerPlayerCreateResult(
    ServerPlayerCreateStatus Status,
    PlayerHandle Player)
{
    public bool IsCreated => Status == ServerPlayerCreateStatus.Created && Player.IsAssigned;
}

/// <summary>
/// Trusted-host lifecycle surface for connection-free runtime-owned players. Creation reserves a normal Terraria
/// player slot from the same generation-safe pool used by network connections; callers never receive mutable state.
/// </summary>
public interface IServerPlayerOperations
{
    ValueTask<ServerPlayerCreateResult> CreateAsync(
        ServerPlayerId id,
        float positionX,
        float positionY,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DespawnAsync(
        ServerPlayerId id,
        CancellationToken cancellationToken = default);
}
