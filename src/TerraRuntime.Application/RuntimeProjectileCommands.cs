using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

/// <summary>
/// Authoritative-loop lifecycle commands for runtime-owned projectiles. Server-created gameplay state uses
/// generation-safe handles directly. Client packet 27/29 state is carried as protocol-neutral decoded DTOs
/// until the authoritative thread resolves the exact wire key and performs the mutation.
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

internal sealed record ClientProjectileUpdateRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaProjectileUpdateState State) : RuntimeCommand
{
    // An authenticated report may wait for adjacent13. It grants no caller
    // role, source clock, stock, entropy or projectile by itself.
    internal bool LocalItemReportIntent { get; init; }
    internal bool ValidateVanillaFishingClient { get; init; }
}

internal sealed record ClientProjectileDestroyRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaProjectileDestroyState State) : RuntimeCommand;
