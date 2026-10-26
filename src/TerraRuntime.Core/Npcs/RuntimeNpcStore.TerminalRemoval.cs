using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    internal bool TryPrepareCatchRemoval(in NpcSnapshot expected, out TerminalRemoval? removal)
    {
        removal = null;
        if (!MatchesSource(in expected) ||
            !TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.TryGet(expected.TypeIdentity, expected.NetIdentity, out var definition) ||
            !definition.SupportsRuntimeLifecycle ||
            !TerraRuntime.Gameplay.Npcs.VanillaNpcCatchCatalog1458.TryGetCatchItem(expected.TypeIdentity, out _)) return false;
        removal = new(this, expected, catchRemoval: true);
        return true;
    }

    internal bool TryPrepareProjectedTerminalRemoval(in NpcSnapshot projected, out TerminalRemoval? removal)
    {
        removal = null;
        // Content/death-producer admission belongs to the caller. This owner only
        // retains the exact revision produced by one forthcoming unpublished HP write.
        if (projected.Simulation.Life != 0 || !TryGet(projected.Handle, out var current) ||
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
        private readonly NpcSnapshot beforeStrike;
        private readonly SlotState before;
        private readonly bool catchRemoval;
        private SlotState after;
        private bool adopted;
        private bool published;
        private bool pendingBirthPublished;

        internal TerminalRemoval(RuntimeNpcStore owner, NpcSnapshot expected, bool projected = false, bool catchRemoval = false)
        {
            this.owner = owner;
            this.catchRemoval = catchRemoval;
            var retained = owner._slots[expected.Handle.Slot];
            beforeStrike = Capture(expected.Handle.Slot, retained);
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

        internal NpcSnapshot Expected => expected;

        internal bool TryCapturePendingBirth(out NpcSnapshot snapshot)
        {
            snapshot = beforeStrike;
            return before.BirthPending;
        }

        internal bool TryPublishPendingBirthBeforeStrike()
        {
            if (!adopted || pendingBirthPublished) return false;
            pendingBirthPublished = true;
            if (before.BirthPending && IsAcceptedCurrent)
                owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in beforeStrike);
            return true;
        }

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
            if (catchRemoval)
            {
                owner._commitSink?.NpcCatchCommitted(in expected, before.BirthPending);
                return true;
            }
            if (before.BirthPending && !pendingBirthPublished)
            {
                pendingBirthPublished = true;
                owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in expected);
                if (!Same(owner._slots[expected.Handle.Slot], after)) return true;
            }
            owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Despawn, in expected);
            return true;
        }
    }
}
