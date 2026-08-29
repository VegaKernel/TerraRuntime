from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text()
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one anchor, found {count}: {old[:100]!r}")
    file.write_text(text.replace(old, new, 1))


def write(path: str, content: str) -> None:
    file = Path(path)
    file.parent.mkdir(parents=True, exist_ok=True)
    if file.exists():
        raise RuntimeError(f"refusing to overwrite existing file: {path}")
    file.write_text(content)


write(
    "src/TerraRuntime.Contracts/Runtime/ServerPlayerJumpIntent.cs",
    '''namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Semantic jump-button state for a runtime-owned server player. The host reports whether jump is currently held;
/// TerraRuntime owns jump speed, duration, release gating, gravity and collision response.
/// </summary>
public enum ServerPlayerJumpIntent : byte
{
    Released = 0,
    Held = 1
}
''')

write(
    "src/TerraRuntime/VanillaServerPlayerJumpControl.cs",
    '''using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

/// <summary>
/// Runtime-owned state for the ordinary dry, unmounted TerrariaServer 1.4.5.8 jump path. RemainingTicks mirrors the
/// vanilla jump counter; ReleaseReady mirrors releaseJump. It is deliberately not exposed through HostContracts.
/// </summary>
internal readonly record struct VanillaServerPlayerJumpState(int RemainingTicks, bool ReleaseReady)
{
    public static VanillaServerPlayerJumpState Initial => new(0, true);

    public bool IsValid =>
        RemainingTicks is >= 0 and <= VanillaServerPlayerJumpControl.JumpHeight &&
        (RemainingTicks == 0 || !ReleaseReady);
}

/// <summary>
/// Source-backed ordinary jump control for an unmounted, normal-gravity, dry player. Accessories, mounts, liquids,
/// grapples, auto-jump and extra-jump families remain outside this slice.
/// </summary>
internal static class VanillaServerPlayerJumpControl
{
    internal const float JumpSpeed = 5.01f;
    internal const int JumpHeight = 15;

    public static bool TryApply(
        float velocityY,
        ServerPlayerJumpIntent intent,
        in VanillaServerPlayerJumpState state,
        out float nextVelocityY,
        out VanillaServerPlayerJumpState nextState)
    {
        if (!float.IsFinite(velocityY) ||
            !state.IsValid ||
            intent is not ServerPlayerJumpIntent.Released and not ServerPlayerJumpIntent.Held)
        {
            nextVelocityY = default;
            nextState = default;
            return false;
        }

        nextVelocityY = velocityY;
        if (intent == ServerPlayerJumpIntent.Released)
        {
            nextState = VanillaServerPlayerJumpState.Initial;
            return true;
        }

        if (state.RemainingTicks > 0)
        {
            if (velocityY == 0f)
            {
                nextState = new VanillaServerPlayerJumpState(0, false);
                return true;
            }

            nextVelocityY = -JumpSpeed;
            nextState = new VanillaServerPlayerJumpState(state.RemainingTicks - 1, false);
            return true;
        }

        if (velocityY == 0f && state.ReleaseReady)
        {
            nextVelocityY = -JumpSpeed;
            nextState = new VanillaServerPlayerJumpState(JumpHeight, false);
            return true;
        }

        nextState = state;
        return true;
    }
}
''')

replace_once(
    "src/TerraRuntime.HostContracts/IServerPlayerOperations.cs",
    '''    ValueTask<bool> SetHorizontalIntentAsync(
        ServerPlayerId id,
        ServerPlayerHorizontalIntent intent,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DespawnAsync(
''',
    '''    ValueTask<bool> SetHorizontalIntentAsync(
        ServerPlayerId id,
        ServerPlayerHorizontalIntent intent,
        CancellationToken cancellationToken = default);

    ValueTask<bool> SetJumpIntentAsync(
        ServerPlayerId id,
        ServerPlayerJumpIntent intent,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DespawnAsync(
''')

replace_once(
    "src/TerraRuntime/RuntimeServerPlayerOperations.cs",
    '''internal sealed record ServerPlayerHorizontalIntentRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerHorizontalIntent Intent,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record ServerPlayerDespawnRuntimeCommand(
''',
    '''internal sealed record ServerPlayerHorizontalIntentRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerHorizontalIntent Intent,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record ServerPlayerJumpIntentRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerJumpIntent Intent,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record ServerPlayerDespawnRuntimeCommand(
''')

