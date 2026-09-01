using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>Authoritative source-shaped bound-town transform transaction for TerrariaServer 1.4.5.8.</summary>
internal sealed class RuntimeTownNpcRescueService1458
{
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeWorldProgressionMutations progression;

    public RuntimeTownNpcRescueService1458(
        RuntimeNpcStore npcs,
        RuntimeTownNpcStateStore townNpcs,
        RuntimeWorldProgressionMutations progression)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.townNpcs = townNpcs ?? throw new ArgumentNullException(nameof(townNpcs));
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
    }

    public bool TryRescueTalk(short npcSlot, out NpcSnapshot transformed)
    {
        transformed = default;
        if ((uint)npcSlot >= TerraRuntime.Protocol.Multiplicity.TerrariaNpcTalkCodec.MaximumNpcSlots ||
            !npcs.TryGetActive(checked((byte)npcSlot), out NpcSnapshot source) ||
            !NpcTypeId.TryCreate(source.Type, out NpcTypeId sourceType) ||
            !VanillaTownNpcRescue1458.TryGetTalkRule(sourceType, out VanillaTownNpcRescueRule1458 rule))
        {
            return false;
        }
        return TryTransform(in source, in rule, out transformed);
    }

    public bool TryRescuePurificationPowder(NpcHandle handle, out NpcSnapshot transformed)
    {
        transformed = default;
        if (!npcs.TryGet(handle, out NpcSnapshot source) ||
            !NpcTypeId.TryCreate(source.Type, out NpcTypeId sourceType) ||
            !VanillaTownNpcRescue1458.TryGet(sourceType, out VanillaTownNpcRescueRule1458 rule) ||
            rule.Trigger != VanillaTownNpcRescueTrigger1458.PurificationPowder)
        {
            return false;
        }

        return TryTransform(in source, in rule, out transformed);
    }

    private bool TryTransform(
        in NpcSnapshot source,
        in VanillaTownNpcRescueRule1458 rule,
        out NpcSnapshot transformed)
    {
        transformed = default;
        short slot = source.Handle.Slot;
        if (!townNpcs.CanAdoptRescuedResident(slot, rule.ResidentType) ||
            !VanillaTownNpcFacts1458.TryGetDefinition(rule.ResidentType, out VanillaNpcDefinition target))
        {
            return false;
        }

        int oldLifeMax = source.Simulation.LifeMax > 0 ? source.Simulation.LifeMax : rule.BoundLifeMax;
        int oldLife = source.Simulation.Life > 0 ? source.Simulation.Life : oldLifeMax;
        int life = Math.Max(1, oldLife * target.LifeMax / oldLifeMax);
        float positionY = source.PositionY + rule.BoundHeight - target.Height;
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            Life = life,
            LifeMax = target.LifeMax,
            TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            DirectionX = source.Simulation.DirectionX,
            DirectionY = source.Simulation.DirectionY,
            SpriteDirection = source.Simulation.SpriteDirection
        };
        var update = new NpcStateUpdate(
            Type: rule.ResidentType.Value,
            NetId: checked((short)rule.ResidentType.Value),
            PositionX: source.PositionX,
            PositionY: positionY,
            VelocityX: source.VelocityX,
            VelocityY: source.VelocityY,
            Target: source.Target,
            Ai: default,
            Simulation: simulation);
        if (!npcs.TryUpdate(source.Handle, in update, out transformed))
            return false;
        if (!townNpcs.TryAdoptRescuedResident(slot, rule.ResidentType, in transformed))
            throw new InvalidOperationException("Preflighted town rescue could not be committed to the persistent roster.");

        progression.MarkTownNpcRescued(ToRuntimeFact(rule.Fact));
        return true;
    }

    private static RuntimeTownRescueFacts1458 ToRuntimeFact(VanillaTownNpcRescueFact1458 fact) => fact switch
    {
        VanillaTownNpcRescueFact1458.Goblin => RuntimeTownRescueFacts1458.Goblin,
        VanillaTownNpcRescueFact1458.Wizard => RuntimeTownRescueFacts1458.Wizard,
        VanillaTownNpcRescueFact1458.Mechanic => RuntimeTownRescueFacts1458.Mechanic,
        VanillaTownNpcRescueFact1458.Stylist => RuntimeTownRescueFacts1458.Stylist,
        VanillaTownNpcRescueFact1458.Angler => RuntimeTownRescueFacts1458.Angler,
        VanillaTownNpcRescueFact1458.Bartender => RuntimeTownRescueFacts1458.Bartender,
        VanillaTownNpcRescueFact1458.Golfer => RuntimeTownRescueFacts1458.Golfer,
        VanillaTownNpcRescueFact1458.TaxCollector => RuntimeTownRescueFacts1458.TaxCollector,
        _ => throw new ArgumentOutOfRangeException(nameof(fact))
    };
}
