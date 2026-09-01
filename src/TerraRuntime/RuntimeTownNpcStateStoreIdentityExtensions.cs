using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Compatibility projection for authoritative town-NPC identity. Housing lifecycle refactors may reshape the
/// state-store surface, but move-in replication still needs the identity committed in the persisted town-NPC row.
/// Keep the projection expressed through the store's stable TryGet read boundary instead of reaching into its
/// private dictionaries or reverting newer housing ownership changes.
/// </summary>
internal static class RuntimeTownNpcStateStoreIdentityExtensions
{
    public static bool TryGetIdentity(
        this RuntimeTownNpcStateStore store,
        short slot,
        out RuntimeTownNpcIdentityCommit commit)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!store.TryGet(slot, out WorldTownNpc npc))
        {
            commit = default;
            return false;
        }

        commit = new RuntimeTownNpcIdentityCommit(
            slot,
            npc.GivenName,
            npc.TownNpcVariationIndex ?? 0);
        return true;
    }
}
