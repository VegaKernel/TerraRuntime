using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;
using TerraRuntime.Core.Players;

namespace TerraRuntime;

/// <summary>
/// World-writer owner of runtime-controlled players. It owns slot leases, semantic control intent, connection-free
/// state mutation, dry-physics progression and the retained per-generation liquid-contact state used by that physics.
/// A state store can be supplied without lifecycle identities for simulation/query-only scenarios; authoritative
/// create/control/despawn commands are admitted only when the identity registry is present.
/// </summary>
internal sealed class ServerPlayerAuthority
{
    private readonly ServerPlayerStateStore states;
    private readonly ServerPlayerSlotRegistry? identities;
    private readonly IRuntimeServerPlayerEventSink? events;
    private readonly VanillaServerPlayerDryPhysicsStepper? dryPhysics;
    private readonly PlayerStateSnapshot[] snapshots;
    private readonly PlayerHandle[] liquidOwners;
    private readonly VanillaLiquidContactState[] liquidContacts;
    private readonly Dictionary<ServerPlayerId, ServerPlayerSlotRegistry.ServerPlayerSlotLease> leases = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerHorizontalIntent> horizontalIntents = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerJumpIntent> jumpIntents = [];
    private readonly Dictionary<PlayerHandle, VanillaServerPlayerJumpState> jumpStates = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerMovementIntent> movementIntents = [];

    public ServerPlayerAuthority(
        ServerPlayerStateStore states,
        ServerPlayerSlotRegistry? identities = null,
        WorldTileStore? worldTiles = null,
        IRuntimeServerPlayerEventSink? events = null)
    {
        this.states = states ?? throw new ArgumentNullException(nameof(states));
        this.identities = identities;
        this.events = events;
        dryPhysics = worldTiles is null ? null : new VanillaServerPlayerDryPhysicsStepper(worldTiles);
        snapshots = new PlayerStateSnapshot[states.Capacity];
        liquidOwners = new PlayerHandle[states.Capacity];
        liquidContacts = new VanillaLiquidContactState[states.Capacity];
    }

    public bool TryApply(RuntimeCommand command)
    {
        if (identities is null)
            return false;

        switch (command)
        {
            case ServerPlayerCreateRuntimeCommand create:
                create.Completion.TrySetResult(Create(create.Id, create.PositionX, create.PositionY));
                return true;

            case ServerPlayerHorizontalIntentRuntimeCommand horizontal:
                horizontal.Completion.TrySetResult(SetHorizontalIntent(horizontal.Id, horizontal.Intent));
                return true;

            case ServerPlayerJumpIntentRuntimeCommand jump:
                jump.Completion.TrySetResult(SetJumpIntent(jump.Id, jump.Intent));
                return true;

            case ServerPlayerMovementIntentRuntimeCommand movement:
            {
                ServerPlayerMovementIntent intent = movement.Intent;
                movement.Completion.TrySetResult(SetMovementIntent(movement.Id, in intent));
                return true;
            }

            case ServerPlayerAppearanceRuntimeCommand appearance:
            {
                ServerPlayerAppearanceState value = appearance.Appearance;
                appearance.Completion.TrySetResult(SetAppearance(appearance.Id, in value));
                return true;
            }

            case ServerPlayerVitalsRuntimeCommand vitals:
            {
                ServerPlayerVitalsState value = vitals.Vitals;
                vitals.Completion.TrySetResult(SetVitals(vitals.Id, in value));
                return true;
            }

            case ServerPlayerItemRuntimeCommand item:
            {
                ServerPlayerItemState value = item.Item;
                item.Completion.TrySetResult(SetItem(item.Id, in value));
                return true;
            }

            case ServerPlayerDespawnRuntimeCommand despawn:
                despawn.Completion.TrySetResult(Despawn(despawn.Id));
                return true;

            default:
                return false;
        }
    }

    public void TickPhysics(IRuntimePlayerSnapshotLookup players)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (dryPhysics is null)
            return;

