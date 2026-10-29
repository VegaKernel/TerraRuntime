using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcBuffStatus1458
{
    // Direct powder/catch interactions must not run UpdateBuffs or age its physical slots.
    internal bool CaptureInteraction(in NpcSnapshot expected, out RuntimeNpcBuffAdditionCheckpoint1458 checkpoint)
    {
        checkpoint = default;
        if (!npcs.TryGet(expected.Handle, out var current) || current != expected) return false;
        if (!EnsureGeneration(expected.Handle))
        {
            // These existing metadata interactions can precede full AI admission. An unadmitted
            // actor cannot receive buffs through this owner, so its fresh generation is empty.
            if (expected.Type != 687 && !VanillaTownNpcRescue1458.TryGet(expected.TypeIdentity, out _)) return false;
            ref Entry pending = ref entries[expected.Handle.Slot];
            if (pending.Handle != expected.Handle)
                pending = new Entry { Handle = expected.Handle, Revision = 1,
                    DefinitionType = expected.Type, DefinitionNetId = expected.NetId };
            if (pending.DefinitionType != expected.Type || pending.DefinitionNetId != expected.NetId) return false;
        }
        ref Entry entry = ref entries[expected.Handle.Slot];
        if (entry.Revision == ulong.MaxValue) return false;
        checkpoint = CaptureAdditionCheckpoint(in entry);
        return true;
    }

    internal bool IsCurrentInteraction(in NpcSnapshot expected, in RuntimeNpcBuffAdditionCheckpoint1458 checkpoint)
    {
        if (!npcs.TryGet(expected.Handle, out var current) || current != expected) return false;
        var observed = CaptureAdditionCheckpoint(in entries[expected.Handle.Slot]);
        return AdditionCheckpointsMatch(in observed, in checkpoint);
    }

    internal bool TryAdoptInteraction(in NpcSnapshot before, in RuntimeNpcBuffAdditionCheckpoint1458 checkpoint,
        in NpcSnapshot accepted, bool materializedTransform, out RuntimeNpcBuffAdditionCheckpoint1458 after)
    {
        after = default;
        if (before.Handle != accepted.Handle || before.Revision.Value == ulong.MaxValue ||
            accepted.Revision.Value != before.Revision.Value + 1 ||
            !IsCurrentInteraction(in accepted, in checkpoint)) return false;
        ref Entry entry = ref entries[before.Handle.Slot];
        if (materializedTransform)
        {
            if (entry.Revision == ulong.MaxValue || accepted.Type != accepted.NetId ||
                !VanillaNpcDefinitionCatalog.TryGet(accepted.TypeIdentity, accepted.NetIdentity, out _)) return false;
            AdoptTransformedDefinition(ref entry, in accepted, checkpoint.Slots);
            entry.Revision++;
        }
        else if (accepted.Type != before.Type || accepted.NetId != before.NetId) return false;
        after = CaptureAdditionCheckpoint(in entry);
        return true;
    }

    internal static bool TryPlanMaterializedTransform(in RuntimeNpcBuffPlan1458 phase,
        in NpcSnapshot initialized, out RuntimeNpcBuffPlan1458 transformed)
    {
        transformed = default;
        if (initialized.Handle != phase.Expected.Handle || initialized.Type != initialized.NetId ||
            !VanillaNpcDefinitionCatalog.TryGet(initialized.TypeIdentity, initialized.NetIdentity, out _)) return false;
        // NPC.SetDefaults preserves buffType/buffTime, but clears its per-update flag views.
        // The current phase has already aged the slots; never replay UpdateBuffs here.
        transformed = phase with { Flags = default, Stinky = false, LoveStruck = false,
            CounterAfter = initialized.Simulation.LifeRegenCounter, TransformedType = initialized.TypeIdentity };
        return true;
    }

    private static void AdoptTransformedDefinition(ref Entry entry, in NpcSnapshot accepted,
        in RuntimeNpcBuffSlots1458 slots)
    {
        entry.Slots = slots;
        entry.Flags = entry.PreviousFlags = default;
        entry.Stinky = entry.PreviousStinky = entry.LoveStruck = false;
        entry.TorchImmunity = false;
        entry.DefinitionType = accepted.Type;
        entry.DefinitionNetId = accepted.NetId;
        // VisualOffer records arguments already offered by this phase, not an NPC buff flag.
    }
}
