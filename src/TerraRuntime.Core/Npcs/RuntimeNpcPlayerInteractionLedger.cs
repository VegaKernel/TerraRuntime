using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Source-shaped NPC.playerInteraction projection. TerrariaServer 1.4.5.8 records interaction by player slot,
/// not by a long-lived account identity, and boss loot later re-checks whether that slot currently has an active
/// player. TerraRuntime keeps the NPC side generation-safe so a reused NPC slot never inherits interactions.
/// </summary>
public sealed partial class RuntimeNpcPlayerInteractionLedger
{
    private readonly RuntimeNpcStore _store;
    private readonly Dictionary<NpcHandle, PlayerSlotMask> _interactions = [];
    private readonly Dictionary<byte, (NpcHandle Handle, PlayerSlotId Player)> _lastInteraction = [];

    public RuntimeNpcPlayerInteractionLedger(RuntimeNpcStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    internal bool CopyDeathPreviewTo(RuntimeNpcPlayerInteractionLedger target)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (var entry in _interactions)
            if (_store.TryGet(entry.Key, out _))
            {
                if (!target._store.TryGet(entry.Key, out _)) return false;
                target._interactions[entry.Key] = entry.Value;
            }
        foreach (var entry in _lastInteraction)
            if (_store.TryGet(entry.Value.Handle, out _) && target._store.TryGet(entry.Value.Handle, out _))
                target._lastInteraction[entry.Key] = entry.Value;
        return true;
    }

    public bool TryMark(NpcHandle npc, PlayerHandle player)
    {
        if (!npc.IsAssigned ||
            !player.IsAssigned ||
            player.Slot.Value >= VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots ||
            !_store.TryGet(npc, out _))
        {
            return false;
        }

        _interactions.TryGetValue(npc, out PlayerSlotMask mask);
        _interactions[npc] = mask.With(player.Slot.Value);
        _lastInteraction[npc.Slot] = (npc, player.Slot);
        return true;
    }

    public bool HasInteraction(NpcHandle npc, PlayerSlotId player)
    {
        if (!npc.IsAssigned ||
            player.Value >= VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots ||
            !_store.TryGet(npc, out _))
        {
            Forget(npc);
            return false;
        }

        return _interactions.TryGetValue(npc, out PlayerSlotMask mask) && mask.Contains(player.Value);
    }

    /// <summary>
    /// Copies interacting slots in the same ascending 0..254 order used by Terraria's per-player loot loops.
    /// The operation fails rather than truncating when the destination cannot hold the complete interaction set.
    /// </summary>
    public bool TryCopyInteractingSlots(
        NpcHandle npc,
        Span<PlayerSlotId> destination,
        out int count)
    {
        count = 0;
        if (!npc.IsAssigned || !_store.TryGet(npc, out _))
        {
            Forget(npc);
            return false;
        }

        if (!_interactions.TryGetValue(npc, out PlayerSlotMask mask))
            return true;

        int required = mask.Count;
        if (destination.Length < required)
            return false;

        for (int slot = 0; slot < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots; slot++)
        {
            if (mask.Contains(checked((byte)slot)))
                destination[count++] = new PlayerSlotId(checked((byte)slot));
        }

        return true;
    }

    public void Forget(NpcHandle npc)
    {
        if (npc.IsAssigned)
        {
            _interactions.Remove(npc);
            if (_lastInteraction.TryGetValue(npc.Slot, out var last) && last.Handle == npc)
                _lastInteraction.Remove(npc.Slot);
        }
    }

    public bool HasAnyInteraction(NpcHandle npc) => _store.TryGet(npc, out _) &&
        _interactions.TryGetValue(npc, out var mask) && mask.Count != 0;

    public bool TryGetLastInteraction(NpcHandle npc, out PlayerSlotId slot)
    {
        slot = default;
        if (!_store.TryGet(npc, out _) || !_lastInteraction.TryGetValue(npc.Slot, out var last) || last.Handle != npc)
            return false;
        slot = last.Player; return true;
    }

    private readonly record struct PlayerSlotMask(ulong A, ulong B, ulong C, ulong D)
    {
        public int Count =>
            System.Numerics.BitOperations.PopCount(A) +
            System.Numerics.BitOperations.PopCount(B) +
            System.Numerics.BitOperations.PopCount(C) +
            System.Numerics.BitOperations.PopCount(D);

        public bool Contains(byte slot)
        {
            int word = slot >> 6;
            int bit = slot & 63;
            ulong value = word switch
            {
                0 => A,
                1 => B,
                2 => C,
                _ => D
            };
            return (value & (1UL << bit)) != 0;
        }

        public PlayerSlotMask With(byte slot)
        {
            int word = slot >> 6;
            ulong bit = 1UL << (slot & 63);
            return word switch
            {
                0 => this with { A = A | bit },
                1 => this with { B = B | bit },
                2 => this with { C = C | bit },
                _ => this with { D = D | bit }
            };
        }
    }
}