replace_once(
    "src/TerraRuntime/RuntimeServerPlayerOperations.cs",
    '''    private readonly Dictionary<ServerPlayerId, RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease> leases = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerHorizontalIntent> horizontalIntents = [];
''',
    '''    private readonly Dictionary<ServerPlayerId, RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease> leases = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerHorizontalIntent> horizontalIntents = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerJumpIntent> jumpIntents = [];
    private readonly Dictionary<PlayerHandle, VanillaServerPlayerJumpState> jumpStates = [];
''')

replace_once(
    "src/TerraRuntime/RuntimeServerPlayerOperations.cs",
    '''            case ServerPlayerHorizontalIntentRuntimeCommand horizontal:
                horizontal.Completion.TrySetResult(SetHorizontalIntent(horizontal.Id, horizontal.Intent));
                return true;

            case ServerPlayerDespawnRuntimeCommand despawn:
''',
    '''            case ServerPlayerHorizontalIntentRuntimeCommand horizontal:
                horizontal.Completion.TrySetResult(SetHorizontalIntent(horizontal.Id, horizontal.Intent));
                return true;

            case ServerPlayerJumpIntentRuntimeCommand jump:
                jump.Completion.TrySetResult(SetJumpIntent(jump.Id, jump.Intent));
                return true;

            case ServerPlayerDespawnRuntimeCommand despawn:
''')

replace_once(
    "src/TerraRuntime/RuntimeServerPlayerOperations.cs",
    '''    public ServerPlayerHorizontalIntent GetHorizontalIntent(PlayerHandle player) =>
        player.IsAssigned && horizontalIntents.TryGetValue(player, out ServerPlayerHorizontalIntent intent)
            ? intent
            : ServerPlayerHorizontalIntent.Stop;

    public bool Despawn(ServerPlayerId id)
''',
    '''    public ServerPlayerHorizontalIntent GetHorizontalIntent(PlayerHandle player) =>
        player.IsAssigned && horizontalIntents.TryGetValue(player, out ServerPlayerHorizontalIntent intent)
            ? intent
            : ServerPlayerHorizontalIntent.Stop;

    public bool SetJumpIntent(ServerPlayerId id, ServerPlayerJumpIntent intent)
    {
        if (!id.IsAssigned ||
            !IsValidJumpIntent(intent) ||
            !leases.TryGetValue(id, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease) ||
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

        return true;
    }

    public ServerPlayerJumpIntent GetJumpIntent(PlayerHandle player) =>
        player.IsAssigned && jumpIntents.TryGetValue(player, out ServerPlayerJumpIntent intent)
            ? intent
            : ServerPlayerJumpIntent.Released;

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
''')

replace_once(
    "src/TerraRuntime/RuntimeServerPlayerOperations.cs",
    '''        horizontalIntents.Remove(lease.Player);
        leases.Remove(id);
''',
    '''        horizontalIntents.Remove(lease.Player);
        jumpIntents.Remove(lease.Player);
        jumpStates.Remove(lease.Player);
        leases.Remove(id);
''')

replace_once(
    "src/TerraRuntime/RuntimeServerPlayerOperations.cs",
    '''    private static bool IsValidHorizontalIntent(ServerPlayerHorizontalIntent intent) =>
        intent is ServerPlayerHorizontalIntent.Left or
            ServerPlayerHorizontalIntent.Stop or
            ServerPlayerHorizontalIntent.Right;
}
''',
    '''    private static bool IsValidHorizontalIntent(ServerPlayerHorizontalIntent intent) =>
        intent is ServerPlayerHorizontalIntent.Left or
            ServerPlayerHorizontalIntent.Stop or
            ServerPlayerHorizontalIntent.Right;

    private static bool IsValidJumpIntent(ServerPlayerJumpIntent intent) =>
        intent is ServerPlayerJumpIntent.Released or ServerPlayerJumpIntent.Held;
}
''')