        int count = states.CopySnapshots(snapshots);
        for (int index = 0; index < count; index++)
        {
            PlayerStateSnapshot player = snapshots[index];
            ServerPlayerMovementIntent movementIntent = GetMovementIntent(player.Player);
            ServerPlayerHorizontalIntent horizontalIntent;
            ServerPlayerJumpIntent jumpIntent;
            if (movementIntent.Kind != ServerPlayerMovementIntentKind.Stop)
            {
                ServerPlayerMovementIntentResolver.TryResolve(
                    in player,
                    in movementIntent,
                    players,
                    out horizontalIntent,
                    out jumpIntent);
            }
            else
            {
                horizontalIntent = GetHorizontalIntent(player.Player);
                jumpIntent = GetJumpIntent(player.Player);
            }

            VanillaServerPlayerJumpState jumpState = GetJumpState(player.Player);
            int slot = player.Player.Slot.Value;
            VanillaLiquidContactState previousLiquidContacts = liquidOwners[slot] == player.Player
                ? liquidContacts[slot]
                : default;
            if (!dryPhysics.TryStep(
                    in player,
                    horizontalIntent,
                    jumpIntent,
                    in jumpState,
                    in previousLiquidContacts,
                    out ServerPlayerDryPhysicsStepResult next,
                    out VanillaServerPlayerJumpState nextJumpState))
            {
                continue;
            }

            CommitJumpState(player.Player, in nextJumpState);
            liquidOwners[slot] = player.Player;
            liquidContacts[slot] = next.LiquidContacts;

            if (next.PositionX == player.PositionX &&
                next.PositionY == player.PositionY &&
                next.VelocityX == player.VelocityX &&
                next.VelocityY == player.VelocityY)
            {
                continue;
            }

            if (states.TrySetMotion(
                player.Player,
                next.PositionX,
                next.PositionY,
                next.VelocityX,
                next.VelocityY,
                out PlayerStateSnapshot committed))
            {
                events?.ServerPlayerMoved(in committed);
            }
        }
    }

    public bool TryGet(PlayerHandle player, out PlayerStateSnapshot snapshot) => states.TryGet(player, out snapshot);

    public bool TryGet(PlayerSlotId slot, out PlayerStateSnapshot snapshot) => states.TryGet(slot, out snapshot);

    public int CopySnapshots(Span<PlayerStateSnapshot> destination) => states.CopySnapshots(destination);

    public bool IntersectsLivingPlayer(
        float left,
        float top,
        float right,
        float bottom,
        float playerWidth,
        float playerHeight)
    {
        int count = states.CopySnapshots(snapshots);
        for (int index = 0; index < count; index++)
        {
            PlayerStateSnapshot player = snapshots[index];
            if (player.IsDead)
                continue;

            if (Intersects(
                    player.PositionX,
                    player.PositionY,
                    playerWidth,
                    playerHeight,
                    left,
                    top,
                    right,
                    bottom))
            {
                return true;
            }
        }

        return false;
    }

    public ServerPlayerCreateResult Create(ServerPlayerId id, float positionX, float positionY)
    {
        ServerPlayerSlotRegistry identityRegistry = identities
            ?? throw new InvalidOperationException("Server-player lifecycle identities are not configured.");
        if (!id.IsAssigned)
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.InvalidId, default);
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY))
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.InvalidPosition, default);
        if (leases.ContainsKey(id))
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.AlreadyExists, default);

        ServerPlayerSlotAcquireResult acquire = identityRegistry.TryAcquire(
            id,
            out ServerPlayerSlotRegistry.ServerPlayerSlotLease? lease);
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
        events?.ServerPlayerCreated(in snapshot);
        return new ServerPlayerCreateResult(ServerPlayerCreateStatus.Created, snapshot.Player);
    }

    public bool SetHorizontalIntent(ServerPlayerId id, ServerPlayerHorizontalIntent intent)
    {
        if (!id.IsAssigned ||
            !IsValidHorizontalIntent(intent) ||
            !leases.TryGetValue(id, out ServerPlayerSlotRegistry.ServerPlayerSlotLease? lease) ||
            !states.TryGet(lease.Player, out _))
        {
            return false;
        }

        if (intent == ServerPlayerHorizontalIntent.Stop)
            horizontalIntents.Remove(lease.Player);
        else
            horizontalIntents[lease.Player] = intent;
        movementIntents.Remove(lease.Player);
        return true;
    }

    public ServerPlayerHorizontalIntent GetHorizontalIntent(PlayerHandle player) =>
        player.IsAssigned && horizontalIntents.TryGetValue(player, out ServerPlayerHorizontalIntent intent)
            ? intent
            : ServerPlayerHorizontalIntent.Stop;

    public bool SetJumpIntent(ServerPlayerId id, ServerPlayerJumpIntent intent)
    {
        if (!id.IsAssigned ||
            !IsValidJumpIntent(intent) ||
            !leases.TryGetValue(id, out ServerPlayerSlotRegistry.ServerPlayerSlotLease? lease) ||
            !states.TryGet(lease.Player, out _))
        {
            return false;
        }

        if (intent == ServerPlayerJumpIntent.Released)
        {
            jumpIntents.Remove(lease.Player);
            jumpStates.Remove(lease.Player);
        }
        else
        {
            jumpIntents[lease.Player] = intent;
        }
        movementIntents.Remove(lease.Player);
        return true;
    }

    public bool SetMovementIntent(ServerPlayerId id, in ServerPlayerMovementIntent intent)
    {
        if (!intent.IsValid || !TryGetPlayer(id, out PlayerHandle player))
            return false;

        horizontalIntents.Remove(player);
        jumpIntents.Remove(player);
        jumpStates.Remove(player);
        if (intent.Kind == ServerPlayerMovementIntentKind.Stop)
            movementIntents.Remove(player);
        else
            movementIntents[player] = intent;
        return true;
    }

    public ServerPlayerMovementIntent GetMovementIntent(PlayerHandle player) =>
        player.IsAssigned && movementIntents.TryGetValue(player, out ServerPlayerMovementIntent intent)
            ? intent
            : ServerPlayerMovementIntent.Stop();

    public ServerPlayerJumpIntent GetJumpIntent(PlayerHandle player) =>
        player.IsAssigned && jumpIntents.TryGetValue(player, out ServerPlayerJumpIntent intent)
            ? intent
            : ServerPlayerJumpIntent.Released;

    public bool SetAppearance(ServerPlayerId id, in ServerPlayerAppearanceState appearance)
    {
        if (!TryGetPlayer(id, out PlayerHandle player) ||
            !states.TrySetAppearance(player, in appearance, out ServerPlayerAppearanceState normalized))
        {
            return false;
        }

        events?.ServerPlayerAppearanceUpdated(player, in normalized);
        return true;
    }

    public bool SetVitals(ServerPlayerId id, in ServerPlayerVitalsState vitals)
    {
        if (!TryGetPlayer(id, out PlayerHandle player) ||
            !states.TrySetVitals(player, in vitals, out PlayerStateSnapshot normalized))
        {
            return false;
        }

        var committed = new ServerPlayerVitalsState(
            normalized.Life,
            normalized.MaxLife,
            normalized.Mana,
            normalized.MaxMana);
        events?.ServerPlayerVitalsUpdated(player, in committed);
        return true;
    }

    public bool SetItem(ServerPlayerId id, in ServerPlayerItemState item)
    {
        if (!TryGetPlayer(id, out PlayerHandle player) ||
            !states.TrySetItem(player, in item, out ServerPlayerItemState normalized))
        {
            return false;
        }

        events?.ServerPlayerItemUpdated(player, in normalized);
        return true;
    }

    public VanillaServerPlayerJumpState GetJumpState(PlayerHandle player) =>
        player.IsAssigned && jumpStates.TryGetValue(player, out VanillaServerPlayerJumpState state)
            ? state
            : VanillaServerPlayerJumpState.Initial;

    public void CommitJumpState(PlayerHandle player, in VanillaServerPlayerJumpState state)
    {
        if (!player.IsAssigned || !states.TryGet(player, out _))
        {
            jumpIntents.Remove(player);
            jumpStates.Remove(player);
            return;
        }

        if (state == VanillaServerPlayerJumpState.Initial)
            jumpStates.Remove(player);
        else
            jumpStates[player] = state;
    }

    public bool Despawn(ServerPlayerId id)
    {
        if (!id.IsAssigned || !leases.TryGetValue(id, out ServerPlayerSlotRegistry.ServerPlayerSlotLease? lease))
            return false;

        if (!states.TryRemove(lease.Player, out _))
        {
            throw new InvalidOperationException(
                "A live server-player lease lost its authoritative state before despawn.");
        }

        horizontalIntents.Remove(lease.Player);
        jumpIntents.Remove(lease.Player);
        jumpStates.Remove(lease.Player);
        movementIntents.Remove(lease.Player);
        leases.Remove(id);
        events?.ServerPlayerDespawned(lease.Player);
        lease.Dispose();
        return true;
    }

    private bool TryGetPlayer(ServerPlayerId id, out PlayerHandle player)
    {
        if (id.IsAssigned &&
            leases.TryGetValue(id, out ServerPlayerSlotRegistry.ServerPlayerSlotLease? lease) &&
            states.TryGet(lease.Player, out _))
        {
            player = lease.Player;
            return true;
        }

        player = default;
        return false;
    }

    private static bool IsValidHorizontalIntent(ServerPlayerHorizontalIntent intent) =>
        intent is ServerPlayerHorizontalIntent.Left or
            ServerPlayerHorizontalIntent.Stop or
            ServerPlayerHorizontalIntent.Right;

    private static bool IsValidJumpIntent(ServerPlayerJumpIntent intent) =>
        intent is ServerPlayerJumpIntent.Released or ServerPlayerJumpIntent.Held;

    private static bool Intersects(
        float leftA,
        float topA,
        float widthA,
        float heightA,
        float leftB,
        float topB,
        float rightB,
        float bottomB)
    {
        float rightA = leftA + widthA;
        float bottomA = topA + heightA;
        return leftA < rightB && rightA > leftB && topA < bottomB && bottomA > topB;
    }
}
