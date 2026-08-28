namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Requests immutable player state without exposing the authoritative runtime thread or collections.
/// </summary>
public interface IPlayerStateSnapshotReader
{
    ValueTask<PlayerStateSnapshot?> CaptureAsync(
        PlayerHandle player,
        CancellationToken cancellationToken = default);
}
