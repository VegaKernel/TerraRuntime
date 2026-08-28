using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Authoritative-loop lifecycle commands for runtime-owned projectiles. Protocol packets never enter this
/// boundary directly: callers must supply gameplay-domain state and generation-safe runtime identities.
/// </summary>
internal sealed record ProjectileSpawnRuntimeCommand(
    ushort Slot,
    ProjectileStateUpdate State,
    TaskCompletionSource<ProjectileSnapshot?>? Completion = null) : RuntimeCommand;

internal sealed record ProjectileUpdateRuntimeCommand(
    ProjectileHandle Projectile,
    ProjectileStateUpdate State) : RuntimeCommand;

internal sealed record ProjectileDespawnRuntimeCommand(
    ProjectileHandle Projectile) : RuntimeCommand;