replace_once(
    "src/TerraRuntime/RuntimeServerPlayerOperations.cs",
    '''    public async ValueTask<bool> DespawnAsync(
        ServerPlayerId id,
        CancellationToken cancellationToken = default)
''',
    '''    public async ValueTask<bool> SetJumpIntentAsync(
        ServerPlayerId id,
        ServerPlayerJumpIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerJumpIntentRuntimeCommand(id, intent, completion)))
        {
            throw new InvalidOperationException(
                "The authoritative command queue rejected the server-player jump intent command.");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> DespawnAsync(
        ServerPlayerId id,
        CancellationToken cancellationToken = default)
''')

replace_once(
    "src/TerraRuntime/VanillaServerPlayerDryPhysicsStepper.cs",
    '''/// This slice owns source-backed baseline horizontal input, the base hitbox, gravity/fall-speed clamp,
/// walk-down-slope, ordinary StepDown/StepUp, tile collision, position advance and post-move slope collision.
/// Mounts, liquids and jump-control state remain outside this slice.
''',
    '''/// This slice owns source-backed baseline horizontal/jump input, the base hitbox, gravity/fall-speed clamp,
/// walk-down-slope, ordinary StepDown/StepUp, tile collision, position advance and post-move slope collision.
/// Mounts, liquids and extended jump families remain outside this slice.
''')

replace_once(
    "src/TerraRuntime/VanillaServerPlayerDryPhysicsStepper.cs",
    '''    public bool TryStep(
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
''',
    '''    public bool TryStep(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent,
        out ServerPlayerDryPhysicsStepResult next)
    {
        VanillaServerPlayerJumpState jumpState = VanillaServerPlayerJumpState.Initial;
        return TryStep(
            in player,
            horizontalIntent,
            ServerPlayerJumpIntent.Released,
            in jumpState,
            out next,
            out _);
    }

    public bool TryStep(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent,
        ServerPlayerJumpIntent jumpIntent,
        in VanillaServerPlayerJumpState jumpState,
        out ServerPlayerDryPhysicsStepResult next,
        out VanillaServerPlayerJumpState nextJumpState)
    {
        if (!IsValidHorizontalIntent(horizontalIntent))
        {
            next = default;
            nextJumpState = default;
            return false;
        }

        float velocityX = VanillaServerPlayerHorizontalControl.Apply(
            player.VelocityX,
            player.VelocityY,
            horizontalIntent);
        if (!VanillaServerPlayerJumpControl.TryApply(
                player.VelocityY,
                jumpIntent,
                in jumpState,
                out float velocityY,
                out nextJumpState))
        {
            next = default;
            return false;
        }

        return TryStepCore(in player, velocityX, velocityY, ref nextJumpState, out next);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        out ServerPlayerDryPhysicsStepResult next)
    {
        VanillaServerPlayerJumpState jumpState = VanillaServerPlayerJumpState.Initial;
        return TryStepCore(in player, velocityX, player.VelocityY, ref jumpState, out next);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        float controlledVelocityY,
        ref VanillaServerPlayerJumpState jumpState,
        out ServerPlayerDryPhysicsStepResult next)
''')

replace_once(
    "src/TerraRuntime/VanillaServerPlayerDryPhysicsStepper.cs",
    '''            !float.IsFinite(player.PositionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(player.VelocityY))
''',
    '''            !float.IsFinite(player.PositionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(controlledVelocityY))
''')

replace_once(
    "src/TerraRuntime/VanillaServerPlayerDryPhysicsStepper.cs",
    '''        float positionX = player.PositionX;
        float positionY = player.PositionY;
        float velocityY = Math.Min(player.VelocityY + Gravity, MaximumFallSpeed);
''',
    '''        float positionX = player.PositionX;
        float positionY = player.PositionY;
        float velocityY = Math.Min(controlledVelocityY + Gravity, MaximumFallSpeed);
''')

replace_once(
    "src/TerraRuntime/VanillaServerPlayerDryPhysicsStepper.cs",
    '''        positionX += collision.VelocityX;
        positionY += collision.VelocityY;
        VanillaSlopeCollisionResult slope = VanillaWorldSlopeCollision.Resolve(
''',
    '''        if (collision.HitCeiling && jumpState.RemainingTicks > 0)
            jumpState = new VanillaServerPlayerJumpState(0, jumpState.ReleaseReady);

        positionX += collision.VelocityX;
        positionY += collision.VelocityY;
        VanillaSlopeCollisionResult slope = VanillaWorldSlopeCollision.Resolve(
''')

