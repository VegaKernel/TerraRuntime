using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal interface IRuntimeGemBunnyStatusPhase1458
{
    byte[]? ExpiredBuffFrame { get; }
    byte[]? AiBuffFrame { get; }
    void PublishAiStatus();
    byte[]? CollisionBuffFrame { get; }
    void PublishCollisionStatus();
    bool TryStageWetRemoval(in NpcStateUpdate collision, RuntimeNpcBuffAdditionPlan1458? addition = null);
    bool TryPlanLavaAddition(in NpcSnapshot accepted, out RuntimeNpcBuffAdditionPlan1458 addition, out byte[]? frame);
    bool TryAdoptStatus(in NpcSnapshot accepted, RuntimeNpcBuffAdditionPlan1458? addition);
}

internal sealed partial class RuntimeNpcBuffStatus1458
{
    internal bool TryPlanGemBunnyLowerWorldPhase(in NpcSnapshot source, out GemBunnyLowerWorldPhase1458 phase)
    {
        phase = default!;
        if (!TryPlanGemBunnyPhase(in source, out var plan)) return false;
        phase = new(this, plan);
        return true;
    }

    internal sealed class GemBunnyLowerWorldPhase1458(RuntimeNpcBuffStatus1458 owner, RuntimeNpcBuffPlan1458 phase)
        : IRuntimeGemBunnyStatusPhase1458
    {
        private RuntimeNpcStinkyVisualOffer1458 offer;
        internal bool IsCurrent => owner.IsCurrentGemBunnyPhase(in phase);
        internal bool StageAfterLoot(TerraRuntime.Core.VanillaUnifiedRandom1458 random)
        {
            if (!IsCurrent) return false;
            offer = PlanVisualOffers(in phase, new NpcRuntimeTownCombatRandom1458(new SystemVanillaNpcRandom(random)), false);
            return IsCurrent;
        }
        public byte[]? ExpiredBuffFrame
        {
            get
            {
                var slots = phase.Slots;
                return phase.Expired && TryPrepareGemBunnyBuffFrame(phase.Expected.Handle.Slot,
                    in slots, out var frame) ? frame : null;
            }
        }
        public byte[]? AiBuffFrame => null;
        public void PublishAiStatus() { }
        public byte[]? CollisionBuffFrame => null;
        public void PublishCollisionStatus() { }
        public bool TryStageWetRemoval(in NpcStateUpdate collision, RuntimeNpcBuffAdditionPlan1458? addition = null) => false;
        public bool TryPlanLavaAddition(in NpcSnapshot accepted, out RuntimeNpcBuffAdditionPlan1458 addition, out byte[]? frame)
        { addition = default!; frame = null; return false; }
        public bool TryAdoptStatus(in NpcSnapshot accepted, RuntimeNpcBuffAdditionPlan1458? addition) =>
            addition is null && owner.TryAdoptGemBunnyPhase(in phase, in accepted, null) &&
            owner.ObserveVisualOffer(accepted.Handle, in offer);
    }

    internal static RuntimeNpcBuffPlan1458 PlanGemBunnyRepeatedStinky(in RuntimeNpcBuffPlan1458 phase)
    {
        var slots = phase.Slots;
        int free = -1;
        for (int i = 0; i < Capacity; i++)
        {
            if (slots[i].Type == 120)
            {
                if (slots[i].Duration <= 10) slots[i] = new(120, 180);
                return phase with { Slots = slots };
            }
            if (free < 0 && slots[i].Type == 0) free = i;
        }
        if (free >= 0) slots[free] = new(120, 180);
        return phase with { Slots = slots };
    }

    internal static RuntimeNpcBuffSlots1458 PlanGemBunnyWetRemoval(in RuntimeNpcBuffSlots1458 original, bool stinky, bool wet)
    {
        var slots = original;
        if (!stinky || !wet) return slots;
        // Collision_WaterCollision removes Stinky even in lava. BuffSetFlags remains the
        // current update's flag; removal only changes the source buff slots and its packet54.
        for (int i = 0; i < Capacity; i++) if (slots[i].Type == 120) RemoveAt(ref slots, i);
        return slots;
    }

