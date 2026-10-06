using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeNpcBuffAdditionCheckpoint1458(NpcHandle Handle, ulong Revision,
    RuntimeNpcBuffSlots1458 Slots, PlayerDebuffSnapshot1458 Flags, PlayerDebuffSnapshot1458 PreviousFlags,
    bool Stinky, bool PreviousStinky, RuntimeNpcStinkyVisualOffer1458 VisualOffer,
    int DefinitionType, int DefinitionNetId, bool TorchImmunity);

internal sealed class RuntimeNpcBuffAdditionPlan1458
{
    internal RuntimeNpcBuffAdditionPlan1458(RuntimeNpcBuffStatus1458 owner, in NpcSnapshot expected,
        in RuntimeNpcBuffAdditionCheckpoint1458 checkpoint)
    {
        Owner = owner;
        Expected = expected;
        Checkpoint = checkpoint;
        Slots = checkpoint.Slots;
    }

    internal RuntimeNpcBuffStatus1458 Owner { get; }
    internal NpcSnapshot Expected { get; }
    internal RuntimeNpcBuffAdditionCheckpoint1458 Checkpoint { get; }
    internal RuntimeNpcBuffSlots1458 Slots;
    internal bool Changed;
    internal bool Adopted;
    internal bool Published;
    internal NpcSnapshot Accepted;
    internal ulong AdoptedRevision;
    internal bool IsCurrent => Owner.IsCurrentAddition(this);
}

internal sealed partial class RuntimeNpcBuffStatus1458
{
    internal bool CaptureAddition(in NpcSnapshot expected, out RuntimeNpcBuffAdditionPlan1458 plan)
    {
        plan = default!;
        if (expected.TypeIdentity != VanillaNpcIds.Zombie && expected.TypeIdentity != VanillaNpcIds.BlueSlime &&
            expected.TypeIdentity != VanillaNpcIds.LavaSlime) return false;
        if (!EnsureGeneration(expected.Handle) || !npcs.TryGet(expected.Handle, out var current) ||
            current != expected) return false;
        ref Entry entry = ref entries[expected.Handle.Slot];
        if (entry.Revision == ulong.MaxValue) return false;
        // The admitted owner has only source debuffs. Unknown table contents cannot choose a replacement victim.
        for (int i = 0; i < Capacity; i++)
            if (entry.Slots[i].Type != 0 && entry.Slots[i].Type != VanillaBuffIds.OnFire.Value &&
                entry.Slots[i].Type != VanillaBuffIds.Poisoned.Value &&
                entry.Slots[i].Type != VanillaBuffIds.Stinky.Value) return false;
        var checkpoint = CaptureAdditionCheckpoint(in entry);
        plan = new(this, in expected, in checkpoint);
        return true;
    }

    internal bool TryPlanAddition(in NpcSnapshot expected, BuffTypeId type, int duration,
        out RuntimeNpcBuffAdditionPlan1458 plan)
    {
        plan = default!;
        if ((type != VanillaBuffIds.OnFire && type != VanillaBuffIds.Poisoned) ||
            duration is <= 0 or > short.MaxValue || !CaptureAddition(in expected, out plan)) return false;
        bool immune = expected.TypeIdentity != VanillaNpcIds.Zombie &&
            (type == VanillaBuffIds.Poisoned || expected.TypeIdentity == VanillaNpcIds.LavaSlime);
        if (immune) return true;
        int free = -1;
        for (int i = 0; i < Capacity; i++)
        {
            if (plan.Slots[i].Type == type.Value)
            {
                if (plan.Slots[i].Duration < duration)
                {
                    plan.Slots[i] = new((ushort)type.Value, duration);
                    plan.Changed = true;
                }
                return true;
            }
            if (free < 0 && plan.Slots[i].Type == 0) free = i;
        }
        if (free >= 0)
        {
            plan.Slots[free] = new((ushort)type.Value, duration);
            plan.Changed = true;
        }
        return true;
    }

    internal bool IsCurrentAddition(RuntimeNpcBuffAdditionPlan1458 plan) =>
        ReferenceEquals(plan.Owner, this) && !plan.Adopted &&
        npcs.TryGet(plan.Expected.Handle, out var current) && current == plan.Expected &&
        AdditionCheckpointMatches(plan);

