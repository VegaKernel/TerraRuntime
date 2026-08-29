from pathlib import Path


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one replacement target, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


Path("src/TerraRuntime.HostContracts/IServerPlayerOperations.cs").write_text('''using TerraRuntime.Contracts.Runtime;

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
/// Trusted-host lifecycle and semantic control surface for connection-free runtime-owned players. Creation reserves a
/// normal Terraria player slot from the same generation-safe pool used by network connections; callers never receive
/// mutable state or direct final position/velocity writes.
/// </summary>
public interface IServerPlayerOperations
{
    ValueTask<ServerPlayerCreateResult> CreateAsync(
        ServerPlayerId id,
        float positionX,
        float positionY,
        CancellationToken cancellationToken = default);

    ValueTask<bool> SetHorizontalIntentAsync(
        ServerPlayerId id,
        ServerPlayerHorizontalIntent intent,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DespawnAsync(
        ServerPlayerId id,
        CancellationToken cancellationToken = default);
}
''', encoding="utf-8")

Path("src/TerraRuntime/RuntimeServerPlayerOperations.cs").write_text('''using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime;

internal sealed record ServerPlayerCreateRuntimeCommand(
    ServerPlayerId Id,
    float PositionX,
    float PositionY,
    TaskCompletionSource<ServerPlayerCreateResult> Completion) : RuntimeCommand;

internal sealed record ServerPlayerHorizontalIntentRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerHorizontalIntent Intent,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record ServerPlayerDespawnRuntimeCommand(
    ServerPlayerId Id,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

/// <summary>
/// Authoritative-thread owner of server-player slot leases, semantic control intent and connection-free state. A live
/// server player keeps its shared slot lease for its entire lifetime. Control state is keyed by the exact reusable
/// <see cref="PlayerHandle"/> generation, so despawn/reuse cannot transfer stale input to a replacement player.
/// </summary>
internal sealed class RuntimeServerPlayerCommandService
{
    private readonly RuntimeServerPlayerSlotRegistry identities;
    private readonly RuntimeServerPlayerStateStore states;
    private readonly Dictionary<ServerPlayerId, RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease> leases = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerHorizontalIntent> horizontalIntents = [];

    public RuntimeServerPlayerCommandService(
        RuntimeServerPlayerSlotRegistry identities,
        RuntimeServerPlayerStateStore states)
    {
        this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
        this.states = states ?? throw new ArgumentNullException(nameof(states));
    }

    public bool TryApply(RuntimeCommand command)
    {
        switch (command)
        {
            case ServerPlayerCreateRuntimeCommand create:
                create.Completion.TrySetResult(Create(create.Id, create.PositionX, create.PositionY));
                return true;

            case ServerPlayerHorizontalIntentRuntimeCommand horizontal:
                horizontal.Completion.TrySetResult(SetHorizontalIntent(horizontal.Id, horizontal.Intent));
                return true;

            case ServerPlayerDespawnRuntimeCommand despawn:
                despawn.Completion.TrySetResult(Despawn(despawn.Id));
                return true;

            default:
                return false;
        }
    }

    public ServerPlayerCreateResult Create(ServerPlayerId id, float positionX, float positionY)
    {
        if (!id.IsAssigned)
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.InvalidId, default);
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY))
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.InvalidPosition, default);
        if (leases.ContainsKey(id))
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.AlreadyExists, default);

        ServerPlayerSlotAcquireResult acquire = identities.TryAcquire(
            id,
            out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease);
        if (acquire != ServerPlayerSlotAcquireResult.Acquired || lease is null)
        {
            return new ServerPlayerCreateResult(
                acquire switch
                {
                    ServerPlayerSlotAcquireResult.InvalidId => ServerPlayerCreateStatus.InvalidId,
                    ServerPlayerSlotAcquireResult.DuplicateId => ServerPlayerCreateStatus.AlreadyExists,
                    ServerPlayerSlotAcquireResult.NoAvailableSlot => ServerPlayerCreateStatus.NoAvailableSlot,
                    _ => throw new InvalidOperationException("Unknown server-player slot acquisition result.")
                },
                default);
        }

        if (!states.TrySpawn(id, positionX, positionY, out PlayerStateSnapshot snapshot))
        {
            lease.Dispose();
            throw new InvalidOperationException("Server-player identity was acquired but authoritative state could not be created.");
        }

        leases.Add(id, lease);
        return new ServerPlayerCreateResult(ServerPlayerCreateStatus.Created, snapshot.Player);
    }

    public bool SetHorizontalIntent(ServerPlayerId id, ServerPlayerHorizontalIntent intent)
    {
        if (!id.IsAssigned ||
            !IsValidHorizontalIntent(intent) ||
            !leases.TryGetValue(id, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease) ||
            !states.TryGet(lease.Player, out _))
        {
            return false;
        }

        if (intent == ServerPlayerHorizontalIntent.Stop)
            horizontalIntents.Remove(lease.Player);
        else
            horizontalIntents[lease.Player] = intent;

        return true;
    }

    public ServerPlayerHorizontalIntent GetHorizontalIntent(PlayerHandle player) =>
        player.IsAssigned && horizontalIntents.TryGetValue(player, out ServerPlayerHorizontalIntent intent)
            ? intent
            : ServerPlayerHorizontalIntent.Stop;

    public bool Despawn(ServerPlayerId id)
    {
        if (!id.IsAssigned || !leases.TryGetValue(id, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease))
            return false;

        if (!states.TryRemove(lease.Player, out _))
        {
            throw new InvalidOperationException(
                "A live server-player lease lost its authoritative state before despawn.");
        }

        horizontalIntents.Remove(lease.Player);
        leases.Remove(id);
        lease.Dispose();
        return true;
    }

    private static bool IsValidHorizontalIntent(ServerPlayerHorizontalIntent intent) =>
        intent is ServerPlayerHorizontalIntent.Left or
            ServerPlayerHorizontalIntent.Stop or
            ServerPlayerHorizontalIntent.Right;
}

/// <summary>
/// Trusted-host facade that serializes server-player lifecycle and semantic control through the authoritative command
/// queue. Once accepted by the queue, completion is intentionally not cancellable to avoid an ambiguous maybe-applied
/// control mutation.
/// </summary>
internal sealed class RuntimeServerPlayerOperations : IServerPlayerOperations
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeServerPlayerOperations(IGameCommandIngress<RuntimeCommand> ingress)
    {
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public async ValueTask<ServerPlayerCreateResult> CreateAsync(
        ServerPlayerId id,
        float positionX,
        float positionY,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ServerPlayerCreateResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerCreateRuntimeCommand(id, positionX, positionY, completion)))
        {
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.QueueRejected, default);
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> SetHorizontalIntentAsync(
        ServerPlayerId id,
        ServerPlayerHorizontalIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerHorizontalIntentRuntimeCommand(id, intent, completion)))
        {
            throw new InvalidOperationException(
                "The authoritative command queue rejected the server-player horizontal intent command.");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> DespawnAsync(
        ServerPlayerId id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerDespawnRuntimeCommand(id, completion)))
        {
            throw new InvalidOperationException("The authoritative command queue rejected the server-player despawn command.");
        }

        return await completion.Task.ConfigureAwait(false);
    }
}
''', encoding="utf-8")

