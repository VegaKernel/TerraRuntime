using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.HostContracts;

/// <summary>
/// Trusted-host administrative player controls. Mutations cross the authoritative game-loop boundary and are
/// generation-safe; callers never receive mutable runtime player state.
/// </summary>
public interface IPlayerAdministrativeOperations
{
    ValueTask<bool> SetGodModeAsync(
        PlayerHandle player,
        bool enabled,
        CancellationToken cancellationToken = default);

    ValueTask<bool?> GetGodModeAsync(
        PlayerHandle player,
        CancellationToken cancellationToken = default);
}
