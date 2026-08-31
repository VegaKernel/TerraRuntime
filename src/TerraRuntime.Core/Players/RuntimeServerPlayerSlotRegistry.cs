using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public enum ServerPlayerSlotAcquireResult : byte
{
    Acquired = 0,
    InvalidId = 1,
    DuplicateId = 2,
    NoAvailableSlot = 3
}

public readonly record struct ServerPlayerSlotBinding(
    ServerPlayerId Id,
    PlayerHandle Player);

/// <summary>
/// Owns runtime-controlled player identities and their exact Terraria slot generations. Server players and network
/// connections share the same <see cref="PlayerSlotPool"/>, so a server-owned player reserves its wire slot against
/// connection bootstrap without inventing a fake transport source. Lifecycle/id-index mutation is serialized on the
/// control path; slot+generation lookup used by simulation is a lock-free immutable-entry read.
/// </summary>
public sealed class RuntimeServerPlayerSlotRegistry
{
    private readonly object gate = new();
    private readonly PlayerSlotPool slots;
    private readonly Dictionary<ServerPlayerId, ServerPlayerSlotBinding> byId = [];
    private readonly SlotEntry?[] bySlot;
    private readonly PlayerSlotPool.PlayerSlotLease?[] slotLeases;

    public RuntimeServerPlayerSlotRegistry(PlayerSlotPool slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        this.slots = slots;
        bySlot = new SlotEntry?[slots.Capacity];
        slotLeases = new PlayerSlotPool.PlayerSlotLease?[slots.Capacity];
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return byId.Count;
            }
        }
    }

    public ServerPlayerSlotAcquireResult TryAcquire(
        ServerPlayerId id,
        out ServerPlayerSlotLease? lease)
    {
        if (!id.IsAssigned)
        {
            lease = null;
            return ServerPlayerSlotAcquireResult.InvalidId;
        }

        lock (gate)
        {
            if (byId.ContainsKey(id))
            {
                lease = null;
                return ServerPlayerSlotAcquireResult.DuplicateId;
            }

            if (!slots.TryAcquireServerOwned(out PlayerSlotPool.PlayerSlotLease? slotLease) ||
                slotLease is null)
            {
                lease = null;
                return ServerPlayerSlotAcquireResult.NoAvailableSlot;
            }

            if (slotLease.Kind != PlayerSlotLeaseKind.ServerOwned)
            {
                slotLease.Dispose();
                throw new InvalidOperationException("Server player allocation returned a non-server-owned slot lease.");
            }

            int slot = slotLease.Slot.Value;
            if (Volatile.Read(ref bySlot[slot]) is not null || slotLeases[slot] is not null)
            {
                slotLease.Dispose();
                throw new InvalidOperationException("Server player registry slot ownership diverged from the shared pool.");
            }

            var binding = new ServerPlayerSlotBinding(id, slotLease.Handle);
            byId.Add(id, binding);
            slotLeases[slot] = slotLease;
            Volatile.Write(ref bySlot[slot], new SlotEntry(binding));
            lease = new ServerPlayerSlotLease(this, binding);
            return ServerPlayerSlotAcquireResult.Acquired;
        }
    }

    public bool TryGet(ServerPlayerId id, out ServerPlayerSlotBinding binding)
    {
        if (!id.IsAssigned)
        {
            binding = default;
            return false;
        }

        lock (gate)
        {
            return byId.TryGetValue(id, out binding);
        }
    }

    public bool TryGet(PlayerHandle player, out ServerPlayerSlotBinding binding)
    {
        if (!player.IsAssigned || player.Slot.Value >= bySlot.Length)
        {
            binding = default;
            return false;
        }

        SlotEntry? entry = Volatile.Read(ref bySlot[player.Slot.Value]);
        if (entry is null || entry.Binding.Player != player)
        {
            binding = default;
            return false;
        }

        binding = entry.Binding;
        return true;
    }

    private void Release(in ServerPlayerSlotBinding binding)
    {
        PlayerSlotPool.PlayerSlotLease? slotLease = null;
        lock (gate)
        {
            if (!byId.TryGetValue(binding.Id, out ServerPlayerSlotBinding current) || current != binding)
                return;

            int slot = binding.Player.Slot.Value;
            SlotEntry? slotted = Volatile.Read(ref bySlot[slot]);
            if (slotted is null || slotted.Binding != binding)
                throw new InvalidOperationException("Server player registry identity and slot indexes diverged.");

            slotLease = slotLeases[slot]
                ?? throw new InvalidOperationException("Server player binding lost its shared slot lease.");
            byId.Remove(binding.Id);
            Volatile.Write(ref bySlot[slot], null);
            slotLeases[slot] = null;
        }

        slotLease.Dispose();
    }

    private sealed class SlotEntry(ServerPlayerSlotBinding binding)
    {
        public ServerPlayerSlotBinding Binding { get; } = binding;
    }

    /// <summary>
    /// Exclusive lifetime for one stable server-player identity and one exact reusable player-slot generation.
    /// Releasing the lease removes both registry indexes before returning the slot to connection/server allocation.
    /// </summary>
    public sealed class ServerPlayerSlotLease : IDisposable
    {
        private RuntimeServerPlayerSlotRegistry? owner;
        private readonly ServerPlayerSlotBinding binding;

        internal ServerPlayerSlotLease(
            RuntimeServerPlayerSlotRegistry owner,
            ServerPlayerSlotBinding binding)
        {
            this.owner = owner;
            this.binding = binding;
        }

        public ServerPlayerId Id => binding.Id;

        public PlayerHandle Player => binding.Player;

        public bool IsReleased => Volatile.Read(ref owner) is null;

        public void Dispose()
        {
            RuntimeServerPlayerSlotRegistry? registry = Interlocked.Exchange(ref owner, null);
            registry?.Release(in binding);
        }
    }
}
