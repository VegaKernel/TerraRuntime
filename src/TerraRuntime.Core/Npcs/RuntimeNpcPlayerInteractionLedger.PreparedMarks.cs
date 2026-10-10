using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcPlayerInteractionLedger
{
    internal bool TryPrepareMarks(ReadOnlySpan<NpcHandle> targets, PlayerHandle player,
        out MarksPreparation? preparation)
    {
        preparation = null;
        if (!player.IsAssigned || player.Slot.Value >= VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots ||
            targets.Length > _store.Capacity) return false;
        Span<bool> seen = stackalloc bool[_store.Capacity];
        seen.Clear();
        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (!target.IsAssigned || target.Slot >= seen.Length || seen[target.Slot] ||
                !_store.TryGet(target, out _)) return false;
            seen[target.Slot] = true;
        }
        preparation = new(this, player, targets);
        return true;
    }

    private readonly record struct MarkState(NpcHandle Target, bool HasMask, PlayerSlotMask Mask,
        bool HasLast, (NpcHandle Handle, PlayerSlotId Player) Last);

    internal sealed class MarksPreparation
    {
        private readonly RuntimeNpcPlayerInteractionLedger owner;
        private readonly PlayerHandle player;
        private readonly MarkState[] states;
        private bool adopted;

        internal MarksPreparation(RuntimeNpcPlayerInteractionLedger owner, PlayerHandle player, ReadOnlySpan<NpcHandle> targets)
        {
            this.owner = owner; this.player = player;
            states = new MarkState[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                bool hasMask = owner._interactions.TryGetValue(target, out var mask);
                bool hasLast = owner._lastInteraction.TryGetValue(target.Slot, out var last);
                states[i] = new(target, hasMask, mask, hasLast, last);
            }
        }

        internal bool IsCurrent
        {
            get
            {
                if (adopted) return false;
                foreach (var state in states)
                    if (!owner._store.TryGet(state.Target, out _) ||
                        owner._interactions.TryGetValue(state.Target, out var mask) != state.HasMask ||
                        mask != state.Mask ||
                        owner._lastInteraction.TryGetValue(state.Target.Slot, out var last) != state.HasLast ||
                        last != state.Last) return false;
                return true;
            }
        }

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            adopted = true;
            // All handles and ledger entries were checked before this concrete callback-free tail.
            foreach (var state in states)
                if (!owner.TryMark(state.Target, player))
                    throw new InvalidOperationException("An admitted NPC interaction lost its retained handle.");
            return true;
        }
    }
}
