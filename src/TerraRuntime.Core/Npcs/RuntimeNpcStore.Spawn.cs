using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    private Func<VanillaNpcSpawnContext>? _spawnContext;

    /// <summary>Sets the world-owned input source, sampled synchronously for each vanilla creation.</summary>
    public void SetVanillaSpawnContextSource(Func<VanillaNpcSpawnContext> source) =>
        _spawnContext = source ?? throw new ArgumentNullException(nameof(source));

    public bool TrySpawn(byte slot, in NpcStateUpdate update, out NpcSnapshot snapshot) =>
        TrySpawnCore(slot, in update, out snapshot, replaceActive: false, protect: false);

    private bool TrySpawnCore(byte slot, in NpcStateUpdate update, out NpcSnapshot snapshot, bool replaceActive, bool protect,
        VanillaNpcSpawnDefaults? spawnDefaults = null)
    {
        if (!IsAddressableSlot(slot) || !IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[slot];
        if (state.Active && !replaceActive || !TryAdvance(ref state.Generation))
        {
            snapshot = default;
            return false;
        }

        NpcStateUpdate normalized = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in update, spawnDefaults);
        bool wasActive = state.Active;
        state.Active = true;
        if (protect) state.SpawnProtection = VanillaNpcSpawnRules.SpawnProtectionUpdates;
        state.Revision = 1;
        state.Update = normalized;
        if (!wasActive) _activeCount++;
        snapshot = Capture(slot, in state);
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in snapshot);
        return true;
    }

    /// <summary>Materializes and allocates one committed AI spawn intent using source-backed vanilla defaults.</summary>
    public bool TrySpawnIntent(in NpcAiSpawnIntent intent, out NpcSnapshot snapshot)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(intent.Type, out VanillaNpcDefinition definition) ||
            !float.IsFinite(intent.VelocityX) ||
            !float.IsFinite(intent.VelocityY) ||
            !intent.InitialAi.IsValidFor(intent.Type) ||
            !intent.InitialLocalAi.IsFinite)
        {
            snapshot = default;
            return false;
        }

        if (!TryCaptureSpawnDefaults(intent.Type.Value, checked((short)intent.Type.Value), out var spawnDefaults, out float difficulty))
        {
            snapshot = default;
            return false;
        }
        var hitbox = spawnDefaults?.Hitbox ?? new VanillaNpcHitboxSize(definition.Width, definition.Height);
        var update = new NpcStateUpdate(
            Type: intent.Type.Value,
            NetId: checked((short)intent.Type.Value),
            PositionX: intent.BottomX - hitbox.Width * 0.5f,
            PositionY: intent.BottomY - hitbox.Height,
            VelocityX: intent.VelocityX,
            VelocityY: intent.VelocityY,
            Target: intent.Target,
            Ai: intent.InitialAi,
            Simulation: NpcSimulationState.Initial with
            {
                TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft,
                SpawnDifficulty = difficulty,
                LocalAi = intent.InitialLocalAi,
                CanBeReplacedByOtherNpcs = intent.CanBeReplacedByOtherNpcs
            });

        return TrySpawnVanillaCore(in update, out snapshot, intent.StartSlot, spawnDefaults);
    }

    /// <summary>Allocates in vanilla search order, observing protection and replacement eligibility.</summary>
    public bool TrySpawnVanilla(in NpcStateUpdate update, out NpcSnapshot snapshot, int startSlot = 0)
    {
        if (!IsValid(in update) || !TryCaptureSpawnDefaults(update.Type, update.NetId, out var spawnDefaults, out float difficulty))
        {
            snapshot = default;
            return false;
        }
        var owned = update with { Simulation = update.Simulation with { SpawnDifficulty = update.Simulation.SpawnDifficulty ?? difficulty } };
        return TrySpawnVanillaCore(in owned, out snapshot, startSlot, spawnDefaults);
    }

    private bool TryCaptureSpawnDefaults(int type, short netId, out VanillaNpcSpawnDefaults? defaults, out float difficulty)
    {
        defaults = null;
        difficulty = 1f;
        if (_spawnContext is null) return true;
        var context = _spawnContext();
        if (!context.IsValid) return false;
        difficulty = context.Difficulty;
        if (VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), new NpcNetId(netId), out var definition) &&
            VanillaNpcSpawnDefaults.TryResolve(in definition, in context, out var resolved)) defaults = resolved;
        return true;
    }

    private bool TrySpawnVanillaCore(in NpcStateUpdate update, out NpcSnapshot snapshot, int startSlot,
        VanillaNpcSpawnDefaults? spawnDefaults)
    {
        if (!IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        var type = new NpcTypeId(update.Type);
        int capacity = Math.Min(_slots.Length, VanillaNpcSpawnRules.PhysicalSlotCount);
        if ((uint)startSlot >= (uint)capacity)
        {
            snapshot = default;
            return false;
        }
        int minimum = startSlot == 0 && VanillaNpcSpawnRules.CannotSpawnInSlotZero(type) ? 1 : startSlot;
        // NewNPCInstanceInSlot constructs a fresh NPC; its directionY initializer is 1 (1.4.5.8).
        // Explicit storage TrySpawn remains exact, and a supplied nonzero direction stays owned by the caller.
        var spawnedState = update with { Simulation = update.Simulation with { DirectionY = update.Simulation.DirectionY == 0 ? 1 : update.Simulation.DirectionY } };
        bool reverse = VanillaNpcSpawnRules.SearchesInReverse(type);
        // Original reverse traversal stops before the start index; even an otherwise eligible slot zero is excluded.
        if (reverse) minimum++;
        int replacement = -1;
        for (int offset = 0; offset < capacity - minimum; offset++)
        {
            int slot = reverse ? capacity - 1 - offset : minimum + offset;
            ref readonly SlotState state = ref _slots[slot];
            if (state.Generation == ulong.MaxValue) continue;
            if (!state.Active && state.SpawnProtection == 0)
                return TrySpawnCore((byte)slot, in spawnedState, out snapshot, replaceActive: false, protect: true, spawnDefaults);
            if (replacement < 0 && state.Update.Simulation.CanBeReplacedByOtherNpcs)
                replacement = slot;
        }

        if (replacement >= 0)
            return TrySpawnCore((byte)replacement, in spawnedState, out snapshot, replaceActive: true, protect: true, spawnDefaults);
        snapshot = default;
        return false;
    }

    /// <summary>Advances original NPC spawn protection once at the start of the authoritative world update.</summary>
    public void UpdateProtectedSpawnSlots()
    {
        int capacity = Math.Min(_slots.Length, VanillaNpcSpawnRules.PhysicalSlotCount);
        for (int slot = 0; slot < capacity; slot++)
        {
            ref SlotState state = ref _slots[slot];
            state.SpawnProtection = state.Active ? VanillaNpcSpawnRules.SpawnProtectionUpdates :
                Math.Max(0, state.SpawnProtection - 1);
        }
    }
}
