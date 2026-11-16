using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    internal bool OwnsWiringRandom(IWorldItemSpawnRandom random) =>
        random is SystemWorldItemSpawnRandom world &&
        naturalSpawnRandom is SystemVanillaNpcRandom npc && ReferenceEquals(world.SourceRandom, npc.SourceRandom) &&
        npcs.IsSpawnRandomSource(npc.SourceRandom);

    internal bool CanCreateWiringStatue(int style) =>
        (style != 9 || naturalSpawnWorldFacts is not null) &&
        VanillaNpcStatue1458.TryResolve(style, naturalSpawnWorldFacts?.PartyIsUp == true, out _, out _, out _) &&
        naturalSpawnRandom is SystemVanillaNpcRandom adapter && npcs.IsSpawnRandomSource(adapter.SourceRandom);

    internal int SelectWiringStatue(int style, int x, int y, out bool extraAllowed)
    {
        extraAllowed = false;
        if (!CanCreateWiringStatue(style) || naturalSpawnRandom is not SystemVanillaNpcRandom adapter) return 0;
        int type = VanillaNpcStatue1458.SelectBeforeCooldown(style, naturalSpawnWorldFacts?.PartyIsUp == true, adapter);
        extraAllowed = style != 63 || npcs.AllowsStatue(165, x, y);
        return type;
    }

    internal bool TryCreateWiringStatue(int style, int type, bool extraAllowed, int x, int y,
        Func<bool> blocked, Func<bool> currentSource, out bool smoke)
    {
        smoke = false;
        var context = CaptureSpawnContext();
        if (!CanCreateWiringStatue(style) || naturalSpawnRandom is not SystemVanillaNpcRandom adapter ||
            !VanillaNpcStatue1458.TryResolve(style, naturalSpawnWorldFacts?.PartyIsUp == true, out _, out int dx, out int dy) ||
            !npcs.TryCreateNaturalSpawnPreview(adapter.SourceRandom, out var preview) || preview is null ||
            !preview.AllowsStatue(type, x, y) || !extraAllowed) return false;
        if (VanillaNpcStatue1458.NeedsClearance(style, out _, out _, out _) && blocked())
        {
            VanillaPoofOfSmoke1458.ConsumeDedicatedSourceOffers(new SystemVanillaNpcRandom(preview.Random));
            if (!preview.ValidateContext() || !currentSource() || CaptureSpawnContext() != context || !preview.IsCurrentOwned() ||
                !preview.TryAdoptUnpublished(out var empty, out var retained) || empty is not null || retained is null) return false;
            retained.PublishRetention();
            smoke = true;
            return true;
        }
        if (style == 28) type = VanillaNpcStatue1458.SelectBird(new SystemVanillaNpcRandom(preview.Random));
        var update = new NpcStateUpdate(type, checked((short)type), 0, 0, 0, 0,
            byte.MaxValue, default, NpcSimulationState.Initial);
        if (!preview.TryStageStatue(in update, x + dx, y + dy, out var staged) ||
            !preview.ValidateContext() || !currentSource() || CaptureSpawnContext() != context || !preview.IsCurrentOwned() ||
            !preview.TryAdoptUnpublished(out var birth, out var publication) || birth != staged || publication is null) return false;
        if (birth is not null) AppliedSpawns++;
        // Wiring only sets spawnNeedsSyncing. The existing outer NPC update,
        // replacement or strike announces the retained generation later.
        publication.PublishRetention();
        return true;
    }
}
