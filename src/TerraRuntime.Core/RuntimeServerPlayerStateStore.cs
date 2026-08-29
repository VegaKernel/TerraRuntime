using System.Diagnostics.CodeAnalysis;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Authoritative state for runtime-owned players. Identity ownership is validated against
/// <see cref="RuntimeServerPlayerSlotRegistry"/> on every operation; no transport connection is accepted anywhere in
/// this API, so client packet ingress cannot become an alternate writer by constructing a <see cref="ConnectionHandle"/>.
/// </summary>
public sealed class RuntimeServerPlayerStateStore
{
    private readonly RuntimeServerPlayerSlotRegistry identities;
    private readonly ServerPlayerRuntimeState?[] states;

    public RuntimeServerPlayerStateStore(RuntimeServerPlayerSlotRegistry identities, int capacity)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, byte.MaxValue);
        this.identities = identities;
        states = new ServerPlayerRuntimeState?[capacity];
    }

    public bool TrySpawn(
        ServerPlayerId id,
        float positionX,
        float positionY,
        out PlayerStateSnapshot snapshot)
    {
        if (!id.IsAssigned ||
            !float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !identities.TryGet(id, out ServerPlayerSlotBinding binding) ||
            binding.Player.Slot.Value >= states.Length)
        {
            snapshot = default;
            return false;
        }

        int slot = binding.Player.Slot.Value;
        ServerPlayerRuntimeState? current = states[slot];
        if (current is not null && current.Player == binding.Player)
        {
            snapshot = default;
            return false;
        }

        var state = new ServerPlayerRuntimeState
        {
            Id = id,
            Player = binding.Player,
            Revision = 1,
            PositionX = positionX,
            PositionY = positionY
        };
        states[slot] = state;
        snapshot = state.CaptureSnapshot();
        return true;
    }

    public bool TryGet(ServerPlayerId id, out PlayerStateSnapshot snapshot)
    {
        if (!identities.TryGet(id, out ServerPlayerSlotBinding binding))
        {
            snapshot = default;
            return false;
        }

        return TryGet(binding.Player, out snapshot);
    }

    public bool TryGet(PlayerHandle player, out PlayerStateSnapshot snapshot)
    {
        if (!player.IsAssigned ||
            player.Slot.Value >= states.Length ||
            !identities.TryGet(player, out ServerPlayerSlotBinding binding) ||
            binding.Player != player)
        {
            snapshot = default;
            return false;
        }

        ServerPlayerRuntimeState? state = states[player.Slot.Value];
        if (state is null || state.Player != player)
        {
            snapshot = default;
            return false;
        }

        snapshot = state.CaptureSnapshot();
        return true;
    }

    /// <summary>
    /// Copies live server-owned player snapshots in ascending wire-slot order without allocating. Stale state whose
    /// identity lease has already been released is deliberately skipped even if its storage slot has not been cleared.
    /// </summary>
    public int CopySnapshots(Span<PlayerStateSnapshot> destination)
    {
        int written = 0;
        for (int slot = 0; slot < states.Length && written < destination.Length; slot++)
        {
            ServerPlayerRuntimeState? state = states[slot];
            if (state is null ||
                !identities.TryGet(state.Player, out ServerPlayerSlotBinding binding) ||
                binding.Player != state.Player)
            {
                continue;
            }

            destination[written++] = state.CaptureSnapshot();
        }

        return written;
    }

    /// <summary>
    /// Commits one server-owned kinematic update. This is deliberately not a physics implementation: G6-D will
    /// compute validated gravity/collision results and call this single-writer commit surface.
    /// </summary>
    public bool TrySetMotion(
        PlayerHandle player,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        out PlayerStateSnapshot snapshot)
    {
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            !TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Revision == ulong.MaxValue)
        {
            snapshot = default;
            return false;
        }

        state.Revision++;
        state.PositionX = positionX;
        state.PositionY = positionY;
        state.VelocityX = velocityX;
        state.VelocityY = velocityY;
        snapshot = state.CaptureSnapshot();
        return true;
    }

    public bool TrySetDead(
        PlayerHandle player,
        bool isDead,
        out PlayerStateSnapshot snapshot)
    {
        if (!TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Revision == ulong.MaxValue)
        {
            snapshot = default;
            return false;
        }

        state.Revision++;
        state.IsDead = isDead;
        snapshot = state.CaptureSnapshot();
        return true;
    }

    public bool TryRemove(PlayerHandle player, out PlayerStateSnapshot removed)
    {
        if (!TryGetState(player, out ServerPlayerRuntimeState? state))
        {
            removed = default;
            return false;
        }

        removed = state.CaptureSnapshot();
        states[player.Slot.Value] = null;
        return true;
    }

    private bool TryGetState(
        PlayerHandle player,
        [NotNullWhen(true)] out ServerPlayerRuntimeState? state)
    {
        if (!player.IsAssigned ||
            player.Slot.Value >= states.Length ||
            !identities.TryGet(player, out ServerPlayerSlotBinding binding) ||
            binding.Player != player)
        {
            state = null;
            return false;
        }

        state = states[player.Slot.Value];
        return state is not null && state.Player == player;
    }

    private sealed class ServerPlayerRuntimeState
    {
        public ServerPlayerId Id { get; init; }
        public PlayerHandle Player { get; init; }
        public ulong Revision { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public bool IsDead { get; set; }

        public PlayerStateSnapshot CaptureSnapshot() =>
            new(
                Player,
                new PlayerStateRevision(Revision),
                Team: 0,
                ControlFlags: 0,
                MovementFlags: 0,
                MiscFlags1: 0,
                MiscFlags2: 0,
                SelectedItem: 0,
                PositionX,
                PositionY,
                VelocityX,
                VelocityY,
                MountType: 0,
                PotionOfReturnOriginalPositionX: 0f,
                PotionOfReturnOriginalPositionY: 0f,
                PotionOfReturnHomePositionX: 0f,
                PotionOfReturnHomePositionY: 0f,
                CameraTargetX: 0f,
                CameraTargetY: 0f)
            {
                IsDead = IsDead
            };
    }
}
