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

    internal bool TryCreateWiringStatue(int style, int x, int y, Func<bool> currentSource)
    {
        var context = CaptureSpawnContext();
        if (!CanCreateWiringStatue(style) || naturalSpawnRandom is not SystemVanillaNpcRandom adapter ||
            !VanillaNpcStatue1458.TryResolve(style, naturalSpawnWorldFacts?.PartyIsUp == true, out int type, out int dx, out int dy) ||
            !npcs.TryCreateNaturalSpawnPreview(adapter.SourceRandom, out var preview) || preview is null ||
            !preview.AllowsStatue(type, x, y)) return false;
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