replace_once(
    "src/TerraRuntime/ServerRuntimeState.cs",
    '''            ServerPlayerHorizontalIntent horizontalIntent =
                _serverPlayerCommands?.GetHorizontalIntent(player.Player) ?? ServerPlayerHorizontalIntent.Stop;
            if (!_serverPlayerDryPhysics.TryStep(
                    in player,
                    horizontalIntent,
                    out ServerPlayerDryPhysicsStepResult next))
            {
                continue;
            }

            if (next.PositionX == player.PositionX &&
''',
    '''            ServerPlayerHorizontalIntent horizontalIntent =
                _serverPlayerCommands?.GetHorizontalIntent(player.Player) ?? ServerPlayerHorizontalIntent.Stop;
            ServerPlayerJumpIntent jumpIntent =
                _serverPlayerCommands?.GetJumpIntent(player.Player) ?? ServerPlayerJumpIntent.Released;
            VanillaServerPlayerJumpState jumpState =
                _serverPlayerCommands?.GetJumpState(player.Player) ?? VanillaServerPlayerJumpState.Initial;
            if (!_serverPlayerDryPhysics.TryStep(
                    in player,
                    horizontalIntent,
                    jumpIntent,
                    in jumpState,
                    out ServerPlayerDryPhysicsStepResult next,
                    out VanillaServerPlayerJumpState nextJumpState))
            {
                continue;
            }

            _serverPlayerCommands?.CommitJumpState(player.Player, in nextJumpState);

            if (next.PositionX == player.PositionX &&
''')

replace_once(
    "tests/TerraRuntime.Tests/TrustedHostModuleLoaderTests.cs",
    '''        public ValueTask<bool> DespawnAsync(
            ServerPlayerId id,
            CancellationToken cancellationToken = default)
''',
    '''        public ValueTask<bool> SetJumpIntentAsync(
            ServerPlayerId id,
            ServerPlayerJumpIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask<bool> DespawnAsync(
            ServerPlayerId id,
            CancellationToken cancellationToken = default)
''')

write(
    "tests/TerraRuntime.Tests/VanillaServerPlayerJumpControlTests.cs",
    '''using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class VanillaServerPlayerJumpControlTests
{
    [Fact]
    public void Held_from_released_grounded_state_starts_vanilla_jump()
    {
        VanillaServerPlayerJumpState state = VanillaServerPlayerJumpState.Initial;

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            0f,
            ServerPlayerJumpIntent.Held,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(-5.01f, velocityY, 5);
        Assert.Equal(15, next.RemainingTicks);
        Assert.False(next.ReleaseReady);
    }

    [Fact]
    public void Held_active_jump_reasserts_jump_speed_and_decrements_counter()
    {
        var state = new VanillaServerPlayerJumpState(15, false);

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            -4.61f,
            ServerPlayerJumpIntent.Held,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(-5.01f, velocityY, 5);
        Assert.Equal(14, next.RemainingTicks);
        Assert.False(next.ReleaseReady);
    }

    [Fact]
    public void Released_cancels_remaining_jump_and_arms_release_gate()
    {
        var state = new VanillaServerPlayerJumpState(9, false);

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            -4.61f,
            ServerPlayerJumpIntent.Released,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(-4.61f, velocityY, 5);
        Assert.Equal(VanillaServerPlayerJumpState.Initial, next);
    }

    [Fact]
    public void Held_after_landing_without_release_does_not_restart_jump()
    {
        var state = new VanillaServerPlayerJumpState(0, false);

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            0f,
            ServerPlayerJumpIntent.Held,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(0f, velocityY, 5);
        Assert.Equal(state, next);
    }

    [Fact]
    public void Held_active_jump_that_is_already_stopped_clears_counter_without_rearming_release()
    {
        var state = new VanillaServerPlayerJumpState(7, false);

        Assert.True(VanillaServerPlayerJumpControl.TryApply(
            0f,
            ServerPlayerJumpIntent.Held,
            in state,
            out float velocityY,
            out VanillaServerPlayerJumpState next));

        Assert.Equal(0f, velocityY, 5);
        Assert.Equal(new VanillaServerPlayerJumpState(0, false), next);
    }

    [Fact]
    public void Invalid_jump_intent_is_rejected()
    {
        VanillaServerPlayerJumpState state = VanillaServerPlayerJumpState.Initial;

        Assert.False(VanillaServerPlayerJumpControl.TryApply(
            0f,
            (ServerPlayerJumpIntent)42,
            in state,
            out _,
            out _));
    }
}
''')

