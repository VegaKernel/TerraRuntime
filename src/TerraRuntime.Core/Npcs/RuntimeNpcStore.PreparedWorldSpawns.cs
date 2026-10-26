using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    internal bool TryPrepareWorldSpawns(VanillaUnifiedRandom1458 live,
        VanillaUnifiedRandom1458 sharedWorkingRandom, out PreparedWorldSpawns? preparation)
        => TryPrepareWorldSpawns(live, sharedWorkingRandom, spawnWetCollision, out preparation);

    internal bool TryPrepareWorldSpawns(VanillaUnifiedRandom1458 live,
        VanillaUnifiedRandom1458 sharedWorkingRandom, Func<float, float, int, int, bool>? wetCollision,
        out PreparedWorldSpawns? preparation)
    {
        preparation = null;
        if (mutationSerialExhausted || mutationSerial == ulong.MaxValue ||
            ReferenceEquals(live, sharedWorkingRandom) || !IsSpawnRandomSource(live)) return false;
        ulong serial = mutationSerial;
        var wetSource = spawnWetCollision;
        var before = live.Clone();
        var workingBefore = sharedWorkingRandom.Clone();
        var context = _spawnContext?.Invoke();
        if (context is not { IsValid: true } || mutationSerial != serial ||
            !IsSpawnRandomSource(live) || !live.HasSameState(before) ||
            !sharedWorkingRandom.HasSameState(workingBefore) || !ReferenceEquals(wetSource, spawnWetCollision)) return false;
        preparation = new(this, serial, context.Value, live, before, sharedWorkingRandom, wetCollision, wetSource);
        return true;
    }

    /// <summary>
    /// Bounded ordered source NewNPC calls on one detached physical slot table.
    /// The world action owns the shared working RNG and adopts it once. This
    /// preview never copies RNG state to the live stream and never publishes
    /// births before the complete tile/player/item action has been adopted.
    /// </summary>
    internal sealed class PreparedWorldSpawns
    {
        internal const int MaximumCalls = 4096;
        private readonly RuntimeNpcStore owner;
        private readonly RuntimeNpcStore detached;
        private readonly ulong serial;
        private readonly VanillaNpcSpawnContext context;
        private readonly VanillaUnifiedRandom1458 live;
        private readonly VanillaUnifiedRandom1458 before;
        private readonly VanillaUnifiedRandom1458 working;
        private readonly Func<float, float, int, int, bool>? wetSource;
        private readonly List<NpcSnapshot> births = [];
        private readonly HashSet<byte> changed = [];
        private int calls;
        private bool failed;
        private bool adopted;

        internal PreparedWorldSpawns(RuntimeNpcStore owner, ulong serial, VanillaNpcSpawnContext context,
            VanillaUnifiedRandom1458 live, VanillaUnifiedRandom1458 before, VanillaUnifiedRandom1458 working,
            Func<float, float, int, int, bool>? wetCollision, Func<float, float, int, int, bool>? wetSource)
        {
            this.owner = owner; this.serial = serial; this.context = context;
            this.live = live; this.before = before; this.working = working; this.wetSource = wetSource;
            detached = new(owner.Capacity);
            owner._slots.CopyTo(detached._slots, 0);
            detached._activeCount = owner._activeCount;
            detached._spawnRandom = new SystemVanillaNpcRandom(working);
            detached._spawnContext = () => context;
            detached.spawnWetCollision = wetCollision;
        }

        internal bool IsCurrentOwned => !failed && !adopted && !owner.mutationSerialExhausted &&
            owner.mutationSerial == serial && owner.IsSpawnRandomSource(live) && live.HasSameState(before) &&
            ReferenceEquals(wetSource, owner.spawnWetCollision);
        internal bool ValidateContext() => IsCurrentOwned && owner._spawnContext?.Invoke() == context && IsCurrentOwned;
        internal ReadOnlySpan<NpcSnapshot> Births => CollectionsMarshal.AsSpan(births);

        internal bool HasSameSourceEffects(PreparedWorldSpawns other)
        {
            if (!ReferenceEquals(owner, other.owner) || failed || adopted || other.failed || other.adopted ||
                serial != other.serial || context != other.context || !before.HasSameState(other.before) ||
                detached._activeCount != other.detached._activeCount || births.Count != other.births.Count ||
                !changed.SetEquals(other.changed)) return false;
            for (int index = 0; index < detached.Capacity; index++)
                if (!detached._slots[index].Equals(other.detached._slots[index])) return false;
            return CollectionsMarshal.AsSpan(births).SequenceEqual(CollectionsMarshal.AsSpan(other.births));
        }

        internal bool TryCountSourceType(int type, out int count)
        {
            count = 0;
            if (type <= 0 || !IsCurrentOwned) return false;
            for (int slot = 0; slot < Math.Min(detached.Capacity, VanillaNpcSpawnRules.PhysicalSlotCount); slot++)
                if (detached.TryGetActive((byte)slot, out var npc) && npc.TypeIdentity.Value == type) count++;
            return IsCurrentOwned;
        }

        internal bool TryStageLarvaBoss(int bottomX, int bottomY, byte target, int sourcePlayerCount,
            out bool allocated, out NpcSnapshot birth)
        {
            allocated = false; birth = default;
            if ((uint)sourcePlayerCount > byte.MaxValue || target == byte.MaxValue) return Fail();
            var intent = new NpcAiSpawnIntent(VanillaNpcIds.QueenBee, bottomX, bottomY, 0f, 0f, target)
                { StartSlot = 1 };
            if (!TryStageCore(in intent, true, sourcePlayerCount, out allocated, out birth) || !allocated)
                return !failed;
            ref var slot = ref detached._slots[birth.Handle.Slot];
            slot.Update = slot.Update with { Simulation = slot.Update.Simulation with
                { TimeLeft = checked(slot.Update.Simulation.TimeLeft * 20) } };
            birth = Capture(birth.Handle.Slot, in slot);
            births[^1] = birth;
            return IsCurrentOwned;
        }

        internal bool TryStage(in NpcAiSpawnIntent intent, out bool allocated, out NpcSnapshot birth) =>
            TryStageCore(in intent, false, context.ActivePlayers, out allocated, out birth);

        private bool TryStageCore(in NpcAiSpawnIntent intent, bool sourceLarvaBoss, int sourcePlayerCount,
            out bool allocated, out NpcSnapshot birth)
        {
            allocated = false; birth = default;
            if (!IsCurrentOwned || calls == MaximumCalls || !(IsWorldToolNpc(intent.Type) || sourceLarvaBoss && intent.Type == VanillaNpcIds.QueenBee) ||
                !float.IsFinite(intent.VelocityX) || !float.IsFinite(intent.VelocityY) ||
                !intent.InitialAi.IsValidFor(intent.Type) || !intent.InitialLocalAi.IsFinite ||
                !VanillaNpcDefinitionCatalog.TryGet(intent.Type, intent.NetIdOverride ?? new(intent.Type.Value), out _))
                return Fail();
            calls++;
            int type = intent.Type.Value;
            short netId = checked((short)(intent.NetIdOverride?.Value ?? type));
            // Source consumes GoodWorld's draw BEFORE checking for a free slot.
            if (context.GoodWorld)
            {
                var selected = VanillaNpcSpawnRules.ApplyGoodWorldRoll(new(type), working.Next(0, 3));
                if (selected.Value != type) { type = selected.Value; netId = checked((short)type); }
            }
            if (!VanillaNpcDefinitionCatalog.TryGet(new(type), new(netId), out var definition)) return Fail();
            if (!CanAllocate(new(type), intent.StartSlot))
                return !failed; // Source returns maxNPCs=200; caller-side draws still execute.
            VanillaNpcSpawnDefaults? defaults = null;
            float difficulty = context.Difficulty;
            var sourceContext = sourceLarvaBoss ? context with { ActivePlayers = sourcePlayerCount } : context;
            if (VanillaNpcSpawnDefaults.TryResolve(in definition, in sourceContext, out var resolved))
            { defaults = resolved; difficulty = resolved.Difficulty ?? difficulty; }
            else if (sourceLarvaBoss) return Fail();
            var hitbox = defaults?.Hitbox ?? new VanillaNpcHitboxSize(definition.Width, definition.Height);
            var update = new NpcStateUpdate(type, netId,
                intent.BottomX - hitbox.Width * .5f, intent.BottomY - hitbox.Height,
                intent.VelocityX, intent.VelocityY, intent.Target, intent.InitialAi,
                NpcSimulationState.Initial with
                {
                    TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft,
                    SpawnDifficulty = difficulty, LocalAi = intent.InitialLocalAi,
                    CanBeReplacedByOtherNpcs = intent.CanBeReplacedByOtherNpcs
                });
            // NewNPC evaluates WetCollision after allocation/defaults and before retaining the birth.
            // The supplied tool query reads its guarded staged terrain; adoption invokes no provider.
            var beforeWet = working.Clone();
            if (!detached.TryApplySpawnWetCollision(in update, defaults, out var wetUpdate) ||
                !IsCurrentOwned || !working.HasSameState(beforeWet)) return Fail();
            if (!detached.TrySpawnVanillaCore(in wetUpdate, out birth, intent.StartSlot, defaults, publish: false) ||
                !detached.TryRetainPendingBirth(in birth)) return Fail();
            allocated = true;
            changed.Add(birth.Handle.Slot);
            births.Add(birth);
            return true;
        }

        internal bool TryFinish(in NpcSnapshot expected, float velocityX, float velocityY,
            NpcAiState ai, bool canBeReplaced, bool wet, out NpcSnapshot final)
        {
            final = default;
            if (!IsCurrentOwned || !float.IsFinite(velocityX) || !float.IsFinite(velocityY) ||
                !detached.TryGet(expected.Handle, out var current) || current != expected ||
                !ai.IsValidFor(new(expected.Type)) || births.Count == 0 || births[^1] != expected) return Fail();
            ref var slot = ref detached._slots[expected.Handle.Slot];
            slot.Update = slot.Update with
            {
                VelocityX = velocityX, VelocityY = velocityY, Ai = ai,
                Simulation = slot.Update.Simulation with { CanBeReplacedByOtherNpcs = canBeReplaced, Wet = wet }
            };
            final = Capture(expected.Handle.Slot, in slot);
            births[^1] = final;
            return true;
        }

        internal bool TryAdoptUnpublished(out WorldSpawnPublication? publication)
        {
            publication = null;
            if (!IsCurrentOwned || serial > ulong.MaxValue - (ulong)changed.Count) return false;
            var replaced = new List<NpcSnapshot>();
            foreach (byte index in changed)
            {
                ref readonly var old = ref owner._slots[index];
                if (old.Active && old.BirthPending) replaced.Add(Capture(index, in old));
            }
            foreach (byte index in changed)
            {
                owner._slots[index] = detached._slots[index];
                owner.MarkSlotMutation();
            }
            owner._activeCount = detached._activeCount;
            adopted = true;
            publication = new(owner, replaced.ToArray(), births.ToArray());
            return true;
        }

        private bool CanAllocate(NpcTypeId type, byte start)
        {
            int capacity = Math.Min(detached.Capacity, VanillaNpcSpawnRules.PhysicalSlotCount);
            if (start >= capacity) return false;
            int minimum = start == 0 && VanillaNpcSpawnRules.CannotSpawnInSlotZero(type) ? 1 : start;
            bool reverse = VanillaNpcSpawnRules.SearchesInReverse(type);
            if (reverse) minimum++;
            for (int offset = 0; offset < capacity - minimum; offset++)
            {
                int index = reverse ? capacity - 1 - offset : minimum + offset;
                ref readonly var slot = ref detached._slots[index];
                // Generation exhaustion is an ownership failure, not a vanilla full table.
                if (slot.Generation == ulong.MaxValue) { failed = true; return false; }
                if ((!slot.Active && slot.SpawnProtection == 0) || slot.Update.Simulation.CanBeReplacedByOtherNpcs) return true;
            }
            return false;
        }
        private bool Fail() { failed = true; return false; }
        private static bool IsWorldToolNpc(NpcTypeId type) => type.Value is
            1 or 6 or 9 or 16 or 173 or 46 or 51 or 62 or 66 or 74 or 210 or 211 or 297 or 298 or 299 or
            301 or 356 or 442 or 444 or 538 or 539 or 583 or 584 or 585 or 603 or 611 or 614 or
            653 or 654 or 655 or 671 or 672 or 673 or 674 or 675;
    }

    internal sealed class WorldSpawnPublication(RuntimeNpcStore owner, NpcSnapshot[] replaced, NpcSnapshot[] births)
    {
        private int next;
        private bool publishedReplacements;
        private bool publishing;
        internal bool TryPublishNext()
        {
            if (publishing || next == births.Length) return false;
            var birth = births[next++];
            publishing = true;
            Exception? failure = null;
            try
            {
                if (!publishedReplacements)
                {
                    publishedReplacements = true;
                    foreach (var old in replaced)
                        try { owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in old); }
                        catch (Exception exception) { failure ??= exception; }
                }
                if (owner._commitSink is INpcBirthRetentionSink retained &&
                    owner.TryGet(birth.Handle, out var current) && current == birth)
                    try { retained.NpcBirthRetained(in birth); }
                    catch (Exception exception) { failure ??= exception; }
            }
            finally { publishing = false; }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }
    }
}
