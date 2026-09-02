using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Source-shaped shared lifecycle helpers for NPC types 13/14/15. NPC.PlayerInteraction propagates a hit to every
/// active Eater segment, and DropEoWLoot promotes the dying segment to boss only when no other active Eater segment
/// remains. Both scans use the store's stable slot order and preserve generation-safe ledger keys.
/// </summary>
public static class VanillaEaterOfWorldsLifecycle
{
    public static bool IsSegment(NpcTypeId type) =>
        type == VanillaNpcIds.EaterOfWorldsHead ||
        type == VanillaNpcIds.EaterOfWorldsBody ||
        type == VanillaNpcIds.EaterOfWorldsTail;

    public static int MarkPlayerInteractionAcrossActiveSegments(
        RuntimeNpcStore store,
        RuntimeNpcPlayerInteractionLedger interactions,
        PlayerHandle player,
        Span<NpcSnapshot> activeBuffer)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(interactions);
        int count = store.CopyActive(activeBuffer);
        int marked = 0;
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot candidate = activeBuffer[index];
            if (IsSegment(candidate.TypeIdentity) && interactions.TryMark(candidate.Handle, player))
                marked++;
        }
        return marked;
    }

    public static bool IsLastActiveSegment(
        RuntimeNpcStore store,
        in NpcSnapshot dying,
        Span<NpcSnapshot> activeBuffer)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!IsSegment(dying.TypeIdentity))
            return false;

        int count = store.CopyActive(activeBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot candidate = activeBuffer[index];
            if (candidate.Handle != dying.Handle && IsSegment(candidate.TypeIdentity))
                return false;
        }
        return true;
    }
}
