using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    internal bool TryPrepareProjectedTerminalRemoval(in NpcSnapshot projected, out TerminalRemoval? removal)
    {
        removal = null;
        if ((projected.TypeIdentity != TerraRuntime.Contracts.Gameplay.VanillaNpcIds.PirateCaptain &&
             projected.TypeIdentity != TerraRuntime.Contracts.Gameplay.VanillaNpcIds.PirateGhost) ||
            projected.Simulation.Life != 0 || !TryGet(projected.Handle, out var current) ||
            current.Revision != projected.Revision || current.Revision.Value == ulong.MaxValue)
            return false;
        removal = new(this, projected, true);
        return true;
    }

    internal bool TryPrepareTerminalRemoval(in NpcSnapshot expected, out TerminalRemoval? removal)
    {
        removal = null;
        if (expected.Simulation.Life != 0 || !MatchesSource(in expected)) return false;
        removal = new TerminalRemoval(this, expected);
        return true;
    }

    internal sealed class TerminalRemoval
    {
        private readonly RuntimeNpcStore owner;
        private readonly NpcSnapshot expected;
        private readonly SlotState before;
        private SlotState after;
        private bool adopted;
        private bool published;

        internal TerminalRemoval(RuntimeNpcStore owner, NpcSnapshot expected, bool projected = false)
        {
            this.owner = owner;
            var retained = owner._slots[expected.Handle.Slot];
            if (projected)
            {
                retained.Revision++;
                retained.Update = new(expected.Type, expected.NetId, expected.PositionX, expected.PositionY,
                    expected.VelocityX, expected.VelocityY, expected.Target, expected.Ai, expected.Simulation);
                expected = Capture(expected.Handle.Slot, retained);
            }
            this.expected = expected;
            before = retained;
        }

        private static bool Same(in SlotState left, in SlotState right) =>
            left.Active == right.Active && left.BirthPending == right.BirthPending &&
            left.SpawnProtection == right.SpawnProtection && left.Generation == right.Generation &&
            left.Revision == right.Revision && left.Update == right.Update;

        internal bool IsCurrent => !adopted && owner.MatchesSource(in expected) &&
            Same(owner._slots[expected.Handle.Slot], before);

        internal bool IsAcceptedCurrent => adopted && Same(owner._slots[expected.Handle.Slot], after);

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            ref SlotState state = ref owner._slots[expected.Handle.Slot];
            state.BirthPending = false;
            state.Active = false;
            state.Revision = 0;
            owner._activeCount--;
            owner.MarkSlotMutation();
            after = state;
            adopted = true;
            return true;
        }

        internal bool TryPublish()
        {
            if (!adopted || published) return false;
            published = true;
            if (!Same(owner._slots[expected.Handle.Slot], after)) return true;
            if (before.BirthPending)
            {
                owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in expected);
                if (!Same(owner._slots[expected.Handle.Slot], after)) return true;
            }
            owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Despawn, in expected);
            return true;
        }
    }
}
