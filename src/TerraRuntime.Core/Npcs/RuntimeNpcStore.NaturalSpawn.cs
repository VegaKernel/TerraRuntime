using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    internal bool TryCreateNaturalSpawnPreview(VanillaUnifiedRandom1458 random, out NaturalSpawnPreview? preview)
    {
        preview = null;
        if (mutationSerialExhausted || mutationSerial == ulong.MaxValue || !IsSpawnRandomSource(random))
            return false;
        ulong serial = mutationSerial;
        var before = random.Clone();
        var context = _spawnContext?.Invoke();
        if (context is { IsValid: false } || mutationSerial != serial || !IsSpawnRandomSource(random) ||
            !random.HasSameState(before))
            return false;
        preview = new(this, serial, context, random, before);
        return true;
    }

    // Parentless birth uses the existing physical allocator on a detached table. Admission and RNG
    // adoption complete before retention/publication callbacks; no speculative slot is externally visible.
    internal sealed class NaturalSpawnPreview
    {
        private readonly RuntimeNpcStore owner;
        private readonly ulong serial;
        private readonly VanillaNpcSpawnContext? context;
        private readonly VanillaUnifiedRandom1458 live;
        private readonly VanillaUnifiedRandom1458 before;
        private RuntimeNpcStore? detached;
        private NpcSnapshot? child;
        private bool completed;

        internal NaturalSpawnPreview(RuntimeNpcStore owner, ulong serial, VanillaNpcSpawnContext? context,
            VanillaUnifiedRandom1458 live, VanillaUnifiedRandom1458 before)
        {
            this.owner = owner;
            this.serial = serial;
            this.context = context;
            this.live = live;
            this.before = before;
            Random = before.Clone();
        }

        internal VanillaUnifiedRandom1458 Random { get; }
        internal bool ValidateContext() => !completed && owner._spawnContext?.Invoke() == context && IsCurrentOwned();
        internal bool IsCurrentOwned() => !completed && !owner.mutationSerialExhausted &&
            owner.mutationSerial == serial && owner.IsSpawnRandomSource(live) && live.HasSameState(before);

        internal bool TryStage(in NpcStateUpdate update, float centerX, float bottomY, out NpcSnapshot birth)
        {
            birth = default;
            if (child is not null || !IsCurrentOwned())
                return false;
            detached = new RuntimeNpcStore(owner.Capacity);
            owner._slots.CopyTo(detached._slots, 0);
            detached._activeCount = owner._activeCount;
            detached._spawnRandom = new SystemVanillaNpcRandom(Random);
            if (context is { } captured)
                detached._spawnContext = () => captured;
            if (!float.IsFinite(centerX) || !float.IsFinite(bottomY) || centerX != (int)centerX || bottomY != (int)bottomY)
                return false;
            var intent = new NpcAiSpawnIntent(new(update.Type), (int)centerX, (int)bottomY,
                update.VelocityX, update.VelocityY, update.Target)
            {
                InitialAi = update.Ai,
                NetIdOverride = new(update.NetId)
            };
            // NewNPC overwrites SetDefaults ai[] with its explicit arguments, including all-zero AI.
            if (!detached.TrySpawnIntent(in intent, out birth) || !detached.TryRetainPendingBirth(in birth))
                return false;
            child = birth;
            return true;
        }

        internal bool TryAdoptOwned(out NpcSnapshot? birth)
        {
            birth = null;
            if (!IsCurrentOwned())
                return false;
            NpcSnapshot? replaced = null;
            if (child is { } staged)
            {
                int slot = staged.Handle.Slot;
                if (detached is null || !detached._slots[slot].BirthPending)
                    return false;
                if (owner._slots[slot].Active && owner._slots[slot].BirthPending)
                    replaced = Capture((byte)slot, in owner._slots[slot]);
                if (!owner._slots[slot].Active)
                    owner._activeCount++;
                owner._slots[slot] = detached._slots[slot];
                owner.MarkSlotMutation();
                birth = staged;
            }
            live.CopyStateFrom(Random);
            completed = true;
            if (replaced is { } old)
                owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in old);
            if (birth is { } born && owner._commitSink is INpcBirthRetentionSink retention && owner.MatchesSource(in born))
                retention.NpcBirthRetained(in born);
            return true;
        }
    }
}