    internal bool IsAcceptedAdditionCurrent(RuntimeNpcBuffAdditionPlan1458 plan)
    {
        if (!ReferenceEquals(plan.Owner, this) || !plan.Adopted ||
            !npcs.TryGet(plan.Accepted.Handle, out var current) || current != plan.Accepted) return false;
        ref Entry entry = ref entries[current.Handle.Slot];
        var checkpoint = CaptureAdditionCheckpoint(in entry);
        var expected = plan.Checkpoint with { Revision = plan.AdoptedRevision, Slots = plan.Slots };
        return AdditionCheckpointsMatch(in checkpoint, in expected);
    }

    internal bool TryAdoptAddition(RuntimeNpcBuffAdditionPlan1458 plan, in NpcSnapshot acceptedNpc)
    {
        if (!ReferenceEquals(plan.Owner, this) || plan.Adopted || !AdditionCheckpointMatches(plan) ||
            !npcs.TryGet(acceptedNpc.Handle, out var current) || current != acceptedNpc ||
            acceptedNpc.Handle != plan.Expected.Handle || acceptedNpc.TypeIdentity != plan.Expected.TypeIdentity ||
            acceptedNpc.NetIdentity != plan.Expected.NetIdentity) return false;
        // A direct AddBuff has no NPC mutation; a retained strike commits exactly one owned NPC revision.
        bool unchanged = acceptedNpc == plan.Expected;
        bool strike = plan.Expected.Revision.Value != ulong.MaxValue &&
            acceptedNpc.Revision.Value == plan.Expected.Revision.Value + 1;
        if (!unchanged && !strike) return false;
        ref Entry entry = ref entries[acceptedNpc.Handle.Slot];
        if (plan.Changed)
        {
            entry.Slots = plan.Slots;
            entry.Revision++;
        }
        plan.Accepted = acceptedNpc;
        plan.AdoptedRevision = entry.Revision;
        plan.Adopted = true;
        return true;
    }

    internal void PublishAddition(RuntimeNpcBuffAdditionPlan1458 plan)
    {
        if (!ReferenceEquals(plan.Owner, this) || !plan.Adopted || plan.Published) return;
        plan.Published = true;
        if (!plan.Changed || !npcs.TryGet(plan.Accepted.Handle, out var current) || current != plan.Accepted) return;
        ref Entry entry = ref entries[current.Handle.Slot];
        if (entry.Handle != current.Handle || entry.Revision != plan.AdoptedRevision) return;
        buffListChanged?.Invoke(current.Handle);
    }

    private bool AdditionCheckpointMatches(RuntimeNpcBuffAdditionPlan1458 plan)
    {
        ref Entry entry = ref entries[plan.Expected.Handle.Slot];
        var checkpoint = CaptureAdditionCheckpoint(in entry);
        var expected = plan.Checkpoint;
        return AdditionCheckpointsMatch(in checkpoint, in expected);
    }

    private static bool AdditionCheckpointsMatch(in RuntimeNpcBuffAdditionCheckpoint1458 checkpoint,
        in RuntimeNpcBuffAdditionCheckpoint1458 expected)
    {
        for (int i = 0; i < Capacity; i++)
            if (checkpoint.Slots[i] != expected.Slots[i]) return false;
        return checkpoint.Handle == expected.Handle && checkpoint.Revision == expected.Revision &&
            checkpoint.Flags == expected.Flags && checkpoint.PreviousFlags == expected.PreviousFlags &&
            checkpoint.Stinky == expected.Stinky && checkpoint.PreviousStinky == expected.PreviousStinky &&
            checkpoint.VisualOffer == expected.VisualOffer && checkpoint.DefinitionType == expected.DefinitionType &&
            checkpoint.DefinitionNetId == expected.DefinitionNetId && checkpoint.TorchImmunity == expected.TorchImmunity;
    }

    private static RuntimeNpcBuffAdditionCheckpoint1458 CaptureAdditionCheckpoint(in Entry entry) =>
        new(entry.Handle, entry.Revision, entry.Slots, entry.Flags, entry.PreviousFlags, entry.Stinky,
            entry.PreviousStinky, entry.VisualOffer, entry.DefinitionType, entry.DefinitionNetId, entry.TorchImmunity);
}
