using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal static class VanillaMoonLordLeechBehavior
{
    public static bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY,
            npc.Target, npc.Ai, npc.Simulation);
        if (!TryHead(in npc, context, out var head))
        {
            next = next with { Simulation = npc.Simulation with { Life = 0, TimeLeft = 0 } };
            return true;
        }
        float age = npc.Ai.Ai2 + 1f;
        next = next with { Ai = npc.Ai with { Ai2 = age } };
        if (age >= 90f)
        {
            next = next with { Simulation = npc.Simulation with { Life = 0, TimeLeft = 0 } };
            return true;
        }
        next = next with { VelocityX = 0f, VelocityY = 0f };
        if (context.ProjectileAnchors is null ||
            !context.ProjectileAnchors.TryGetProjectile(npc.Ai.Ai1, out var anchor) ||
            anchor.Type != VanillaProjectileIds.MoonLeech ||
            !VanillaNpcDefinitionCatalog.TryGet(head.TypeIdentity, head.NetIdentity, out var headDefinition) ||
            !headDefinition.TryResolveHitbox(head.Simulation, out var headSize) ||
            !definition.TryResolveHitbox(npc.Simulation, out var clotSize)) return true;
        float fromX = anchor.PositionX + 8f, fromY = anchor.PositionY + 8f;
        float toX = head.PositionX + headSize.Width * .5f;
        float toY = head.PositionY + headSize.Height * .5f + 216f;
        float fraction = age / 90f;
        next = next with
        {
            PositionX = fromX + (toX - fromX) * fraction - clotSize.Width * .5f,
            PositionY = fromY + (toY - fromY) * fraction - clotSize.Height * .5f
        };
        return true;
    }

    public static void ApplyHealing(in NpcSnapshot before, in NpcSnapshot committed,
        VanillaNpcBehaviorContext context, INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity != VanillaNpcIds.MoonLordLeechBlob || committed.Type != before.Type ||
            committed.Ai.Ai2 < 90f || committed.Ai.Ai2 != before.Ai.Ai2 + 1f ||
            committed.Simulation.Life != 0 || committed.Simulation.TimeLeft != 0 ||
            !TryHead(in before, context, out var head)) return;
        int rootSlot = (int)head.Ai.Ai3;
        NpcHandle core = default, left = default, right = default;
        if ((uint)rootSlot < RuntimeNpcStore.MaximumAddressableCapacity &&
            context.TryFindNpcPeer((byte)rootSlot, out var root)) core = root.Handle;
        Span<NpcSnapshot> hands = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = context.CopyNpcPeers(VanillaNpcIds.MoonLordHand, hands);
        for (int i = 0; i < count; i++)
        {
            var hand = hands[i];
            if (hand.Ai.Ai3 != rootSlot) continue;
            if (!left.IsAssigned && hand.Ai.Ai2 == 0f) left = hand.Handle;
            if (!right.IsAssigned && hand.Ai.Ai2 == 1f) right = hand.Handle;
        }
        int remaining = 1000;
        remaining -= mutations.TryHeal(head.Handle, remaining);
        remaining -= mutations.TryHeal(core, remaining);
        remaining -= mutations.TryHeal(left, remaining);
        _ = mutations.TryHeal(right, remaining);
    }

    private static bool TryHead(in NpcSnapshot npc, VanillaNpcBehaviorContext context, out NpcSnapshot head)
    {
        int slot = (int)MathF.Abs(npc.Ai.Ai0) - 1;
        head = default;
        return (uint)slot < RuntimeNpcStore.MaximumAddressableCapacity &&
            context.TryFindNpcPeer((byte)slot, out head) && head.TypeIdentity == VanillaNpcIds.MoonLordHead;
    }
}