    internal bool TryPlanGemBunnyPhase(in NpcSnapshot before, out RuntimeNpcBuffPlan1458 plan)
    {
        plan = default;
        if (!VanillaGroundCritterAi1458.IsSupported(before.Type) || !TryPlan(in before, out plan)) return false;
        // The existing public status ingress admits Stinky for these critters. Fire is produced by
        // collision immediately before their lethal50 strike; wider imported DOT histories remain unowned.
        ref Entry entry = ref entries[before.Handle.Slot];
        for (int i = 0; i < Capacity; i++)
            if (entry.Slots[i].Type is not (0 or 120)) return false;
        return plan.LifeAfter > 0;
    }

    internal bool IsCurrentGemBunnyPhase(in RuntimeNpcBuffPlan1458 plan) =>
        VanillaGroundCritterAi1458.IsSupported(plan.Expected.Type) &&
        npcs.TryGet(plan.Expected.Handle, out var current) && current.Type == plan.Expected.Type &&
        current.NetId == plan.Expected.NetId && entries[current.Handle.Slot].Handle == current.Handle &&
        entries[current.Handle.Slot].Revision == plan.StatusRevision;

    internal bool TryPlanGemBunnyLavaAddition(in RuntimeNpcBuffPlan1458 phase, in NpcSnapshot accepted,
        out RuntimeNpcBuffAdditionPlan1458 addition)
    {
        addition = default!;
        if (!IsCurrentGemBunnyPhase(in phase) || accepted.Handle != phase.Expected.Handle ||
            !npcs.TryGet(accepted.Handle, out var current) || current != accepted) return false;
        ref Entry entry = ref entries[accepted.Handle.Slot];
        if (entry.Revision == ulong.MaxValue) return false;
        var checkpoint = CaptureAdditionCheckpoint(in entry);
        addition = new(this, in accepted, in checkpoint) { Slots = phase.Slots };
        int free = -1;
        for (int i = 0; i < Capacity; i++)
        {
            if (addition.Slots[i].Type == 24)
            {
                if (addition.Slots[i].Duration < 420)
                { addition.Slots[i] = new(24, 420); addition.Changed = true; }
                return true;
            }
            if (free < 0 && addition.Slots[i].Type == 0) free = i;
        }
        if (free >= 0) { addition.Slots[free] = new(24, 420); addition.Changed = true; }
        return true;
    }

    internal bool TryAdoptGemBunnyPhase(in RuntimeNpcBuffPlan1458 phase, in NpcSnapshot accepted,
        RuntimeNpcBuffAdditionPlan1458? addition)
    {
        if (addition is null) return Commit(in phase, in accepted);
        if (!IsCurrentGemBunnyPhase(in phase) || !ReferenceEquals(addition.Owner, this) || addition.Adopted ||
            !AdditionCheckpointMatches(addition) || accepted.Handle != phase.Expected.Handle ||
            accepted.Simulation.LifeRegenCounter != phase.CounterAfter ||
            !npcs.TryGet(accepted.Handle, out var current) || current != accepted) return false;
        ref Entry entry = ref entries[accepted.Handle.Slot];
        entry.Slots = addition.Slots; entry.Flags = phase.Flags; entry.Stinky = phase.Stinky;
        entry.LoveStruck = phase.LoveStruck; entry.Revision++;
        addition.Accepted = accepted; addition.AdoptedRevision = entry.Revision; addition.Adopted = true;
        return true;
    }

    internal static bool TryPrepareGemBunnyBuffFrame(byte slot, in RuntimeNpcBuffSlots1458 slots, out byte[] frame)
    {
        Span<TerrariaNpcBuffEntryState> rows = stackalloc TerrariaNpcBuffEntryState[Capacity];
        int count = 0;
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Type > 0 && slots[i].Duration > 0)
                rows[count++] = new(slots[i].Type, checked((ushort)slots[i].Duration));
        return TerrariaNpcBuffCodec.TryEncodeCurrent(slot, rows[..count], out frame);
    }
}