Path("src/TerraRuntime/VanillaServerPlayerDryPhysicsStepper.cs").write_text('''using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// One authoritative dry-world physics step for the verified ordinary TerrariaServer 1.4.5.8 player path.
/// This slice owns source-backed baseline horizontal input, the base hitbox, gravity/fall-speed clamp,
/// walk-down-slope, tile collision, position advance and post-move slope collision. Mounts, liquids,
/// StepUp/StepDown and jump-control state remain outside this slice.
/// </summary>
internal sealed class VanillaServerPlayerDryPhysicsStepper
{
    internal const int PlayerWidth = 20;
    internal const int PlayerHeight = 42;
    internal const float Gravity = 0.4f;
    internal const float MaximumFallSpeed = 10f;

    private readonly WorldTileStore tiles;

    public VanillaServerPlayerDryPhysicsStepper(WorldTileStore tiles)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    public bool TryStep(
        in PlayerStateSnapshot player,
        out ServerPlayerDryPhysicsStepResult next) =>
        TryStepCore(in player, player.VelocityX, out next);

    public bool TryStep(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent,
        out ServerPlayerDryPhysicsStepResult next)
    {
        if (!IsValidHorizontalIntent(horizontalIntent))
        {
            next = default;
            return false;
        }

        float velocityX = VanillaServerPlayerHorizontalControl.Apply(
            player.VelocityX,
            player.VelocityY,
            horizontalIntent);
        return TryStepCore(in player, velocityX, out next);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        out ServerPlayerDryPhysicsStepResult next)
    {
        if (!player.Player.IsAssigned ||
            player.IsDead ||
            player.MountType != 0 ||
            !float.IsFinite(player.PositionX) ||
            !float.IsFinite(player.PositionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(player.VelocityY))
        {
            next = default;
            return false;
        }

        float velocityY = Math.Min(player.VelocityY + Gravity, MaximumFallSpeed);

        velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            player.PositionX,
            player.PositionY,
            velocityX,
            velocityY,
            PlayerWidth,
            PlayerHeight,
            Gravity);

        float preCollisionVelocityX = velocityX;
        float preCollisionVelocityY = velocityY;
        VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
            tiles,
            player.PositionX,
            player.PositionY,
            velocityX,
            velocityY,
            PlayerWidth,
            PlayerHeight,
            fallThrough: false,
            fall2: false);

        float positionX = player.PositionX + collision.VelocityX;
        float positionY = player.PositionY + collision.VelocityY;
        VanillaSlopeCollisionResult slope = VanillaWorldSlopeCollision.Resolve(
            tiles,
            positionX,
            positionY,
            collision.VelocityX,
            collision.VelocityY,
            PlayerWidth,
            PlayerHeight,
            fall: false);

        next = new ServerPlayerDryPhysicsStepResult(
            slope.PositionX,
            slope.PositionY,
            slope.VelocityX,
            slope.VelocityY,
            CollideX: preCollisionVelocityX != collision.VelocityX,
            CollideY: preCollisionVelocityY != collision.VelocityY,
            collision.HitFloor,
            collision.HitCeiling);
        return true;
    }

    private static bool IsValidHorizontalIntent(ServerPlayerHorizontalIntent intent) =>
        intent is ServerPlayerHorizontalIntent.Left or
            ServerPlayerHorizontalIntent.Stop or
            ServerPlayerHorizontalIntent.Right;
}

internal readonly record struct ServerPlayerDryPhysicsStepResult(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    bool CollideX,
    bool CollideY,
    bool HitFloor,
    bool HitCeiling);
''', encoding="utf-8")

state = Path("src/TerraRuntime/ServerRuntimeState.cs")
replace_once(
    state,
    '''            PlayerStateSnapshot player = _serverPlayerSnapshots[index];
            if (!_serverPlayerDryPhysics.TryStep(in player, out ServerPlayerDryPhysicsStepResult next))
                continue;
''',
    '''            PlayerStateSnapshot player = _serverPlayerSnapshots[index];
            ServerPlayerHorizontalIntent horizontalIntent =
                _serverPlayerCommands?.GetHorizontalIntent(player.Player) ?? ServerPlayerHorizontalIntent.Stop;
            if (!_serverPlayerDryPhysics.TryStep(
                    in player,
                    horizontalIntent,
                    out ServerPlayerDryPhysicsStepResult next))
            {
                continue;
            }
''')

print("G6 server-player horizontal intent wiring applied")
