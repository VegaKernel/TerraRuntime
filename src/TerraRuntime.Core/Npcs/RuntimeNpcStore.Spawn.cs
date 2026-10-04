using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    private Func<VanillaNpcSpawnContext>? _spawnContext;
    private IVanillaNpcRandom _spawnRandom = new SystemVanillaNpcRandom();

    /// <summary>Shares the world-owned NPC random stream with creation, AI and natural spawn selection.</summary>
    public void SetVanillaSpawnRandomSource(IVanillaNpcRandom source) =>
        _spawnRandom = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Sets the world-owned input source, sampled synchronously for each vanilla creation.</summary>
    public void SetVanillaSpawnContextSource(Func<VanillaNpcSpawnContext> source) =>
        _spawnContext = source ?? throw new ArgumentNullException(nameof(source));

    public bool TrySpawn(byte slot, in NpcStateUpdate update, out NpcSnapshot snapshot) =>
        TrySpawnCore(slot, in update, out snapshot, replaceActive: false, protect: false);

    private bool TrySpawnCore(byte slot, in NpcStateUpdate update, out NpcSnapshot snapshot, bool replaceActive, bool protect,
        VanillaNpcSpawnDefaults? spawnDefaults = null, bool publish = true)
    {
        if (!IsAddressableSlot(slot) || !IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[slot];
        if (state.Active && !replaceActive || state.Generation == ulong.MaxValue)
        {
            snapshot = default;
            return false;
        }

        if (state.BirthPending && (publish || _commitSink is not null))
        {
            var previous = Capture(slot, in state);
            if (!TryPublishPendingBirth(previous.Handle) || !MatchesSource(in previous))
            {
                snapshot = default;
                return false;
            }
        }

        state.Generation++;

        NpcStateUpdate normalized = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in update, spawnDefaults);
        bool wasActive = state.Active;
        state.Active = true;
        state.BirthPending = false;
        if (protect) state.SpawnProtection = VanillaNpcSpawnRules.SpawnProtectionUpdates;
        state.Revision = 1;
        state.Update = normalized;
        MarkSlotMutation();
        if (!wasActive) _activeCount++;
        snapshot = Capture(slot, in state);
        if (publish)
            _commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in snapshot);
        return true;
    }

    /// <summary>Materializes and allocates one committed AI spawn intent using source-backed vanilla defaults.</summary>
    public bool TrySpawnIntent(in NpcAiSpawnIntent intent, out NpcSnapshot snapshot)
        => TrySpawnIntentCore(in intent, null, out snapshot);

    internal bool TrySpawnIntent(in NpcSnapshot source, in NpcAiSpawnIntent intent, out NpcSnapshot snapshot)
    {
        snapshot = default;
        return MatchesSource(in source) && TrySpawnIntentCore(in intent, source, out snapshot);
    }

    private bool MatchesSource(in NpcSnapshot source) =>
        TryGet(source.Handle, out var current) && current.Revision == source.Revision;

    private bool TrySpawnIntentCore(in NpcAiSpawnIntent intent, NpcSnapshot? source, out NpcSnapshot snapshot)
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

        int type = intent.Type.Value;
        short netId = checked((short)(intent.NetIdOverride?.Value ?? type));
        if (!TryCaptureSpawnDefaults(ref type, ref netId, out var spawnDefaults, out float difficulty) ||
            !VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), new NpcNetId(netId), out definition) ||
            // Context/RNG callbacks may reenter. Retain accepted draws, but stop before allocating or publishing.
            (source is NpcSnapshot expected && !MatchesSource(in expected)))
        {
            snapshot = default;
            return false;
        }
        var hitbox = spawnDefaults?.Hitbox ?? new VanillaNpcHitboxSize(definition.Width, definition.Height);
        var update = new NpcStateUpdate(
            Type: type,
            NetId: netId,
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

        // An AI intent models NewNPC's explicit ai arguments, including an intentional all-zero state.
        return TrySpawnVanillaCore(in update, out snapshot, intent.StartSlot, spawnDefaults);
    }

    /// <summary>Allocates in vanilla search order, observing protection and replacement eligibility.</summary>
    public bool TrySpawnVanilla(in NpcStateUpdate update, out NpcSnapshot snapshot, int startSlot = 0)
        => TrySpawnVanillaCreation(in update, out snapshot, startSlot, deferBirth: false);

    internal bool TrySpawnVanillaPending(in NpcStateUpdate update, out NpcSnapshot snapshot, int startSlot = 0)
        => TrySpawnVanillaCreation(in update, out snapshot, startSlot, deferBirth: true);

    internal bool TrySpawnVanillaPendingAtBottomCenter(in NpcStateUpdate update,
        float centerX, float bottomY, out NpcSnapshot snapshot)
    {
        snapshot = default;
        return float.IsFinite(centerX) && float.IsFinite(bottomY) &&
            TrySpawnVanillaCreation(in update, out snapshot, 0, deferBirth: true, (centerX, bottomY));
    }

    private bool TrySpawnVanillaCreation(in NpcStateUpdate update, out NpcSnapshot snapshot,
        int startSlot, bool deferBirth, (float X, float Y)? bottomCenter = null)
    {
        int type = update.Type;
        short netId = update.NetId;
        if (!IsValid(in update) || !TryCaptureSpawnDefaults(ref type, ref netId, out var spawnDefaults, out float difficulty))
        {
            snapshot = default;
            return false;
        }
        var owned = update with { Type = type, NetId = netId,
            Simulation = update.Simulation with { SpawnDifficulty = update.Simulation.SpawnDifficulty ?? difficulty } };
        if (bottomCenter is { } anchor)
        {
            if (!VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), new NpcNetId(netId), out var definition))
            {
                snapshot = default;
                return false;
            }
            var materialized = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in owned, spawnDefaults);
            if (!definition.TryResolveHitbox(materialized.Simulation, out var body))
            {
                snapshot = default;
                return false;
            }
            owned = owned with
            {
                PositionX = anchor.X - body.Width * .5f,
                PositionY = anchor.Y - body.Height
            };
        }
        NpcStateUpdate initialized = ApplySpawnAiDefaults(in owned);
        if (!TrySpawnVanillaCore(in initialized, out snapshot, startSlot, spawnDefaults, publish: !deferBirth))
            return false;
        if (deferBirth)
            TryRetainPendingBirth(in snapshot);
        return true;
    }

    private NpcStateUpdate ApplySpawnAiDefaults(in NpcStateUpdate update)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(update.Type), new NpcNetId(update.NetId), out var definition))
            return update;
        return update with
        {
            Ai = VanillaNpcAiSpawnDefaults1458.Resolve(new NpcTypeId(update.Type), in definition, update.Ai, _spawnRandom)
        };
    }

    private bool TryCaptureSpawnDefaults(ref int type, ref short netId, out VanillaNpcSpawnDefaults? defaults, out float difficulty)
        => TryCaptureSpawnDefaults(ref type, ref netId, out defaults, out difficulty, out _);

    private bool TryCaptureSpawnDefaults(ref int type, ref short netId, out VanillaNpcSpawnDefaults? defaults,
        out float difficulty, out VanillaNpcSpawnContext context)
    {
        defaults = null;
        difficulty = 1f;
        context = new(1f, 1, false);
        if (_spawnContext is null) return true;
        context = _spawnContext();
        if (!context.IsValid) return false;
        if (context.GoodWorld)
        {
            // Original NewNPC consumes this draw before allocation, including a full/protected table.
            var selected = VanillaNpcSpawnRules.ApplyGoodWorldRoll(new NpcTypeId(type), _spawnRandom.NextInt32(0, 3));
            if (selected.Value != type)
            {
                type = selected.Value;
                netId = checked((short)type);
            }
        }
        difficulty = context.Difficulty;
        if (VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), new NpcNetId(netId), out var definition) &&
            VanillaNpcSpawnDefaults.TryResolve(in definition, in context, out var resolved))
        {
            defaults = resolved;
            difficulty = resolved.Difficulty ?? difficulty;
        }
        return true;
    }

    private bool TrySpawnVanillaCore(in NpcStateUpdate update, out NpcSnapshot snapshot, int startSlot,
        VanillaNpcSpawnDefaults? spawnDefaults, bool publish = true)
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
                return TrySpawnCore((byte)slot, in spawnedState, out snapshot, replaceActive: false, protect: true, spawnDefaults, publish);
            if (replacement < 0 && state.Update.Simulation.CanBeReplacedByOtherNpcs)
                replacement = slot;
        }

        if (replacement >= 0)
            return TrySpawnCore((byte)replacement, in spawnedState, out snapshot, replaceActive: true, protect: true, spawnDefaults, publish);
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
            int protection = state.Active ? VanillaNpcSpawnRules.SpawnProtectionUpdates :
                Math.Max(0, state.SpawnProtection - 1);
            if (state.SpawnProtection != protection)
            {
                state.SpawnProtection = protection;
                MarkSlotMutation();
            }
        }
    }
}
