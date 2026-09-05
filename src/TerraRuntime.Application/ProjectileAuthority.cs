using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal enum ClientProjectileProvenanceResolveResult : byte
{
    NotApplicable = 0,
    Accepted = 1,
    Rejected = 2
}

internal readonly record struct AuthoritativeClientProjectileSpawn(
    ProjectileStateUpdate State,
    RuntimePlayerInventoryMutation? InventoryMutation,
    int ManaCost,
    VanillaLaunchSpeedEnvelope LaunchSpeedEnvelope,
    int UseTimeTicks);

/// <summary>
/// Owns the projectile store, simulation, client commit validation and lifecycle metrics for one world.
/// It is invoked only by the enclosing authoritative world loop.
/// </summary>
internal sealed partial class ProjectileAuthority
{
    private readonly RuntimeProjectileStore projectiles;
    private readonly RuntimeNpcStore npcs;
    private readonly PlayerAuthority players;
    private readonly IRuntimePlayerSlotSnapshotLookup playerSnapshots;
    private readonly RuntimeProjectileStateExecutor executor;
    private readonly RuntimeProjectileExplosionQueue explosions;
    private readonly RuntimeProjectileChildSpawnQueue childSpawns;
    private readonly RuntimeProjectileLiveChildSpawnQueue liveChildSpawns;
    private readonly IProjectileStateStepper? stepper;
    private readonly RuntimeNpcProjectileReflectionPass reflections;
    private readonly RuntimeProjectileReplicationRegistry? replication;
    private readonly Func<long> tickProvider;
    private readonly WorldTileStore? worldTiles;
    private readonly bool expertMode;
    private readonly RuntimeProjectileClientUseCadenceTracker trustedClientUseCadence = new();
    private readonly ProjectileSnapshot[] controlledProjectileBuffer;
    private const byte ControlUseItemFlag = 1 << 5;

    public ProjectileAuthority(
        RuntimeProjectileStore projectiles,
        PlayerAuthority players,
        RuntimeNpcStore npcs,
        IRuntimePlayerSlotSnapshotLookup playerSnapshots,
        IProjectileStateStepper? stepper,
        RuntimeProjectileReplicationRegistry? replication,
        Func<long> tickProvider,
        bool goodWorld = false,
        WorldTileStore? worldTiles = null,
        bool expertMode = false)
    {
        this.projectiles = projectiles;
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.players = players;
        this.playerSnapshots = playerSnapshots ?? throw new ArgumentNullException(nameof(playerSnapshots));
        explosions = new RuntimeProjectileExplosionQueue(projectiles.Capacity);
        childSpawns = new RuntimeProjectileChildSpawnQueue(projectiles.Capacity);
        liveChildSpawns = new RuntimeProjectileLiveChildSpawnQueue(projectiles.Capacity);
        var terminationEffects = new RuntimeProjectileTerminationEffectSink(explosions, childSpawns);
        executor = new RuntimeProjectileStateExecutor(projectiles, liveChildSpawns, terminationEffects);
        this.stepper = stepper;
        reflections = new RuntimeNpcProjectileReflectionPass(npcs, projectiles, playerSnapshots, goodWorld: goodWorld);
        this.replication = replication;
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        this.worldTiles = worldTiles;
        this.expertMode = expertMode;
        controlledProjectileBuffer = new ProjectileSnapshot[projectiles.Capacity];
    }

    public bool TryApply(RuntimeCommand command)
    {
        switch (command)
        {
            case ProjectileSpawnRuntimeCommand spawn:
                ApplySpawn(spawn);
                return true;
            case ProjectileUpdateRuntimeCommand update:
                ApplyUpdate(update);
                return true;
            case ProjectileDespawnRuntimeCommand despawn:
                ApplyDespawn(despawn);
                return true;
            case ClientProjectileUpdateRuntimeCommand update:
                ApplyClientUpdate(update);
                return true;
            case ClientProjectileDestroyRuntimeCommand destroy:
                ApplyClientDestroy(destroy);
                return true;
            default:
                return false;
        }
    }


    public ReadOnlySpan<RuntimeProjectileExplosionEvent> PendingExplosions => explosions.Events;

    public void ApplyReflections() => AppliedReflections += reflections.Tick();

    public bool TryCapture(ProjectileHandle projectile, out ProjectileSnapshot snapshot) =>
        projectiles.TryGet(projectile, out snapshot);

    public long AppliedSpawns { get; private set; }
    public long RejectedSpawns { get; private set; }
    public long AppliedUpdates { get; private set; }
    public long RejectedUpdates { get; private set; }
    public long AppliedDespawns { get; private set; }
    public long RejectedDespawns { get; private set; }
    public long AppliedReflections { get; private set; }
    public long RejectedClientUpdates { get; private set; }
    public long RejectedClientDestroys { get; private set; }
    public long RejectedTrustedClientUpdates { get; private set; }
    public long AcceptedTrustedSteeringInputs { get; private set; }
    public long RejectedTrustedClientDestroys { get; private set; }
    public long PromotedClientProjectileSpawns { get; private set; }
    public long RejectedClientProjectileProvenance { get; private set; }
    public long RelayedUnknownDestroys { get; private set; }
    public ProjectileStateTickSummary LastTick { get; private set; }

    private static ProjectileStateUpdate SnapshotToUpdate(in ProjectileSnapshot current) => new(
        current.Type,
        current.Spawner,
        current.PositionX,
        current.PositionY,
        current.VelocityX,
        current.VelocityY,
        current.Ai,
        current.BannerIdToRespondTo,
        current.Damage,
        current.KnockBack,
        current.OriginalDamage);

}