write(
    "tests/TerraRuntime.Tests/RuntimeServerPlayerJumpIntentTests.cs",
    '''using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerJumpIntentTests
{
    [Fact]
    public void Jump_control_is_bound_to_exact_generation_and_removed_on_reuse()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var service = new RuntimeServerPlayerCommandService(identities, states);
        var firstId = new ServerPlayerId("test:first-jump");
        var secondId = new ServerPlayerId("test:second-jump");

        ServerPlayerCreateResult first = service.Create(firstId, 10f, 20f);
        Assert.True(first.IsCreated);
        Assert.True(service.SetJumpIntent(firstId, ServerPlayerJumpIntent.Held));
        service.CommitJumpState(first.Player, new VanillaServerPlayerJumpState(11, false));
        Assert.Equal(ServerPlayerJumpIntent.Held, service.GetJumpIntent(first.Player));
        Assert.Equal(11, service.GetJumpState(first.Player).RemainingTicks);

        Assert.True(service.Despawn(firstId));
        ServerPlayerCreateResult second = service.Create(secondId, 30f, 40f);

        Assert.True(second.IsCreated);
        Assert.Equal(first.Player.Slot, second.Player.Slot);
        Assert.True(second.Player.Generation.Value > first.Player.Generation.Value);
        Assert.Equal(ServerPlayerJumpIntent.Released, service.GetJumpIntent(first.Player));
        Assert.Equal(VanillaServerPlayerJumpState.Initial, service.GetJumpState(first.Player));
        Assert.Equal(ServerPlayerJumpIntent.Released, service.GetJumpIntent(second.Player));
        Assert.Equal(VanillaServerPlayerJumpState.Initial, service.GetJumpState(second.Player));
    }

    [Fact]
    public void Release_resets_sparse_jump_input_and_physics_state()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var service = new RuntimeServerPlayerCommandService(identities, states);
        var id = new ServerPlayerId("test:release-jump");
        ServerPlayerCreateResult created = service.Create(id, 0f, 0f);
        Assert.True(created.IsCreated);

        Assert.True(service.SetJumpIntent(id, ServerPlayerJumpIntent.Held));
        service.CommitJumpState(created.Player, new VanillaServerPlayerJumpState(8, false));
        Assert.True(service.SetJumpIntent(id, ServerPlayerJumpIntent.Released));

        Assert.Equal(ServerPlayerJumpIntent.Released, service.GetJumpIntent(created.Player));
        Assert.Equal(VanillaServerPlayerJumpState.Initial, service.GetJumpState(created.Player));
        Assert.False(service.SetJumpIntent(id, (ServerPlayerJumpIntent)42));
    }

    [Fact]
    public void Missing_server_player_cannot_receive_jump_intent()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var service = new RuntimeServerPlayerCommandService(identities, states);

        Assert.False(service.SetJumpIntent(
            new ServerPlayerId("test:missing-jump"),
            ServerPlayerJumpIntent.Held));
    }
}
''')

