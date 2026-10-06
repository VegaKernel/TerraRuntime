using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal sealed class VanillaPirateGhostNpcBehaviorStrategy1458 : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        if (TryPlan(in npc, in definition, context, out next, out bool fadeStrike) && !fadeStrike)
            return true;
        next = default;
        return false;
    }

    internal static bool TryPlan(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, out NpcStateUpdate next, out bool fadeStrike)
    {
        next = default;
        fadeStrike = false;
        if (npc.TypeIdentity != VanillaNpcIds.PirateGhost ||
            !definition.TryResolveHitbox(npc.Simulation, out var body) ||
            npc.Simulation.LiquidContact != NpcLiquidContactKind.None || npc.Simulation.Wet ||
            !npc.Simulation.NoGravity || !npc.Simulation.NoTileCollide || npc.Simulation.Confused)
            return false;
        NpcSnapshot aimedActor = npc;
        VanillaNpcTargetCandidate? aimed = null;
        if (npc.Target < byte.MaxValue && context.TryFindCandidate((byte)npc.Target, out var current) &&
            current.Active && !current.Dead && !current.Ghost)
            aimed = current;
        else if (context.TrySelectClosestTarget(in npc, in definition, out var selected) &&
            context.TryFindCandidate((byte)selected.Target, out var closest))
        {
            if (closest.NoAggro || closest.Aggro < 0) return false;
            int dx = (int)(closest.CenterX - closest.Width * .5f) + (int)closest.Width / 2 <
                npc.PositionX + body.Width / 2 ? -1 : 1;
            int dy = (int)(closest.CenterY - closest.Height * .5f) + (int)closest.Height / 2 <
                npc.PositionY + body.Height / 2 ? -1 : 1;
            aimedActor = npc with { Target = selected.Target,
                Simulation = npc.Simulation with { DirectionX = dx, DirectionY = dy } };
            aimed = closest;
        }
        else
        {
            // TargetClosest still reads the retained player rectangle when no eligible player was found.
            byte slot = npc.Target < byte.MaxValue ? (byte)npc.Target : (byte)0;
            if (!context.TryCaptureRawPlayer(slot, out var raw) ||
                !VanillaNpcUnoccupiedTarget1458.TryRefresh(npc.Target, npc.Simulation.DirectionX,
                    npc.Simulation.DirectionY, npc.PositionX, npc.PositionY, body.Width, body.Height,
                    raw.Facts, out var refresh) || !context.IsRawPlayerCurrent(in raw))
                return false;
            aimedActor = npc with { Target = refresh.Target,
                Simulation = npc.Simulation with { DirectionX = refresh.DirectionX, DirectionY = refresh.DirectionY } };
        }
        Span<NpcSnapshot> peers = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = context.CopyNpcPeers(npc.TypeIdentity, peers);
        // Executor snapshots are normally ordered, but standalone owned consumers may supply a compact roster.
        for (int index = 1; index < count; index++)
        {
            var value = peers[index];
            int at = index;
            while (at > 0 && peers[at - 1].Handle.Slot > value.Handle.Slot)
            {
                peers[at] = peers[at - 1];
                at--;
            }
            peers[at] = value;
        }
        if (!VanillaPirateGhostNpcCatalog1458.TryMove(in aimedActor, in body, aimed,
            peers[..count], out var motion, out fadeStrike))
            return false;
        next = new(aimedActor.Type, aimedActor.NetId, aimedActor.PositionX, aimedActor.PositionY,
            motion.VelocityX, motion.VelocityY, aimedActor.Target, aimedActor.Ai,
            aimedActor.Simulation with { Alpha = motion.Alpha, LocalAi = motion.LocalAi });
        return true;
    }
}
