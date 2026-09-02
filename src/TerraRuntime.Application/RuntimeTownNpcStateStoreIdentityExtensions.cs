namespace TerraRuntime;

internal static class RuntimeTownNpcStateStoreIdentityExtensions
{
    public static bool TryGetIdentity(
        this RuntimeTownNpcStateStore store,
        short slot,
        out RuntimeTownNpcIdentityCommit commit)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!store.TryGet(slot, out var npc))
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