write(
    "tests/TerraRuntime.Tests/ServerRuntimeServerPlayerJumpControlTests.cs",
    '''using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeServerPlayerJumpControlTests
{
    [Fact]
    public async Task Jump_intent_command_drives_authoritative_jump_before_gravity()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        WorldTileStore tiles = CreateGroundedWorld();
        var runtime = new ServerRuntimeState(
            worldTiles: tiles,
            serverPlayerStates: states,
            serverPlayerIdentities: identities);
        var id = new ServerPlayerId("test:runtime-jump");

        var create = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, 320f, 438f, create));
        ServerPlayerCreateResult created = await create.Task;
        Assert.True(created.IsCreated);

        var jump = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerJumpIntentRuntimeCommand(id, ServerPlayerJumpIntent.Held, jump));
        Assert.True(await jump.Task);

        runtime.Tick();

        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot moved));
        Assert.Equal(2UL, moved.Revision.Value);
        Assert.Equal(320f, moved.PositionX, 5);
        Assert.Equal(433.39f, moved.PositionY, 4);
        Assert.Equal(0f, moved.VelocityX, 5);
        Assert.Equal(-4.61f, moved.VelocityY, 4);
    }

    [Fact]
    public async Task Held_jump_does_not_restart_after_landing_until_release()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        WorldTileStore tiles = CreateGroundedWorld();
        var runtime = new ServerRuntimeState(
            worldTiles: tiles,
            serverPlayerStates: states,
            serverPlayerIdentities: identities);
        var id = new ServerPlayerId("test:held-jump-release-gate");

        var create = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, 320f, 438f, create));
        ServerPlayerCreateResult created = await create.Task;
        Assert.True(created.IsCreated);

        var jump = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerJumpIntentRuntimeCommand(id, ServerPlayerJumpIntent.Held, jump));
        Assert.True(await jump.Task);

        for (int tick = 0; tick < 180; tick++)
            runtime.Tick();

        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot landed));
        Assert.Equal(438f, landed.PositionY, 4);
        Assert.Equal(0f, landed.VelocityY, 4);

        ulong revision = landed.Revision.Value;
        runtime.Tick();

        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot stillLanded));
        Assert.Equal(438f, stillLanded.PositionY, 4);
        Assert.Equal(0f, stillLanded.VelocityY, 4);
        Assert.Equal(revision, stillLanded.Revision.Value);
    }

    private static WorldTileStore CreateGroundedWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 18; x <= 23; x++)
        {
            tiles.Set(x, 30, new WorldTile
            {
                Type = 1,
                Flags = WorldTileFlags.Active
            });
        }

        return tiles;
    }
}
''')

replace_once(
    "docs/en/host-interfaces.md",
    '''bool accepted = await runtime.ServerPlayers.SetHorizontalIntentAsync(
    serverPlayerId,
    ServerPlayerHorizontalIntent.Right,
    cancellationToken);
```

Despawn:
''',
    '''bool accepted = await runtime.ServerPlayers.SetHorizontalIntentAsync(
    serverPlayerId,
    ServerPlayerHorizontalIntent.Right,
    cancellationToken);

bool jumping = await runtime.ServerPlayers.SetJumpIntentAsync(
    serverPlayerId,
    ServerPlayerJumpIntent.Held,
    cancellationToken);

await runtime.ServerPlayers.SetJumpIntentAsync(
    serverPlayerId,
    ServerPlayerJumpIntent.Released,
    cancellationToken);
```

`ServerPlayerJumpIntent` is button-level semantic input, not a velocity command. TerraRuntime owns the ordinary vanilla
jump speed, jump-duration counter, release gate, gravity and collision. Holding jump through landing therefore does not
start another jump until a `Released` state has armed the vanilla release gate again. The current source-backed slice is
the dry, unmounted, normal-gravity path; liquid, mount, grapple and extra-jump families are separate gameplay work.

Despawn:
''')

replace_once(
    "docs/ru/host-interfaces.md",
    '''bool accepted = await runtime.ServerPlayers.SetHorizontalIntentAsync(
    serverPlayerId,
    ServerPlayerHorizontalIntent.Right,
    cancellationToken);
```

Удаление:
''',
    '''bool accepted = await runtime.ServerPlayers.SetHorizontalIntentAsync(
    serverPlayerId,
    ServerPlayerHorizontalIntent.Right,
    cancellationToken);

bool jumping = await runtime.ServerPlayers.SetJumpIntentAsync(
    serverPlayerId,
    ServerPlayerJumpIntent.Held,
    cancellationToken);

await runtime.ServerPlayers.SetJumpIntentAsync(
    serverPlayerId,
    ServerPlayerJumpIntent.Released,
    cancellationToken);
```

`ServerPlayerJumpIntent` — это semantic состояние кнопки, а не команда записи скорости. TerraRuntime сам владеет
vanilla jump speed, счётчиком длительности прыжка, release gate, gravity и collision. Поэтому удерживание jump после
приземления не запускает новый прыжок, пока `Released` снова не взведёт vanilla release gate. Текущий source-backed
slice покрывает dry/unmounted/normal-gravity path; liquids, mounts, grapples и extra-jump families идут отдельно.

Удаление:
''')

print("G6 player jump control slice staged successfully")
